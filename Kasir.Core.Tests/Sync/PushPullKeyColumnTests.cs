using System;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
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
    // F04: transaction-table sync triggers enqueue record_key = journal_no, but
    // GetKeyColumn used to map them to 'id' — so FetchRowData never matched and every
    // sale/purchase/cash event was silently dropped and marked synced.
    // F24: PullService.ApplyInsert replicated the source register's autoincrement id
    // into the local PK, so INSERT OR IGNORE dropped cross-register id collisions.
    [TestFixture]
    public class PushPullKeyColumnTests
    {
        private SqliteConnection _db;

        [SetUp]
        public void SetUp()
        {
            _db = TestDb.Create();
        }

        [TearDown]
        public void TearDown()
        {
            _db?.Close();
            _db?.Dispose();
        }

        private void Cfg(string reg)
        {
            var cfg = new ConfigRepository(_db);
            cfg.Set("register_id", reg);
            cfg.Set("sync_hub_share", "C:\\kasir\\sync");
            cfg.Set("sync_hmac_key", "test-secret-key-32bytes!!");
        }

        private void InsertSale(string journalNo, int? id = null)
        {
            using var cmd = _db.CreateCommand();
            if (id.HasValue)
            {
                cmd.CommandText =
                    "INSERT INTO sales (id, doc_type, journal_no, doc_date, period_code) VALUES (@id, 'SALE', @jn, '2026-04-25', '202604')";
                cmd.Parameters.AddWithValue("@id", id.Value);
            }
            else
            {
                cmd.CommandText =
                    "INSERT INTO sales (doc_type, journal_no, doc_date, period_code) VALUES ('SALE', @jn, '2026-04-25', '202604')";
            }
            cmd.Parameters.AddWithValue("@jn", journalNo);
            cmd.ExecuteNonQuery();
        }

        private string QueueStatus(string recordKey)
        {
            using var cmd = _db.CreateCommand();
            cmd.CommandText = "SELECT status FROM sync_queue WHERE table_name='sales' AND record_key=@k";
            cmd.Parameters.AddWithValue("@k", recordKey);
            return cmd.ExecuteScalar() as string;
        }

        [Test]
        public void Sale_Is_Fetched_By_JournalNo_And_Pushed()
        {
            Cfg("01");
            InsertSale("KLR-01-2601-0001"); // trigger enqueues record_key = journal_no

            var writer = new FakeSyncFileWriter();
            var push = new PushService(_db, writer, new FakeClock(new DateTime(2026, 4, 25, 10, 0, 0)));

            var result = push.Push();

            result.Success.Should().BeTrue(result.Error);
            result.EventCount.Should().Be(1, "the sale must be resolved by journal_no and included in the batch");

            var batch = JsonConvert.DeserializeObject<SyncBatch>(writer.Files.Values.Single());
            var saleEvt = batch.Events.Single(e => e.TableName == "sales");
            saleEvt.Data.Should().NotBeNull("FetchRowData must find the row by journal_no");
            saleEvt.Data["journal_no"].ToString().Should().Be("KLR-01-2601-0001");

            // The queue row is legitimately synced because it was actually pushed.
            QueueStatus("KLR-01-2601-0001").Should().Be("synced");
        }

        [Test]
        public void IU_Event_With_Unresolvable_Key_Is_MarkedFailed_Not_Synced()
        {
            Cfg("01");
            // A pending I event whose sales row does not exist (key mismatch / deleted).
            using (var cmd = _db.CreateCommand())
            {
                cmd.CommandText =
                    "INSERT INTO sync_queue (register_id, table_name, record_key, operation, status) " +
                    "VALUES ('01', 'sales', 'KLR-01-GONE', 'I', 'pending')";
                cmd.ExecuteNonQuery();
            }

            var writer = new FakeSyncFileWriter();
            var push = new PushService(_db, writer, new FakeClock(new DateTime(2026, 4, 25, 10, 0, 0)));

            var result = push.Push();

            result.Success.Should().BeTrue(result.Error);
            result.EventCount.Should().Be(0, "no row could be resolved, so nothing is batched");
            QueueStatus("KLR-01-GONE").Should().Be("failed",
                "an unresolvable I/U event must be marked failed (visible), never synced (silent loss)");
        }

        [Test]
        public void Pull_Insert_With_Colliding_Source_Id_Keeps_Both_Rows()
        {
            Cfg("02"); // this register pulls from register 01
            InsertSale("KLR-02-LOCAL", id: 1); // local sale occupies PK id=1

            // Remote batch from register 01 carries a DIFFERENT sale that happens to
            // share the source autoincrement id=1 but has a distinct journal_no.
            var batch = new SyncBatch
            {
                RegisterId = "01",
                SchemaVersion = SyncConfig.SchemaVersion,
                Timestamp = "2026-04-25 12:00:00",
                BatchId = "abc12345"
            };
            var evt = new SyncEvent
            {
                QueueId = 1,
                TableName = "sales",
                RecordKey = "KLR-01-REMOTE",
                Operation = "I"
            };
            evt.Data["id"] = 1;              // colliding source PK — must be ignored locally
            evt.Data["doc_type"] = "SALE";
            evt.Data["journal_no"] = "KLR-01-REMOTE";
            evt.Data["doc_date"] = "2026-04-25";
            evt.Data["period_code"] = "202604";
            batch.Events.Add(evt);

            var reader = new FakeSyncFileReader();
            reader.Files["C:\\kasir\\sync\\outbox\\01_20260425_120000_abc12345.json"] = SignAndSerialize(batch);

            var pull = new PullService(_db, reader);
            var result = pull.Pull();

            result.AppliedCount.Should().Be(1);

            using var cmd = _db.CreateCommand();
            cmd.CommandText = "SELECT COUNT(*) FROM sales";
            Convert.ToInt32(cmd.ExecuteScalar()).Should().Be(2,
                "the colliding source id must not cause INSERT OR IGNORE to drop the remote sale");

            cmd.CommandText = "SELECT COUNT(*) FROM sales WHERE journal_no IN ('KLR-02-LOCAL','KLR-01-REMOTE')";
            Convert.ToInt32(cmd.ExecuteScalar()).Should().Be(2, "both journal numbers must survive");
        }

        private string SignAndSerialize(SyncBatch batch)
        {
            batch.Signature = null;
            string payloadJson = JsonConvert.SerializeObject(batch, Formatting.None);
            using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes("test-secret-key-32bytes!!"));
            batch.Signature = Convert.ToBase64String(hmac.ComputeHash(Encoding.UTF8.GetBytes(payloadJson)));
            return JsonConvert.SerializeObject(batch, Formatting.None);
        }
    }
}
