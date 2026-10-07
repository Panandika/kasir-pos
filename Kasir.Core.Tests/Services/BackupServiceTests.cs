using System;
using System.IO;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using NUnit.Framework;
using Kasir.Services;

namespace Kasir.Tests.Services
{
    // F07: kasir.db runs in WAL mode, so recent commits live in kasir.db-wal until a
    // checkpoint. A plain File.Copy of kasir.db misses them; the backup must not.
    [TestFixture]
    public class BackupServiceTests
    {
        private string _dir, _src, _dest;
        private SqliteConnection _live;

        [SetUp]
        public void SetUp()
        {
            _dir = Path.Combine(Path.GetTempPath(), "kasir-backup-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_dir);
            _src = Path.Combine(_dir, "kasir.db");
            _dest = Path.Combine(_dir, "out", "kasir_backup.db");
            Directory.CreateDirectory(Path.GetDirectoryName(_dest));
        }

        [TearDown]
        public void TearDown()
        {
            _live?.Dispose();
            SqliteConnection.ClearAllPools();
            try { Directory.Delete(_dir, true); } catch { /* best-effort */ }
        }

        // Opens the "live" register DB in WAL mode with auto-checkpoint off and keeps the
        // connection open, so committed rows stay in kasir.db-wal (like a running POS).
        private void CreateLiveWalDb(int rows)
        {
            _live = new SqliteConnection($"Data Source={_src};Pooling=False");
            _live.Open();
            Exec(_live, "PRAGMA journal_mode=WAL; PRAGMA wal_autocheckpoint=0;");
            Exec(_live, "CREATE TABLE sales (id INTEGER PRIMARY KEY, total INTEGER NOT NULL);");
            for (int i = 1; i <= rows; i++)
                Exec(_live, $"INSERT INTO sales (id, total) VALUES ({i}, {i * 100});");
        }

        private static void Exec(SqliteConnection c, string sql)
        {
            using var cmd = c.CreateCommand();
            cmd.CommandText = sql;
            cmd.ExecuteNonQuery();
        }

        private static object Scalar(string path, string sql)
        {
            using var c = new SqliteConnection($"Data Source={path};Mode=ReadOnly;Pooling=False");
            c.Open();
            using var cmd = c.CreateCommand();
            cmd.CommandText = sql;
            return cmd.ExecuteScalar();
        }

        [Test]
        public void BackupTo_IncludesRowsStillInWalFile()
        {
            CreateLiveWalDb(rows: 50);
            new FileInfo(_src + "-wal").Length.Should().BeGreaterThan(0,
                "precondition: the rows must still be un-checkpointed in the -wal file");

            BackupService.BackupTo(_src, _dest);

            Convert.ToInt64(Scalar(_dest, "SELECT COUNT(*) FROM sales")).Should().Be(50);
            Convert.ToInt64(Scalar(_dest, "SELECT SUM(total) FROM sales")).Should().Be(127500);
        }

        [Test]
        public void BackupTo_ProducesSelfContainedFile_NotInWalMode()
        {
            CreateLiveWalDb(rows: 3);

            BackupService.BackupTo(_src, _dest);

            // A backup is a single file the owner copies to a flash drive; it must not
            // depend on (or spawn) a -wal sidecar.
            File.Exists(_dest + "-wal").Should().BeFalse();
            ((string)Scalar(_dest, "PRAGMA journal_mode")).Should().Be("delete");
            ((string)Scalar(_dest, "PRAGMA integrity_check")).Should().Be("ok");
        }

        [Test]
        public void BackupTo_DestinationExists_ThrowsAndLeavesItUntouched()
        {
            CreateLiveWalDb(rows: 1);
            File.WriteAllText(_dest, "older backup");

            Action act = () => BackupService.BackupTo(_src, _dest);

            act.Should().Throw<IOException>();
            File.ReadAllText(_dest).Should().Be("older backup");
        }

        [Test]
        public void BackupTo_SourceNotADatabase_LeavesNoFileBehind()
        {
            File.WriteAllText(_src, new string('x', 8192));

            Action act = () => BackupService.BackupTo(_src, _dest);

            act.Should().Throw<Exception>();
            File.Exists(_dest).Should().BeFalse();
            Directory.GetFiles(Path.GetDirectoryName(_dest)).Should().BeEmpty(
                "a failed backup must not leave a half-written or temp file");
        }

        [Test]
        public void BackupTo_SourceMissing_Throws()
        {
            Action act = () => BackupService.BackupTo(_src, _dest);

            act.Should().Throw<FileNotFoundException>();
            File.Exists(_dest).Should().BeFalse();
        }
    }
}
