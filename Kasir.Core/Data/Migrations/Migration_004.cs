using Microsoft.Data.Sqlite;

namespace Kasir.Data.Migrations
{
    /// <summary>
    /// Add terms and received_date columns to purchases for full invoice header support.
    /// </summary>
    public class Migration_004 : IMigration
    {
        public int Version { get { return 4; } }
        public string Description { get { return "Add terms and received_date columns to purchases"; } }

        public void Up(SqliteConnection db)
        {
            string[] columns = new string[]
            {
                "ALTER TABLE purchases ADD COLUMN terms INTEGER DEFAULT 0",
                "ALTER TABLE purchases ADD COLUMN received_date TEXT"
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
