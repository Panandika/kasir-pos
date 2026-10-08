using System.Collections.Generic;
using System.Linq;
using Microsoft.Data.Sqlite;

namespace Kasir.Data.Migrations
{
    /// <summary>
    /// PR-K3 inactive-scan log. Creates inactive_sale_log and its sync trigger, and
    /// rebuilds sync_queue so its table_name CHECK accepts 'inactive_sale_log' (SQLite
    /// cannot alter a CHECK in place; without this the trigger's insert would abort the
    /// scan). Queue rows and ids are kept; rows for the long-dropped product_barcodes
    /// table cannot satisfy the new CHECK and are dropped. Idempotent: the rebuild is
    /// skipped when the CHECK already lists the table (fresh DBs from Schema.sql).
    /// </summary>
    public class Migration_012 : IMigration
    {
        public int Version { get { return 12; } }
        public string Description { get { return "inactive_sale_log + sync_queue CHECK"; } }

        private static readonly string[] QueueTables =
        {
            "products",
            "sales", "purchases",
            "cash_transactions",
            "memorial_journals",
            "orders",
            "stock_transfers",
            "stock_adjustments",
            "members", "subsidiaries",
            "departments", "discounts",
            "accounts", "locations",
            "discount_partners", "credit_cards",
            "inactive_sale_log"
        };

        private static string QueueTableList()
        {
            return string.Join(", ", QueueTables.Select(t => "'" + t + "'"));
        }

        public void Up(SqliteConnection db)
        {
            Exec(db, @"
                CREATE TABLE IF NOT EXISTS inactive_sale_log (
                    id              INTEGER PRIMARY KEY,
                    register_id     TEXT    NOT NULL DEFAULT '',
                    product_code    TEXT    NOT NULL,
                    sale_date       TEXT    NOT NULL,
                    created_at      TEXT    NOT NULL DEFAULT (datetime('now','localtime')),
                    UNIQUE(product_code, sale_date)
                )");

            string queueSql = Scalar(db, "SELECT sql FROM sqlite_master WHERE type = 'table' AND name = 'sync_queue'");
            if (queueSql != null && !queueSql.Contains("'inactive_sale_log'"))
                RebuildSyncQueue(db);

            Exec(db, @"
                CREATE TRIGGER IF NOT EXISTS trg_inactive_sale_log_sync_i AFTER INSERT ON inactive_sale_log
                BEGIN
                    INSERT INTO sync_queue(register_id, table_name, record_key, operation)
                    VALUES (COALESCE((SELECT value FROM config WHERE key='register_id'), 'unknown'),
                            'inactive_sale_log', NEW.id, 'I');
                END");
        }

        private static void RebuildSyncQueue(SqliteConnection db)
        {
            var newColumns = new[]
            {
                "id", "register_id", "table_name", "record_key", "operation", "payload",
                "created_at", "synced_at", "status", "retry_count", "last_error",
                "cloud_synced", "cloud_synced_at"
            };
            var oldColumns = new HashSet<string>();
            using (var cmd = db.CreateCommand())
            {
                cmd.CommandText = "PRAGMA table_info(sync_queue)";
                using (var rd = cmd.ExecuteReader())
                    while (rd.Read()) oldColumns.Add(rd.GetString(1));
            }
            string copyColumns = string.Join(", ", newColumns.Where(oldColumns.Contains));

            // legacy_alter_table: the RENAME must not re-validate the other tables'
            // sync triggers, which name sync_queue while it is momentarily absent.
            Exec(db, "PRAGMA legacy_alter_table = ON");
            try
            {
                Exec(db, "DROP TABLE IF EXISTS sync_queue_new");
                Exec(db, @"
                    CREATE TABLE sync_queue_new (
                        id              INTEGER PRIMARY KEY,
                        register_id     TEXT    NOT NULL,
                        table_name      TEXT    NOT NULL CHECK(table_name IN (" + QueueTableList() + @")),
                        record_key      TEXT    NOT NULL,
                        operation       TEXT    NOT NULL CHECK(operation IN ('I','U','D')),
                        payload         TEXT    CHECK(payload IS NULL OR json_valid(payload)),
                        created_at      TEXT    NOT NULL DEFAULT (datetime('now')),
                        synced_at       TEXT,
                        status          TEXT    NOT NULL DEFAULT 'pending'
                                               CHECK(status IN ('pending','synced','failed')),
                        retry_count     INTEGER NOT NULL DEFAULT 0,
                        last_error      TEXT,
                        cloud_synced    INTEGER NOT NULL DEFAULT 0,
                        cloud_synced_at TEXT
                    )");
                Exec(db,
                    "INSERT INTO sync_queue_new (" + copyColumns + ") " +
                    "SELECT " + copyColumns + " FROM sync_queue " +
                    "WHERE table_name IN (" + QueueTableList() + ")");
                Exec(db, "DROP TABLE sync_queue");
                Exec(db, "ALTER TABLE sync_queue_new RENAME TO sync_queue");
                Exec(db, @"CREATE INDEX IF NOT EXISTS idx_sync_queue_drain ON sync_queue(status, id)
                           WHERE status = 'pending'");
                Exec(db, @"CREATE INDEX IF NOT EXISTS idx_sync_queue_cloud_drain ON sync_queue(cloud_synced, id)
                           WHERE cloud_synced = 0");
            }
            finally
            {
                Exec(db, "PRAGMA legacy_alter_table = OFF");
            }
        }

        private static void Exec(SqliteConnection db, string sql)
        {
            using (var cmd = db.CreateCommand())
            {
                cmd.CommandText = sql;
                cmd.ExecuteNonQuery();
            }
        }

        private static string Scalar(SqliteConnection db, string sql)
        {
            using (var cmd = db.CreateCommand())
            {
                cmd.CommandText = sql;
                return cmd.ExecuteScalar() as string;
            }
        }
    }
}
