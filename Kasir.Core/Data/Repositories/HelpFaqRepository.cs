using System.Collections.Generic;
using Microsoft.Data.Sqlite;
using Kasir.Models;

namespace Kasir.Data.Repositories
{
    /// <summary>
    /// FAQ corpus for Bantuan TANYA mode. Backed by FTS5 virtual table.
    /// </summary>
    public class HelpFaqRepository
    {
        private readonly SqliteConnection _db;

        public HelpFaqRepository(SqliteConnection db)
        {
            _db = db;
        }

        public int Upsert(HelpFaq f)
        {
            // Upsert by (doc_path, anchor) so re-ingesting docs replaces in place.
            using (var cmd = _db.CreateCommand())
            {
                cmd.CommandText = @"
                    INSERT INTO help_faq (doc_path, anchor, title, content, tags, updated_at)
                    VALUES (@path, @anchor, @title, @content, @tags, datetime('now','localtime'))
                    ON CONFLICT (doc_path, ifnull(anchor,'')) DO UPDATE SET
                        title = excluded.title,
                        content = excluded.content,
                        tags = excluded.tags,
                        updated_at = excluded.updated_at
                    RETURNING id";

                // SQLite ON CONFLICT requires a unique constraint on the conflict target;
                // create one on first use if it does not exist.
                EnsureUniqueConstraint();

                cmd.Parameters.AddWithValue("@path", f.DocPath);
                cmd.Parameters.AddWithValue("@anchor", (object)f.Anchor ?? System.DBNull.Value);
                cmd.Parameters.AddWithValue("@title", (object)f.Title ?? System.DBNull.Value);
                cmd.Parameters.AddWithValue("@content", f.Content);
                cmd.Parameters.AddWithValue("@tags", (object)f.Tags ?? System.DBNull.Value);
                long id = (long)cmd.ExecuteScalar();
                return (int)id;
            }
        }

        public int Insert(HelpFaq f)
        {
            using (var cmd = _db.CreateCommand())
            {
                cmd.CommandText = @"
                    INSERT INTO help_faq (doc_path, anchor, title, content, tags)
                    VALUES (@path, @anchor, @title, @content, @tags);
                    SELECT last_insert_rowid();";
                cmd.Parameters.AddWithValue("@path", f.DocPath);
                cmd.Parameters.AddWithValue("@anchor", (object)f.Anchor ?? System.DBNull.Value);
                cmd.Parameters.AddWithValue("@title", (object)f.Title ?? System.DBNull.Value);
                cmd.Parameters.AddWithValue("@content", f.Content);
                cmd.Parameters.AddWithValue("@tags", (object)f.Tags ?? System.DBNull.Value);
                long id = (long)cmd.ExecuteScalar();
                return (int)id;
            }
        }

        public List<HelpFaqHit> Search(string query, int limit = 5)
        {
            var hits = new List<HelpFaqHit>();
            if (string.IsNullOrWhiteSpace(query)) return hits;

            string ftsQuery = SanitizeFtsQuery(query);
            if (string.IsNullOrEmpty(ftsQuery)) return hits;

            using (var cmd = _db.CreateCommand())
            {
                cmd.CommandText = @"
                    SELECT f.id, f.title, f.content, f.doc_path, f.anchor,
                           bm25(help_faq_fts) AS score
                    FROM help_faq_fts fts
                    JOIN help_faq f ON f.id = fts.rowid
                    WHERE help_faq_fts MATCH @q
                    ORDER BY score
                    LIMIT @lim";
                cmd.Parameters.AddWithValue("@q", ftsQuery);
                cmd.Parameters.AddWithValue("@lim", limit);
                using (var r = cmd.ExecuteReader())
                {
                    while (r.Read())
                    {
                        hits.Add(new HelpFaqHit
                        {
                            Id = r.GetInt32(0),
                            Title = r.IsDBNull(1) ? null : r.GetString(1),
                            Content = r.GetString(2),
                            DocPath = r.GetString(3),
                            Anchor = r.IsDBNull(4) ? null : r.GetString(4),
                            // bm25 returns negative reals; flip sign so higher = better
                            Score = -r.GetDouble(5)
                        });
                    }
                }
            }
            return hits;
        }

        public int Count()
        {
            using (var cmd = _db.CreateCommand())
            {
                cmd.CommandText = "SELECT COUNT(*) FROM help_faq";
                long c = (long)cmd.ExecuteScalar();
                return (int)c;
            }
        }

        public void Clear()
        {
            SqlHelper.ExecuteNonQuery(_db, "DELETE FROM help_faq");
        }

        // Strip FTS5 metacharacters that would otherwise produce a syntax error.
        // Keep alphanumerics, spaces, and treat anything else as whitespace.
        // Wrap each token to enforce prefix matching for short queries.
        private static string SanitizeFtsQuery(string raw)
        {
            var chars = new System.Text.StringBuilder(raw.Length);
            foreach (char c in raw)
            {
                if (char.IsLetterOrDigit(c) || c == ' ' || c == '_') chars.Append(c);
                else chars.Append(' ');
            }
            string cleaned = chars.ToString().Trim();
            if (cleaned.Length == 0) return null;
            // Quote each token to be safe against keywords like AND/OR/NOT.
            var tokens = cleaned.Split(new[] { ' ' }, System.StringSplitOptions.RemoveEmptyEntries);
            for (int i = 0; i < tokens.Length; i++) tokens[i] = "\"" + tokens[i] + "\"";
            return string.Join(" ", tokens);
        }

        private void EnsureUniqueConstraint()
        {
            // help_faq lacks a UNIQUE constraint on (doc_path, anchor) by default — add a
            // unique index lazily so Upsert can use ON CONFLICT.
            SqlHelper.ExecuteNonQuery(_db,
                "CREATE UNIQUE INDEX IF NOT EXISTS idx_help_faq_path_anchor ON help_faq(doc_path, ifnull(anchor,''))");
        }
    }
}
