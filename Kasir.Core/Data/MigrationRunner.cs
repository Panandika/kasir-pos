using System;
using System.Collections.Generic;
using Microsoft.Data.Sqlite;
using System.IO;
using System.Linq;
using Kasir.Data.Migrations;
using Kasir.Data.Repositories;

namespace Kasir.Data
{
    public static class MigrationRunner
    {
        private static readonly List<IMigration> Migrations = new List<IMigration>
        {
            new Migration_002(),
            new Migration_003(),
            new Migration_004(),
            new Migration_005(),
            new Migration_006(),
            new Migration_007(),
            new Migration_008(),
            new Migration_009(),
            new Migration_010(),
            new Migration_011(),
            new Migration_012(),
            // Migration_012 is PR-K3 (inactive_sale_log): merge AND release K3 before K4.
            // Migration_013 also creates 012's table (idempotent) in case a K4 build ships first.
            new Migration_013(),
            new Migration_014(),
            new Migration_015()
            // Add new migrations here in order:
            // new Migration_016(),
        };

        /// <summary>
        /// Highest migration version known to this build. Single source of truth
        /// for schema version — DatabaseValidator.ExpectedSchemaVersion derives from this.
        /// </summary>
        /// <summary>Registered migration versions, in list order (a test asserts they are 2..N contiguous).</summary>
        public static IReadOnlyList<int> Versions => Migrations.Select(m => m.Version).ToList();

        public static int LatestVersion =>
            Migrations.Count == 0 ? 1 : Migrations.Max(m => m.Version);

        public static void Run(SqliteConnection db)
        {
            var configRepo = new ConfigRepository(db);
            string versionStr = configRepo.Get("schema_version") ?? "1";
            int currentVersion;
            if (!int.TryParse(versionStr, out currentVersion))
            {
                currentVersion = 1;
            }

            var pending = Migrations
                .Where(m => m.Version > currentVersion)
                .OrderBy(m => m.Version)
                .ToList();

            if (pending.Count == 0)
                return;

            foreach (var migration in pending)
            {
                // Backup database before each migration
                BackupDatabase(db);

                using (var txn = db.BeginTransaction())
                {
                    try
                    {
                        migration.Up(db);

                        // Update schema version
                        configRepo.Set("schema_version", migration.Version.ToString());

                        txn.Commit();
                    }
                    catch (Exception)
                    {
                        txn.Rollback();
                        throw;
                    }
                }
            }
        }

        /// <summary>
        /// Writes &lt;db&gt;.migration.bak with SQLite's online backup API, which includes
        /// commits still sitting in the -wal file (a File.Copy of the main file misses them).
        /// </summary>
        public static void BackupDatabase(SqliteConnection db)
        {
            try
            {
                string dbPath = new SqliteConnectionStringBuilder(db.ConnectionString).DataSource;
                if (string.IsNullOrEmpty(dbPath) || dbPath == ":memory:" || !File.Exists(dbPath))
                    return;

                string backupPath = dbPath + ".migration.bak";
                if (File.Exists(backupPath)) File.Delete(backupPath);
                using (var dest = new SqliteConnection("Data Source=" + backupPath + ";Pooling=False"))
                {
                    dest.Open();
                    db.BackupDatabase(dest);
                }
            }
            catch
            {
                // Non-fatal: backup failure shouldn't block migration
            }
        }
    }
}
