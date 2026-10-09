using System;
using System.IO;
using System.Linq;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using NUnit.Framework;
using Kasir.Data;

namespace Kasir.Tests.Data
{
    [TestFixture]
    public class MigrationRunnerTests
    {
        [Test]
        public void Versions_AreContiguousAndAscending_From2()
        {
            // The runner only applies versions above schema_version, so a gap (e.g. a
            // build that ships 013 without 012) makes the missing one skip forever on
            // registers that reach the higher version first.
            var versions = MigrationRunner.Versions.ToList();

            versions.Should().NotBeEmpty();
            versions.First().Should().Be(2);
            versions.Should().Equal(Enumerable.Range(2, versions.Count),
                "migration versions must be contiguous and in ascending order");
            MigrationRunner.LatestVersion.Should().Be(versions.Last());
        }

        [Test]
        public void BackupDatabase_CopiesCommitsStillInTheWal()
        {
            string dir = Path.Combine(Path.GetTempPath(), "kasir-mig-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            string dbPath = Path.Combine(dir, "kasir.db");
            try
            {
                using (var db = new SqliteConnection("Data Source=" + dbPath + ";Pooling=False"))
                {
                    db.Open();
                    using (var cmd = db.CreateCommand())
                    {
                        // wal_autocheckpoint=0 keeps the committed rows in -wal only, which a
                        // plain File.Copy of the main file would miss.
                        cmd.CommandText = @"PRAGMA journal_mode=WAL; PRAGMA wal_autocheckpoint=0;
                                            CREATE TABLE t (v INTEGER); INSERT INTO t VALUES (1),(2),(3);";
                        cmd.ExecuteNonQuery();
                    }

                    MigrationRunner.BackupDatabase(db);
                }

                string backup = dbPath + ".migration.bak";
                File.Exists(backup).Should().BeTrue();
                using (var bak = new SqliteConnection("Data Source=" + backup + ";Mode=ReadOnly;Pooling=False"))
                {
                    bak.Open();
                    using var cmd = bak.CreateCommand();
                    cmd.CommandText = "SELECT COUNT(*) FROM t";
                    Convert.ToInt64(cmd.ExecuteScalar()).Should().Be(3);
                }
            }
            finally
            {
                SqliteConnection.ClearAllPools();
                try { Directory.Delete(dir, true); } catch { }
            }
        }
    }
}
