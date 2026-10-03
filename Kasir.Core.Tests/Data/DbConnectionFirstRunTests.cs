using System;
using System.IO;
using System.Reflection;
using Microsoft.Data.Sqlite;
using NUnit.Framework;
using FluentAssertions;
using Kasir.Data;

namespace Kasir.Tests.Data
{
    // Regression for KLR-01 (2026-10-03): a background poller opened kasir.db before
    // first-run, leaving an empty 4 KB kasir.db. InitializeDatabase then skipped first-run,
    // never moved the cloud-import staging file into place, and failed validation with
    // "Missing required tables".
    [TestFixture]
    [NonParallelizable]
    public class DbConnectionFirstRunTests
    {
        private static readonly string DataDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "data");
        private static readonly string DbPath = Path.Combine(DataDir, "kasir.db");
        private string _tempDir;

        [SetUp]
        public void SetUp()
        {
            ResetDbConnection();
            if (Directory.Exists(DataDir)) Directory.Delete(DataDir, true);
            _tempDir = Path.Combine(Path.GetTempPath(), "kasir-firstrun-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_tempDir);
        }

        [TearDown]
        public void TearDown()
        {
            ResetDbConnection();
            if (Directory.Exists(DataDir)) Directory.Delete(DataDir, true);
            if (Directory.Exists(_tempDir)) Directory.Delete(_tempDir, true);
        }

        private static void ResetDbConnection()
        {
            DbConnection.CloseConnection();
            SqliteConnection.ClearAllPools();
            typeof(DbConnection).GetProperty(nameof(DbConnection.IsInitialized))
                .SetValue(null, false);
            DbConnection.FirstRunHandler = null;
        }

        // Reproduces what the old printer-status poller left behind: a WAL-mode
        // SQLite file with no tables.
        private static void CreateStrayEmptyDb(string path)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            using (var conn = new SqliteConnection("Data Source=" + path + ";Pooling=False"))
            {
                conn.Open();
                using (var cmd = conn.CreateCommand())
                {
                    cmd.CommandText = "PRAGMA journal_mode=WAL;";
                    cmd.ExecuteNonQuery();
                }
            }
        }

        private static long TableCount(string path)
        {
            using (var conn = new SqliteConnection("Data Source=" + path + ";Pooling=False"))
            {
                conn.Open();
                using (var cmd = conn.CreateCommand())
                {
                    cmd.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='users'";
                    return Convert.ToInt64(cmd.ExecuteScalar());
                }
            }
        }

        [Test]
        public void CreateConnection_BeforeInitialize_ThrowsAndDoesNotCreateFile()
        {
            Action open = () => DbConnection.CreateConnection();

            open.Should().Throw<InvalidOperationException>();
            File.Exists(DbPath).Should().BeFalse();
        }

        [Test]
        public void IsFreshDatabaseFile_TablelessSqliteFile_IsFresh()
        {
            string path = Path.Combine(_tempDir, "empty.db");
            CreateStrayEmptyDb(path);

            new FileInfo(path).Length.Should().BeGreaterThan(0);
            DbConnection.IsFreshDatabaseFile(path).Should().BeTrue();
        }

        [Test]
        public void IsFreshDatabaseFile_NotSqlite_IsNotFresh()
        {
            string path = Path.Combine(_tempDir, "garbage.db");
            File.WriteAllText(path, "this is not a database file at all, just text padding padding");

            DbConnection.IsFreshDatabaseFile(path).Should().BeFalse();
        }

        [Test]
        public void InitializeDatabase_StrayEmptyDb_StillImportsCloudStaging()
        {
            // Build a real store database to act as the cloud-import staging file.
            DbConnection.FirstRunHandler = () => new FirstRunResult { Choice = "seed" };
            DbConnection.InitializeDatabase();
            ResetDbConnection();
            string staging = Path.Combine(_tempDir, "kasir.db.cloud-staging");
            File.Copy(DbPath, staging);
            Directory.Delete(DataDir, true);

            CreateStrayEmptyDb(DbPath);
            DbConnection.IsFreshInstall().Should().BeTrue();

            bool handlerCalled = false;
            DbConnection.FirstRunHandler = () =>
            {
                handlerCalled = true;
                return new FirstRunResult { Choice = "import", ImportPath = staging };
            };

            DbConnection.InitializeDatabase();

            handlerCalled.Should().BeTrue();
            DbConnection.IsInitialized.Should().BeTrue();
            SqliteConnection.ClearAllPools();
            TableCount(DbPath).Should().Be(1);
        }
    }
}
