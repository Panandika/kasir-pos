using System;
using System.Collections.Generic;
using System.Data.SQLite;
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
            new Migration_002()
            // Add new migrations here in order:
            // new Migration_003(),
            // new Migration_004(),
        };

        public static void Run(SQLiteConnection db)
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

        private static void BackupDatabase(SQLiteConnection db)
        {
            try
            {
                string dbPath = db.FileName;
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
