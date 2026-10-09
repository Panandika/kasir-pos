using Microsoft.Data.Sqlite;

namespace Kasir.Data.Migrations
{
    /// <summary>
    /// PR-K3 inactive-scan log. Creates inactive_sale_log only: no sync_queue trigger and
    /// no sync_queue CHECK change, so no existing table is rebuilt. Cloud delivery is
    /// deferred; a later mirror loads the table by its natural key
    /// (register_id, product_code, sale_date). Idempotent.
    /// </summary>
    public class Migration_012 : IMigration
    {
        public int Version { get { return 12; } }
        public string Description { get { return "inactive_sale_log"; } }

        public void Up(SqliteConnection db)
        {
            Apply(db);
        }

        /// <summary>
        /// Idempotent body, also called by later migrations so a register that reached a
        /// higher schema_version from a build without 012 still gets the table.
        /// </summary>
        public static void Apply(SqliteConnection db)
        {
            using (var cmd = db.CreateCommand())
            {
                cmd.CommandText = @"
                    CREATE TABLE IF NOT EXISTS inactive_sale_log (
                        id              INTEGER PRIMARY KEY,
                        register_id     TEXT    NOT NULL DEFAULT '',
                        product_code    TEXT    NOT NULL,
                        sale_date       TEXT    NOT NULL,
                        created_at      TEXT    NOT NULL DEFAULT (datetime('now','localtime')),
                        UNIQUE(product_code, sale_date)
                    );
                    DROP TRIGGER IF EXISTS trg_inactive_sale_log_sync_i;";
                cmd.ExecuteNonQuery();
            }
        }
    }
}
