using Microsoft.Data.Sqlite;

namespace Kasir.Data.Migrations
{
    /// <summary>
    /// Add cash_variance column to shifts table to persist closing-vs-expected
    /// drawer mismatch for reporting. Idempotent: re-run on already-migrated DBs
    /// is a no-op.
    /// </summary>
    public class Migration_006 : IMigration
    {
        public int Version { get { return 6; } }
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
                    catch (SqliteException)
                    {
                        // already applied
                    }
                }
            }
        }
    }
}
