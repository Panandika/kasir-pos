using System;
using System.Collections.Generic;
using System.Data.SQLite;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using Kasir.Data;
using Kasir.Data.Repositories;
using Kasir.Models;
using Kasir.Utils;
using Newtonsoft.Json;

namespace Kasir.Sync
{
    public class PushService
    {
        private readonly SQLiteConnection _db;
        private readonly SyncQueueRepository _queueRepo;
        private readonly ConfigRepository _configRepo;
        private readonly ISyncFileWriter _fileWriter;
        private readonly IClock _clock;

        public PushService(
            SQLiteConnection db,
            ISyncFileWriter fileWriter,
            IClock clock)
        {
            _db = db;
            _queueRepo = new SyncQueueRepository(db);
            _configRepo = new ConfigRepository(db);
            _fileWriter = fileWriter;
            _clock = clock;
        }

        public PushResult Push()
        {
            string registerId = _configRepo.Get("register_id") ?? "01";
            string hubShare = _configRepo.Get("sync_hub_share");

            if (string.IsNullOrEmpty(hubShare))
            {
                return new PushResult { Success = false, Error = "sync_hub_share not configured" };
            }

            var pending = _queueRepo.GetPending(registerId, SyncConfig.MaxBatchSize);

            if (pending.Count == 0)
            {
                return new PushResult { Success = true, EventCount = 0 };
            }

            var batch = BuildBatch(registerId, pending);
            string json = SerializeBatch(batch);
            string signature = SignPayload(json);
            batch.Signature = signature;
            json = SerializeBatch(batch);

            string outboxPath = SyncConfig.GetOutboxPath(hubShare);
            string fileName = string.Format("{0}_{1}_{2}.json",
                registerId,
                _clock.Now.ToString("yyyyMMdd_HHmmss"),
                batch.BatchId);

            string tempPath = Path.Combine(outboxPath, fileName + ".tmp");
            string destPath = Path.Combine(outboxPath, fileName);

            try
            {
                _fileWriter.Write(tempPath, json);
                _fileWriter.SafeMove(tempPath, destPath);

                // Mark all events as synced
                foreach (var entry in pending)
                {
                    _queueRepo.MarkSynced(entry.Id);
                }

                return new PushResult
                {
                    Success = true,
                    EventCount = pending.Count,
                    FilePath = destPath
                };
            }
            catch (Exception ex)
            {
                // Mark as failed
                foreach (var entry in pending)
                {
                    _queueRepo.MarkFailed(entry.Id, ex.Message);
                }

                return new PushResult { Success = false, Error = ex.Message };
            }
        }

        private SyncBatch BuildBatch(string registerId, List<SyncQueueEntry> entries)
        {
            var batch = new SyncBatch
            {
                RegisterId = registerId,
                SchemaVersion = SyncConfig.SchemaVersion,
                Timestamp = _clock.Now.ToString("yyyy-MM-dd HH:mm:ss"),
                BatchId = Guid.NewGuid().ToString("N").Substring(0, 8)
            };

            foreach (var entry in entries)
            {
                if (!SyncConfig.SyncedTables.Contains(entry.TableName))
                {
                    continue;
                }

                var evt = new SyncEvent
                {
                    QueueId = entry.Id,
                    TableName = entry.TableName,
                    RecordKey = entry.RecordKey,
                    Operation = entry.Operation
                };

                if (entry.Operation == "D")
                {
                    // DELETE: use payload from sync_queue (row is gone)
                    if (!string.IsNullOrEmpty(entry.Payload))
                    {
                        evt.Data = JsonConvert.DeserializeObject<Dictionary<string, object>>(entry.Payload);
                    }
                }
                else
                {
                    // INSERT/UPDATE: fetch current row
                    evt.Data = FetchRowData(entry.TableName, entry.RecordKey);
                }

                if (evt.Data != null)
                {
                    batch.Events.Add(evt);
                }
            }

            return batch;
        }

        private Dictionary<string, object> FetchRowData(string tableName, string recordKey)
        {
            // Determine primary key column based on table
            string keyColumn = GetKeyColumn(tableName);
            if (keyColumn == null) return null;

            string sql = string.Format(
                "SELECT * FROM [{0}] WHERE [{1}] = @key",
                tableName, keyColumn);

            var data = new Dictionary<string, object>();

            using (var cmd = new SQLiteCommand(sql, _db))
            {
                cmd.Parameters.AddWithValue("@key", recordKey);
                using (var reader = cmd.ExecuteReader())
                {
                    if (!reader.Read()) return null;

                    for (int i = 0; i < reader.FieldCount; i++)
                    {
                        string colName = reader.GetName(i);
                        object value = reader.IsDBNull(i) ? null : reader.GetValue(i);
                        data[colName] = value;
                    }
                }
            }

            return data;
        }

        internal static string GetKeyColumn(string tableName)
        {
            switch (tableName)
            {
                case "products": return "product_code";
                case "product_barcodes": return "barcode";
                case "departments": return "dept_code";
                case "subsidiaries": return "sub_code";
                case "members": return "member_code";
                case "discounts": return "id";
                case "discount_partners": return "id";
                case "accounts": return "account_code";
                case "locations": return "location_code";
                case "credit_cards": return "id";
                case "sales": return "id";
                case "purchases": return "id";
                case "cash_transactions": return "id";
                case "memorial_journals": return "id";
                case "orders": return "id";
                case "stock_transfers": return "id";
                case "stock_adjustments": return "id";
                default: return null;
            }
        }

        private string SignPayload(string json)
        {
            string hmacKey = _configRepo.Get("sync_hmac_key") ?? "default-hmac-key-change-me";
            byte[] keyBytes = Encoding.UTF8.GetBytes(hmacKey);
            byte[] payloadBytes = Encoding.UTF8.GetBytes(json);

            using (var hmac = new HMACSHA256(keyBytes))
            {
                byte[] hash = hmac.ComputeHash(payloadBytes);
                return Convert.ToBase64String(hash);
            }
        }

        private static string SerializeBatch(SyncBatch batch)
        {
            return JsonConvert.SerializeObject(batch, Formatting.None);
        }
    }

    public class PushResult
    {
        public bool Success { get; set; }
        public int EventCount { get; set; }
        public string FilePath { get; set; }
        public string Error { get; set; }
    }
}
