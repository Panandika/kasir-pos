using Microsoft.Data.Sqlite;

namespace Kasir.Data.Migrations
{
    /// <summary>
    /// WP-04 CloudSync pull. applied_requests records every Supabase
    /// pos_stock_requests row the hub has applied (idempotency, written in the apply
    /// transaction); pull_movement_id_seq is the next id for a dashboard-originated
    /// stock_movements row (reserved range from 5,000,000,000, OB-13). Both
    /// idempotent: Schema.sql creates/seeds them on a fresh DB.
    /// </summary>
    public class Migration_015 : IMigration
    {
        public int Version { get { return 15; } }
        public string Description { get { return "CloudSync pull: applied_requests + dashboard movement id sequence"; } }

        public void Up(SqliteConnection db)
        {
            using (var cmd = db.CreateCommand())
            {
                cmd.CommandText = @"
                    CREATE TABLE IF NOT EXISTS applied_requests (
                        request_kind    TEXT    NOT NULL,
                        idempotency_key TEXT    NOT NULL,
                        request_id      TEXT,
                        journal_no      TEXT,
                        applied_at      TEXT    NOT NULL DEFAULT (datetime('now','localtime')),
                        PRIMARY KEY (request_kind, idempotency_key)
                    );

                    INSERT OR IGNORE INTO config (key, value, description) VALUES
                      ('pull_movement_id_seq', '5000000000', 'Next id for dashboard-originated stock_movements');";
                cmd.ExecuteNonQuery();
            }
        }
    }
}
