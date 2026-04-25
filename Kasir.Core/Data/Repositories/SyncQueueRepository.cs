using System.Collections.Generic;
using Microsoft.Data.Sqlite;
using Kasir.Models;

namespace Kasir.Data.Repositories
{
    public class SyncQueueRepository
    {
        private readonly SqliteConnection _db;

        public SyncQueueRepository(SqliteConnection db)
        {
            _db = db;
        }

        public List<SyncQueueEntry> GetPending(string registerId, int limit)
        {
            return SqlHelper.Query(_db,
                @"SELECT * FROM sync_queue
                  WHERE register_id = @reg AND status = 'pending'
                  ORDER BY id ASC LIMIT @limit",
                MapEntry,
                SqlHelper.Param("@reg", registerId),
                SqlHelper.Param("@limit", limit));
        }

        public List<SyncQueueEntry> GetAfter(long afterId, int limit)
        {
            return SqlHelper.Query(_db,
                @"SELECT * FROM sync_queue
                  WHERE id > @afterId AND status = 'pending'
                  ORDER BY id ASC LIMIT @limit",
                MapEntry,
                SqlHelper.Param("@afterId", afterId),
                SqlHelper.Param("@limit", limit));
        }

        // Phase 6 cloud-sync reader: LAN must already be synced (status='synced')
        // AND cloud must not yet be synced (cloud_synced=0). Preserves the plan's
        // "LAN sync is authoritative" principle — the cloud worker never sees a
        // row before its LAN peers have.
        public List<SyncQueueEntry> GetPendingCloud(int limit)
        {
            return SqlHelper.Query(_db,
                @"SELECT * FROM sync_queue
                  WHERE cloud_synced = 0 AND status = 'synced'
                  ORDER BY id ASC LIMIT @limit",
                MapEntry,
                SqlHelper.Param("@limit", limit));
        }

        public void MarkSynced(int id)
        {
            SqlHelper.ExecuteNonQuery(_db,
                @"UPDATE sync_queue SET status = 'synced',
                  synced_at = datetime('now','localtime')
                  WHERE id = @id",
                SqlHelper.Param("@id", id));
        }

        public void MarkFailed(int id, string error)
        {
            SqlHelper.ExecuteNonQuery(_db,
                @"UPDATE sync_queue SET status = 'failed',
                  retry_count = retry_count + 1,
                  last_error = @error
                  WHERE id = @id",
                SqlHelper.Param("@id", id),
                SqlHelper.Param("@error", error));
        }

        // Phase 6 cloud-sync writer: invoked only after PostgresSink confirms the row
        // landed in Supabase. Independent of MarkSynced; the two flags never conflict.
        public void MarkCloudSynced(long id)
        {
            SqlHelper.ExecuteNonQuery(_db,
                @"UPDATE sync_queue SET cloud_synced = 1,
                  cloud_synced_at = datetime('now','localtime')
                  WHERE id = @id",
                SqlHelper.Param("@id", id));
        }

        public long GetMaxId()
        {
            return SqlHelper.ExecuteScalar<long>(_db,
                "SELECT COALESCE(MAX(id), 0) FROM sync_queue");
        }

        public int PruneSynced(long beforeId)
        {
            return SqlHelper.ExecuteNonQuery(_db,
                "DELETE FROM sync_queue WHERE status = 'synced' AND id < @id",
                SqlHelper.Param("@id", beforeId));
        }

        private static SyncQueueEntry MapEntry(SqliteDataReader reader)
        {
            return new SyncQueueEntry
            {
                Id = SqlHelper.GetInt(reader, "id"),
                RegisterId = SqlHelper.GetString(reader, "register_id"),
                TableName = SqlHelper.GetString(reader, "table_name"),
                RecordKey = SqlHelper.GetString(reader, "record_key"),
                Operation = SqlHelper.GetString(reader, "operation"),
                Payload = SqlHelper.GetString(reader, "payload"),
                CreatedAt = SqlHelper.GetString(reader, "created_at"),
                SyncedAt = SqlHelper.GetString(reader, "synced_at"),
                Status = SqlHelper.GetString(reader, "status"),
                RetryCount = SqlHelper.GetInt(reader, "retry_count"),
                LastError = SqlHelper.GetString(reader, "last_error"),
                // Cloud sync bookkeeping — SqlHelper.GetInt/GetString return 0/null when
                // the column is missing (e.g. a pre-migration register running new code),
                // so this is backward-compatible.
                CloudSynced = SqlHelper.GetInt(reader, "cloud_synced"),
                CloudSyncedAt = SqlHelper.GetString(reader, "cloud_synced_at")
            };
        }
    }
}
