using Microsoft.Data.Sqlite;

namespace Kasir.Data.Migrations
{
    /// <summary>
    /// Add discount engine columns to discounts table.
    /// </summary>
    public class Migration_002 : IMigration
    {
        public int Version { get { return 2; } }
        public string Description { get { return "Add discount engine columns (dept_code, is_active, priority, etc.)"; } }

        public void Up(SqliteConnection db)
        {
            // Add columns if they don't already exist (fresh DBs have them from Schema.sql)
            string[] columns = new string[]
            {
                "ALTER TABLE discounts ADD COLUMN dept_code TEXT NOT NULL DEFAULT ''",
                "ALTER TABLE discounts ADD COLUMN sub_code TEXT DEFAULT ''",
                "ALTER TABLE discounts ADD COLUMN min_qty INTEGER DEFAULT 0",
                "ALTER TABLE discounts ADD COLUMN max_qty INTEGER DEFAULT 0",
                "ALTER TABLE discounts ADD COLUMN price_override INTEGER DEFAULT 0",
                "ALTER TABLE discounts ADD COLUMN description TEXT DEFAULT ''",
                "ALTER TABLE discounts ADD COLUMN priority INTEGER DEFAULT 0",
                "ALTER TABLE discounts ADD COLUMN is_active INTEGER DEFAULT 1"
            };
            using (var cmd = db.CreateCommand())
            {
                foreach (string sql in columns)
                {
                    try
                    {
                        cmd.CommandText = sql;
                        cmd.ExecuteNonQuery();
                    }
                    catch (SqliteException)
                    {
                        // Column already exists (fresh DB from Schema.sql)
                    }
                }
            }
        }
    }
}
