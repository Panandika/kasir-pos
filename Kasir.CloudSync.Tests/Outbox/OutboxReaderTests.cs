using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;
using NUnit.Framework;
using FluentAssertions;
using Kasir.CloudSync.Outbox;
using Kasir.CloudSync.Tests.TestHelpers;
using Kasir.Data.Repositories;

namespace Kasir.CloudSync.Tests.Outbox
{
    [TestFixture]
    public class OutboxReaderTests
    {
        private SqliteConnection _db;
        private SyncQueueRepository _repo;
        private InMemoryProductSink _sink;
        private OutboxReader _reader;

        [SetUp]
        public void SetUp()
        {
            _db = TestDb.Create();
            _repo = new SyncQueueRepository(_db);
            _sink = new InMemoryProductSink();
            _reader = new OutboxReader(_db, _repo, _sink, NullLogger<OutboxReader>.Instance);

            Exec("INSERT INTO departments (dept_code, name) VALUES ('D1','Dept 1');");
        }

        [TearDown]
        public void TearDown() { _db?.Close(); _db?.Dispose(); }

        private void Exec(string sql)
        {
            using var cmd = _db.CreateCommand();
            cmd.CommandText = sql;
            cmd.ExecuteNonQuery();
        }

        private void InsertQueueRow(string key, string status, int cloudSynced = 0)
        {
            Exec($@"INSERT INTO sync_queue (register_id, table_name, record_key, operation, status, synced_at, cloud_synced)
                    VALUES ('01','products','{key}','I','{status}',
                            CASE WHEN '{status}'='synced' THEN datetime('now') ELSE NULL END,
                            {cloudSynced});");
        }

        [Test]
        public async Task TickAsync_Ships_LAN_Synced_Rows_And_Marks_CloudSynced()
        {
            Exec("INSERT INTO products (product_code, name, dept_code, price) VALUES ('P1','Sampo','D1', 15000);");
            InsertQueueRow("P1", status: "synced");

            var shipped = await _reader.TickAsync(batchSize: 10, ct: CancellationToken.None);

            shipped.Should().Be(1);
            _sink.Upserted.Should().ContainSingle().Which.ProductCode.Should().Be("P1");
            _sink.Upserted[0].Price.Should().Be(15000L);

            // A second tick finds no pending cloud work
            var second = await _reader.TickAsync(batchSize: 10, ct: CancellationToken.None);
            second.Should().Be(0);
            _sink.UpsertCallCount.Should().Be(1, "no upsert call when there's nothing to ship");
        }

        [Test]
        public async Task TickAsync_Skips_Rows_That_Have_Not_Completed_LAN_Sync()
        {
            Exec("INSERT INTO products (product_code, name, dept_code) VALUES ('P-ready','X','D1');");
            Exec("INSERT INTO products (product_code, name, dept_code) VALUES ('P-pending','Y','D1');");
            InsertQueueRow("P-ready", status: "synced");
            InsertQueueRow("P-pending", status: "pending");

            await _reader.TickAsync(10, CancellationToken.None);

            _sink.Upserted.Should().ContainSingle()
                .Which.ProductCode.Should().Be("P-ready");
        }

        [Test]
        public async Task TickAsync_Does_Not_Mark_CloudSynced_On_Sink_Failure()
        {
            Exec("INSERT INTO products (product_code, name, dept_code) VALUES ('P1','X','D1');");
            InsertQueueRow("P1", status: "synced");
            _sink.ThrowOnNextUpsert = new Exception("network boom");

            var shipped = await _reader.TickAsync(10, CancellationToken.None);

            shipped.Should().Be(0, "sink threw, nothing shipped");
            _sink.Upserted.Should().BeEmpty();

            // Row must still be cloud-pending for retry
            var pending = _repo.GetPendingCloud(10);
            pending.Should().ContainSingle().Which.RecordKey.Should().Be("P1");
        }

        [Test]
        public async Task TickAsync_Handles_Delete_Operations_With_Tombstone()
        {
            // Seed only the queue row with op='D' — the underlying products row
            // is already gone in a DELETE scenario.
            Exec(@"INSERT INTO sync_queue (register_id, table_name, record_key, operation, status, synced_at, cloud_synced)
                   VALUES ('01','products','P-GONE','D','synced', datetime('now'), 0);");

            await _reader.TickAsync(10, CancellationToken.None);

            _sink.Upserted.Should().ContainSingle();
            _sink.Upserted[0].ProductCode.Should().Be("P-GONE");
            _sink.Upserted[0].Status.Should().Be("D", "soft-delete via status='D'");
        }

        [Test]
        public async Task TickAsync_Ignores_Non_Product_Tables_In_Phase_A()
        {
            Exec(@"INSERT INTO sync_queue (register_id, table_name, record_key, operation, status, synced_at, cloud_synced)
                   VALUES ('01','sales','S1','I','synced', datetime('now'), 0);");

            var shipped = await _reader.TickAsync(10, CancellationToken.None);

            shipped.Should().Be(0);
            _sink.Upserted.Should().BeEmpty();

            // The sales row stays cloud-pending for a future phase's mapper
            var pending = _repo.GetPendingCloud(10);
            pending.Should().ContainSingle().Which.TableName.Should().Be("sales");
        }
    }
}
