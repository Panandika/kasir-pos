using System;
using Microsoft.Data.Sqlite;

namespace Kasir.Data.Migrations
{
    /// <summary>
    /// Add cash_variance column to shifts table to persist closing-vs-expected
    /// drawer mismatch for reporting. Idempotent: re-run on already-migrated DBs
    /// is a no-op (catches only the "duplicate column" SqliteException).
    /// </summary>
    public class Migration_007 : IMigration
    {
        public int Version { get { return 7; } }
        public string Description { get { return "Add cash_variance to shifts"; } }

        public void Up(SqliteConnection db)
        {
            string[] steps = new string[]
            {
                "ALTER TABLE shifts ADD COLUMN cash_variance INTEGER"
            };

            using (var cmd = db.CreateCommand())
            {
                foreach (string sql in steps)
                {
                    try
                    {
                        cmd.CommandText = sql;
                        cmd.ExecuteNonQuery();
                    }
                    catch (SqliteException ex) when (ex.Message.IndexOf("duplicate column", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        // Column already exists — idempotent re-run; no action needed.
                    }
                }
            }
        }
    }
}
