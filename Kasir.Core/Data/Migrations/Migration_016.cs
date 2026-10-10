using Microsoft.Data.Sqlite;

namespace Kasir.Data.Migrations
{
    /// <summary>
    /// WP-05 POS purchasing lock. Seeds purchasing_locked = 'true' (purchasing moves to
    /// the dashboard; INSERT OR IGNORE keeps a value an owner already set) and creates
    /// config_audit, the local log of every lock change. Idempotent: Schema.sql
    /// creates/seeds both on a fresh DB.
    /// </summary>
    public class Migration_016 : IMigration
    {
        public int Version { get { return 16; } }
        public string Description { get { return "Purchasing lock flag + config_audit"; } }

        public void Up(SqliteConnection db)
        {
            using (var cmd = db.CreateCommand())
            {
                cmd.CommandText = @"
                    CREATE TABLE IF NOT EXISTS config_audit (
                        id          INTEGER PRIMARY KEY,
                        key         TEXT    NOT NULL,
                        old_value   TEXT,
                        new_value   TEXT,
                        changed_at  TEXT    NOT NULL DEFAULT (datetime('now','localtime')),
                        username    TEXT,
                        source      TEXT    NOT NULL
                    );
                    CREATE INDEX IF NOT EXISTS idx_config_audit_key ON config_audit(key, changed_at);

                    INSERT OR IGNORE INTO config (key, value, description) VALUES
                      ('purchasing_locked', 'true', 'When true, POS purchasing is disabled; use dashboard instead');";
                cmd.ExecuteNonQuery();
            }
        }
    }
}
