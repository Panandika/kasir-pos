using System.Collections.Generic;
using Microsoft.Data.Sqlite;
using Kasir.Models;

namespace Kasir.Data.Repositories
{
    /// <summary>
    /// Local outbox for Bantuan help tickets. Rows drained by CloudSync HttpSink.
    /// </summary>
    public class HelpTicketRepository
    {
        private readonly SqliteConnection _db;

        public HelpTicketRepository(SqliteConnection db)
        {
            _db = db;
        }

        public int Insert(HelpTicket t)
        {
            using (var cmd = _db.CreateCommand())
            {
                cmd.CommandText = @"
                    INSERT INTO help_tickets
                        (ticket_no, store_id, register_id, cashier_id,
                         category, body, attachments_json, status)
                    VALUES
                        (@no, @store, @reg, @cashier,
                         @cat, @body, @att, 'queued');
                    SELECT last_insert_rowid();";
                cmd.Parameters.AddWithValue("@no", t.TicketNo);
                cmd.Parameters.AddWithValue("@store", t.StoreId);
                cmd.Parameters.AddWithValue("@reg", t.RegisterId);
                cmd.Parameters.AddWithValue("@cashier", (object)t.CashierId ?? System.DBNull.Value);
                cmd.Parameters.AddWithValue("@cat", t.Category);
                cmd.Parameters.AddWithValue("@body", t.Body);
                cmd.Parameters.AddWithValue("@att", t.AttachmentsJson);
                long id = (long)cmd.ExecuteScalar();
                return (int)id;
            }
        }

        public HelpTicket GetById(int id)
        {
            using (var cmd = _db.CreateCommand())
            {
                cmd.CommandText = "SELECT * FROM help_tickets WHERE id = @id";
                cmd.Parameters.AddWithValue("@id", id);
                using (var r = cmd.ExecuteReader())
                {
                    return r.Read() ? Map(r) : null;
                }
            }
        }

        public List<HelpTicket> GetPending(int limit = 50)
        {
            var list = new List<HelpTicket>();
            using (var cmd = _db.CreateCommand())
            {
                cmd.CommandText = @"
                    SELECT * FROM help_tickets
                    WHERE status = 'queued'
                    ORDER BY client_created_at
                    LIMIT @lim";
                cmd.Parameters.AddWithValue("@lim", limit);
                using (var r = cmd.ExecuteReader())
                {
                    while (r.Read()) list.Add(Map(r));
                }
            }
            return list;
        }

        public void MarkSent(int id)
        {
            SqlHelper.ExecuteNonQuery(_db,
                @"UPDATE help_tickets
                  SET status='sent', sent_at=datetime('now','localtime'), last_error=NULL
                  WHERE id=@id",
                SqlHelper.Param("@id", id));
        }

        public void MarkFailed(int id, string shortError)
        {
            SqlHelper.ExecuteNonQuery(_db,
                @"UPDATE help_tickets
                  SET status='failed', last_error=@err
                  WHERE id=@id",
                SqlHelper.Param("@id", id),
                SqlHelper.Param("@err", shortError ?? "unknown"));
        }

        public void RecordRetryFailure(int id, string shortError)
        {
            SqlHelper.ExecuteNonQuery(_db,
                @"UPDATE help_tickets
                  SET sync_attempts = sync_attempts + 1, last_error = @err
                  WHERE id = @id",
                SqlHelper.Param("@id", id),
                SqlHelper.Param("@err", shortError ?? "unknown"));
        }

        public int CountPendingForRegister(string registerId)
        {
            using (var cmd = _db.CreateCommand())
            {
                cmd.CommandText = "SELECT COUNT(*) FROM help_tickets WHERE register_id=@reg AND status='queued'";
                cmd.Parameters.AddWithValue("@reg", registerId);
                long c = (long)cmd.ExecuteScalar();
                return (int)c;
            }
        }

        private static HelpTicket Map(SqliteDataReader r)
        {
            return new HelpTicket
            {
                Id = r.GetInt32(r.GetOrdinal("id")),
                TicketNo = r.GetString(r.GetOrdinal("ticket_no")),
                StoreId = r.GetString(r.GetOrdinal("store_id")),
                RegisterId = r.GetString(r.GetOrdinal("register_id")),
                CashierId = r.IsDBNull(r.GetOrdinal("cashier_id")) ? null : r.GetString(r.GetOrdinal("cashier_id")),
                Category = r.GetString(r.GetOrdinal("category")),
                Body = r.GetString(r.GetOrdinal("body")),
                AttachmentsJson = r.GetString(r.GetOrdinal("attachments_json")),
                Status = r.GetString(r.GetOrdinal("status")),
                ClientCreatedAt = r.GetString(r.GetOrdinal("client_created_at")),
                SentAt = r.IsDBNull(r.GetOrdinal("sent_at")) ? null : r.GetString(r.GetOrdinal("sent_at")),
                SyncAttempts = r.GetInt32(r.GetOrdinal("sync_attempts")),
                LastError = r.IsDBNull(r.GetOrdinal("last_error")) ? null : r.GetString(r.GetOrdinal("last_error"))
            };
        }
    }
}
