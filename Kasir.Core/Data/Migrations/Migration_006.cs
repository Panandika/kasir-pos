using Microsoft.Data.Sqlite;

namespace Kasir.Data.Migrations
{
    /// <summary>
    /// Bantuan help assistant tables: help_faq (+FTS5), help_tickets (local outbox),
    /// help_faq_fts (virtual) plus triggers. All idempotent against fresh DBs that
    /// already have these tables from Schema.sql.
    /// </summary>
    public class Migration_006 : IMigration
    {
        public int Version { get { return 6; } }
        public string Description { get { return "Bantuan help assistant: help_faq + FTS5 + help_tickets"; } }

        public void Up(SqliteConnection db)
        {
            string[] steps = new string[]
            {
                @"CREATE TABLE IF NOT EXISTS help_faq (
                    id          INTEGER PRIMARY KEY,
                    doc_path    TEXT NOT NULL,
                    anchor      TEXT,
                    title       TEXT,
                    content     TEXT NOT NULL,
                    tags        TEXT,
                    updated_at  TEXT NOT NULL DEFAULT (datetime('now','localtime'))
                )",

                @"CREATE VIRTUAL TABLE IF NOT EXISTS help_faq_fts USING fts5(
                    title, content, tags,
                    content=help_faq, content_rowid=id,
                    tokenize='unicode61'
                )",

                @"CREATE TRIGGER IF NOT EXISTS help_faq_ai AFTER INSERT ON help_faq BEGIN
                    INSERT INTO help_faq_fts(rowid, title, content, tags)
                    VALUES (new.id, new.title, new.content, new.tags);
                END",

                @"CREATE TRIGGER IF NOT EXISTS help_faq_ad AFTER DELETE ON help_faq BEGIN
                    INSERT INTO help_faq_fts(help_faq_fts, rowid, title, content, tags)
                    VALUES ('delete', old.id, old.title, old.content, old.tags);
                END",

                @"CREATE TRIGGER IF NOT EXISTS help_faq_au AFTER UPDATE ON help_faq BEGIN
                    INSERT INTO help_faq_fts(help_faq_fts, rowid, title, content, tags)
                    VALUES ('delete', old.id, old.title, old.content, old.tags);
                    INSERT INTO help_faq_fts(rowid, title, content, tags)
                    VALUES (new.id, new.title, new.content, new.tags);
                END",

                @"CREATE TABLE IF NOT EXISTS help_tickets (
                    id                INTEGER PRIMARY KEY,
                    ticket_no         TEXT UNIQUE NOT NULL,
                    store_id          TEXT NOT NULL,
                    register_id       TEXT NOT NULL,
                    cashier_id        TEXT,
                    category          TEXT NOT NULL CHECK (category IN ('hardware','transaksi','aplikasi','saran')),
                    body              TEXT NOT NULL CHECK (length(body) BETWEEN 3 AND 2000),
                    attachments_json  TEXT NOT NULL CHECK (json_valid(attachments_json)),
                    status            TEXT NOT NULL DEFAULT 'queued' CHECK (status IN ('queued','sent','failed')),
                    client_created_at TEXT NOT NULL DEFAULT (datetime('now','localtime')),
                    sent_at           TEXT,
                    sync_attempts     INTEGER NOT NULL DEFAULT 0,
                    last_error        TEXT
                )",

                @"CREATE INDEX IF NOT EXISTS idx_help_tickets_pending
                    ON help_tickets(status, client_created_at) WHERE status='queued'",

                @"CREATE INDEX IF NOT EXISTS idx_help_tickets_dead
                    ON help_tickets(status, sync_attempts) WHERE status='queued' AND sync_attempts >= 5"
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
                        // Idempotent: object already exists from Schema.sql on a fresh DB.
                    }
                }
            }
        }
    }
}
