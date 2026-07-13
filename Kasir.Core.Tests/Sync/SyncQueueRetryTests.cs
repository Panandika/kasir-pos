using System;
using System.IO;
using Microsoft.Data.Sqlite;
using NUnit.Framework;
using FluentAssertions;
using Kasir.Data.Repositories;
using Kasir.Sync;
using Kasir.Tests.TestHelpers;
using Kasir.Tests.TestHelpers.Fakes;

namespace Kasir.Tests.Sync
{
    // F05 + F14: a transient SMB transport failure used to mark queue rows 'failed'
    // forever — GetPending only saw 'pending', and nothing reset failed rows, so one
    // outage permanently stranded up to a batch of sales/purchases. Failed rows must be
    // retried under a cap, then parked as terminal 'dead'.
    [TestFixture]
    public class SyncQueueRetryTests
    {
        private SqliteConnection _db;

        [SetUp]
        public void SetUp()
        {
            _db = TestDb.Create();
            var cfg = new ConfigRepository(_db);
            cfg.Set("register_id", "01");
            cfg.Set("sync_hub_share", "C:\\kasir\\sync");
            cfg.Set("sync_hmac_key", "test-secret-key-32bytes!!");
        }

        [TearDown]
        public void TearDown()
        {
            _db?.Close();
            _db?.Dispose();
        }

        private void InsertSale(string journalNo)
        {
            using var cmd = _db.CreateCommand();
            cmd.CommandText =
                "INSERT INTO sales (doc_type, journal_no, doc_date, period_code) VALUES ('SALE', @jn, '2026-04-25', '202604')";
            cmd.Parameters.AddWithValue("@jn", journalNo);
            cmd.ExecuteNonQuery();
        }

        private (string status, int retry) QueueRow(string recordKey)
        {
            using var cmd = _db.CreateCommand();
            cmd.CommandText = "SELECT status, retry_count FROM sync_queue WHERE record_key=@k";
            cmd.Parameters.AddWithValue("@k", recordKey);
            using var r = cmd.ExecuteReader();
            r.Read();
            return (r.GetString(0), r.GetInt32(1));
        }

        private sealed class ThrowingWriter : ISyncFileWriter
        {
            public void Write(string path, string content) => throw new IOException("SMB share unreachable");
            public void SafeMove(string tempPath, string destPath) { }
        }

        [Test]
        public void Transport_Failure_Marks_Failed_Then_Next_Push_Retries_Successfully()
        {
            InsertSale("KLR-01-2601-0001");
            var clock = new FakeClock(new DateTime(2026, 4, 25, 10, 0, 0));

            // First push: SMB unreachable -> rows marked failed (not stranded).
            var failed = new PushService(_db, new ThrowingWriter(), clock).Push();
            failed.Success.Should().BeFalse();
            var afterFail = QueueRow("KLR-01-2601-0001");
            afterFail.status.Should().Be("failed");
            afterFail.retry.Should().Be(1);

            // Second push once the share is back: the failed row is retried and synced.
            var writer = new FakeSyncFileWriter();
            var ok = new PushService(_db, writer, clock).Push();
            ok.Success.Should().BeTrue(ok.Error);
            ok.EventCount.Should().Be(1, "the previously failed row must be retried, not stranded");
            QueueRow("KLR-01-2601-0001").status.Should().Be("synced");
        }

        [Test]
        public void Repeated_Failures_Park_Row_As_Dead_And_Stop_Retrying()
        {
            InsertSale("KLR-01-POISON");
            var repo = new SyncQueueRepository(_db);
            int id = repo.GetPending("01", 10)[0].Id;

            for (int i = 0; i < SyncConfig.MaxRetries; i++)
            {
                repo.MarkFailed(id, "transport error");
            }

            var row = QueueRow("KLR-01-POISON");
            row.status.Should().Be("failed");
            row.retry.Should().Be(SyncConfig.MaxRetries);
            repo.GetPending("01", 10).Should().BeEmpty("rows past the retry cap are no longer retried");
            repo.GetDead("01").Should().ContainSingle(e => e.RecordKey == "KLR-01-POISON",
                "poison rows are surfaced via GetDead for manual attention");
        }
    }
}
