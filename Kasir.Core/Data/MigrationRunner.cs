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
            new Migration_005()
            // Add new migrations here in order:
            // new Migration_006(),
        };

        /// <summary>
        /// Highest migration version known to this build. Single source of truth
        /// for schema version — DatabaseValidator.ExpectedSchemaVersion derives from this.
        /// </summary>
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

        private static void BackupDatabase(SqliteConnection db)
        {
            try
            {
                string dbPath = new SqliteConnectionStringBuilder(db.ConnectionString).DataSource;
                if (string.IsNullOrEmpty(dbPath) || !File.Exists(dbPath))
                    return;

                string backupPath = dbPath + ".migration.bak";
                File.Copy(dbPath, backupPath, true);
            }
            catch
            {
                // Non-fatal: backup failure shouldn't block migration
            }
        }
    }
}
