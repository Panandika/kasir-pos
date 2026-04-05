using System.Data.SQLite;

namespace Kasir.Data.Migrations
{
    /// <summary>
    /// Template migration — replace with actual schema changes when needed.
    /// </summary>
    public class Migration_002 : IMigration
    {
        public int Version { get { return 2; } }
        public string Description { get { return "Template migration — no changes"; } }

        public void Up(SQLiteConnection db)
        {
            // Example:
            // using (var cmd = new SQLiteCommand(db))
            // {
            //     cmd.CommandText = "ALTER TABLE products ADD COLUMN new_field TEXT DEFAULT '';";
            //     cmd.ExecuteNonQuery();
            // }
        }
    }
}
