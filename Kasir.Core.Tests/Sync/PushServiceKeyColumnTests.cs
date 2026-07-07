using System;
using System.Linq;
using Microsoft.Data.Sqlite;
using Newtonsoft.Json;
using NUnit.Framework;
using FluentAssertions;
using Kasir.Data.Repositories;
using Kasir.Sync;
using Kasir.Tests.TestHelpers;
using Kasir.Tests.TestHelpers.Fakes;

namespace Kasir.Tests.Sync
{
    // F04 — transaction-table sync events were silently dropped. The AFTER-INSERT
    // triggers enqueue record_key = NEW.journal_no, but PushService.GetKeyColumn
    // mapped the 7 transaction tables to "id", so FetchRowData ran
    // WHERE [id] = <journal_no> (TEXT vs INTEGER PK), matched nothing, and Push()
    // marked every entry synced anyway. These tests pin the fix:
    //   1. a sale must serialize into the batch, fetched by journal_no (not id); and
    //   2. an entry whose row cannot be fetched must be marked failed (visible),
    //      not silently synced.
    [TestFixture]
    public class PushServiceKeyColumnTests
    {
        private SqliteConnection _db;
        private FakeSyncFileWriter _fileWriter;
        private FakeClock _clock;
        private PushService _push;

        [SetUp]
        public void SetUp()
        {
            _db = TestDb.Create();
            _fileWriter = new FakeSyncFileWriter();
            _clock = new FakeClock(new DateTime(2026, 7, 7, 10, 0, 0));

            var cfg = new ConfigRepository(_db);
            cfg.Set("register_id", "01");
            cfg.Set("sync_hub_share", "C:\\kasir\\sync");
            cfg.Set("sync_hmac_key", "test-secret-key-32bytes!!");

            _push = new PushService(_db, _fileWriter, _clock);
        }

        [TearDown]
        public void TearDown()
        {
            _db?.Close();
            _db?.Dispose();
        }

        [Test]
        public void Push_RoundTripsSaleByJournalNo_NotById()
        {
            // Insert a sale. trg_sales_sync_i enqueues record_key = NEW.journal_no.
            using (var cmd = _db.CreateCommand())
            {
                cmd.CommandText = @"
                    INSERT INTO sales (doc_type, journal_no, doc_date, period_code, register_id, total_value)
                    VALUES ('SALE', 'KLR01-2607-0001', '2026-07-07', '202607', '01', 50000);";
                cmd.ExecuteNonQuery();
            }

            // The trigger must have enqueued the journal_no as record_key (not the id).
            using (var cmd = _db.CreateCommand())
            {
                cmd.CommandText = "SELECT record_key FROM sync_queue WHERE table_name = 'sales'";
                var recordKey = (string)cmd.ExecuteScalar();
                recordKey.Should().Be("KLR01-2607-0001",
                    "the sales sync trigger enqueues the business key, not the surrogate id");
            }

            var result = _push.Push();
            result.Success.Should().BeTrue(result.Error);

            // The serialized batch must contain the sales event with a populated Data
            // row fetched by journal_no. Pre-fix the event is omitted (Data null) — red.
            var json = _fileWriter.Files.Values.Single();
            var batch = JsonConvert.DeserializeObject<SyncBatch>(json);
            var saleEvt = batch.Events.Single(
                e => e.TableName == "sales" && e.RecordKey == "KLR01-2607-0001");
            saleEvt.Data.Should().NotBeNull("FetchRowData must find the row by journal_no");
            Convert.ToString(saleEvt.Data["journal_no"]).Should().Be("KLR01-2607-0001");

            // The row was actually serialized -> it should be marked synced.
            QueueStatusFor("sales").Should().Be("synced");
        }

        [Test]
        public void Push_MarksUnfindableRow_Failed_NotSynced()
        {
            // A sync_queue row whose record_key does not exist in the table. The event
            // cannot be serialized (FetchRowData returns null). It must NOT be silently
            // marked synced — mark it failed so the drop is visible.
            using (var cmd = _db.CreateCommand())
            {
                cmd.CommandText = @"
                    INSERT INTO sync_queue (register_id, table_name, record_key, operation)
                    VALUES ('01', 'sales', 'NO-SUCH-JOURNAL', 'I');";
                cmd.ExecuteNonQuery();
            }

            var result = _push.Push();
            result.Success.Should().BeTrue(result.Error);

            QueueStatusFor("sales").Should().Be("failed",
                "an unfindable row must be marked failed (visible), not silently synced");
        }

        private string QueueStatusFor(string table)
        {
            using (var cmd = _db.CreateCommand())
            {
                cmd.CommandText = "SELECT status FROM sync_queue WHERE table_name = @t";
                cmd.Parameters.AddWithValue("@t", table);
                return (string)cmd.ExecuteScalar();
            }
        }
    }
}
