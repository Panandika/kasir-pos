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
    // Phase 6 invariant: adding cloud_synced / cloud_synced_at columns to sync_queue
    // must NOT change what PushService.SignPayload hashes. The signed payload is
    // derived from SELECT * on DATA tables (products, sales, ...) via FetchRowData,
    // never from sync_queue columns. This fixture proves that empirically by
    // inspecting the signed JSON file written by PushService.
    [TestFixture]
    public class PushServiceHmacInvarianceTests
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
            _clock = new FakeClock(new System.DateTime(2026, 4, 25, 10, 0, 0));

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
        public void SignedPayload_Does_Not_Contain_CloudSynced_Fields()
        {
            // Seed a product row — this is what ends up in the signed batch.
            using (var cmd = _db.CreateCommand())
            {
                cmd.CommandText = @"
                    INSERT INTO departments (dept_code, name) VALUES ('DEPT1', 'Test Dept');
                    INSERT INTO products (product_code, name, unit, price, buying_price, dept_code)
                    VALUES ('P1', 'Test Product', 'pcs', 10000, 5000, 'DEPT1');";
                cmd.ExecuteNonQuery();
            }

            // The INSERT triggers on Schema.sql will have already added sync_queue rows
            // (with cloud_synced=0). Run PushService.Push() to produce the signed file.
            var result = _push.Push();
            result.Success.Should().BeTrue(result.Error);
            result.EventCount.Should().BeGreaterThan(0);

            // Capture the signed JSON that was written
            var json = _fileWriter.Files.Values.Single();

            // Invariant #1: the signed payload must NOT contain cloud_synced key anywhere.
            // It's a sync_queue column, not a data-table column, so it must never appear
            // in the events' Data dictionaries.
            json.Should().NotContain("cloud_synced",
                "cloud_synced lives on sync_queue, never on data tables, so it must not leak into the signed payload");
            json.Should().NotContain("cloud_synced_at",
                "cloud_synced_at lives on sync_queue, never on data tables");

            // Invariant #2: each event's Data dict contains only the DATA table columns,
            // not sync_queue bookkeeping columns.
            var batch = JsonConvert.DeserializeObject<SyncBatch>(json);
            batch.Signature.Should().NotBeNullOrEmpty();
            batch.Events.Should().NotBeEmpty();
            foreach (var evt in batch.Events)
            {
                if (evt.Data == null) continue;
                evt.Data.Keys.Should().NotContain("cloud_synced");
                evt.Data.Keys.Should().NotContain("cloud_synced_at");
                // Negative-control: Data SHOULD contain real data-table columns
                // (e.g. products.product_code) so we haven't accidentally zeroed out Data.
                if (evt.TableName == "products")
                {
                    evt.Data.Keys.Should().Contain("product_code");
                }
            }
        }

        [Test]
        public void CloudSynced_Flag_Changes_Do_Not_Enqueue_Rows_For_LAN_Sync()
        {
            // Touching cloud_synced on an already LAN-synced row must NOT cause a
            // fresh sync_queue entry or a new PushService run. This is the flip side
            // of the invariant: cloud bookkeeping is orthogonal to LAN sync, so
            // mutations on it never enter the HMAC-signed payload.
            using (var cmd = _db.CreateCommand())
            {
                cmd.CommandText = @"
                    INSERT INTO departments (dept_code, name) VALUES ('DEPT1', 'Test');
                    INSERT INTO products (product_code, name, unit, price, buying_price, dept_code)
                    VALUES ('P1', 'Test', 'pcs', 10000, 5000, 'DEPT1');";
                cmd.ExecuteNonQuery();
            }

            var first = _push.Push();
            first.Success.Should().BeTrue(first.Error);
            first.EventCount.Should().BeGreaterThan(0);
            _fileWriter.Files.Clear();

            // Flip cloud_synced on every queue row. This is what the cloud worker does
            // after a successful Supabase upsert — it must have zero effect on LAN sync.
            using (var cmd = _db.CreateCommand())
            {
                cmd.CommandText = "UPDATE sync_queue SET cloud_synced = 1, cloud_synced_at = datetime('now');";
                cmd.ExecuteNonQuery();
            }

            // A second Push with no intervening data changes must find no pending rows
            // and therefore produce zero files. If cloud_synced leaked into sync_queue
            // trigger logic or changed the 'status' column, this would fail.
            var second = _push.Push();
            second.Success.Should().BeTrue(second.Error);
            second.EventCount.Should().Be(0,
                "cloud_synced flips never re-enqueue data for LAN sync");
            _fileWriter.Files.Should().BeEmpty();
        }
    }
}
