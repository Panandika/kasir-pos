using System;
using System.Collections.Generic;
using System.Data.SQLite;
using System.Security;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Kasir.Data;
using Kasir.Data.Repositories;
using Newtonsoft.Json;

namespace Kasir.Sync
{
    public class PullService
    {
        private static readonly Regex ValidColumnName = new Regex(@"^[a-z_][a-z0-9_]{0,63}$");

        private readonly SQLiteConnection _db;
        private readonly ConfigRepository _configRepo;
        private readonly ISyncFileReader _fileReader;

        public PullService(
            SQLiteConnection db,
            ISyncFileReader fileReader)
        {
            _db = db;
            _configRepo = new ConfigRepository(db);
            _fileReader = fileReader;
        }

        public PullResult Pull()
        {
            string registerId = _configRepo.Get("register_id") ?? "01";
            string hubShare = _configRepo.Get("sync_hub_share");

            if (string.IsNullOrEmpty(hubShare))
            {
                return new PullResult { Success = false, Error = "sync_hub_share not configured" };
            }

            string outboxPath = SyncConfig.GetOutboxPath(hubShare);
            string[] files = _fileReader.ListFiles(outboxPath, "*.json");

            int totalApplied = 0;
            int totalSkipped = 0;
            string lastError = null;

            foreach (string file in files)
            {
                // Skip files from our own register
                string fileName = System.IO.Path.GetFileName(file);
                if (fileName.StartsWith(registerId + "_"))
                {
                    continue;
                }

                try
                {
                    string json = _fileReader.Read(file);
                    if (json == null) continue;

                    var batch = DeserializeBatch(json);

                    VerifySignature(batch, json);
                    ValidateBatch(batch);

                    int applied = ApplyBatch(batch);
                    totalApplied += applied;

                    _fileReader.MoveToArchive(file);
                }
                catch (SecurityException ex)
                {
                    lastError = "HMAC verification failed: " + ex.Message;
                    totalSkipped++;
                }
                catch (InvalidOperationException ex)
                {
                    lastError = "Validation failed: " + ex.Message;
                    totalSkipped++;
                }
                catch (Exception ex)
                {
                    lastError = ex.Message;
                    totalSkipped++;
                }
            }

            return new PullResult
            {
                Success = lastError == null,
                AppliedCount = totalApplied,
                SkippedCount = totalSkipped,
                Error = lastError
            };
        }

        private void VerifySignature(SyncBatch batch, string originalJson)
        {
            string hmacKey = _configRepo.Get("sync_hmac_key") ?? "default-hmac-key-change-me";

            if (hmacKey == "default-hmac-key-change-me")
            {
                throw new SecurityException("sync_hmac_key has not been configured. Set a unique key before syncing.");
            }

            byte[] keyBytes = Encoding.UTF8.GetBytes(hmacKey);

            // Recompute: serialize without signature, compute HMAC
            string savedSig = batch.Signature;
            batch.Signature = null;
            string payloadJson = JsonConvert.SerializeObject(batch, Formatting.None);
            batch.Signature = savedSig;

            byte[] payloadBytes = Encoding.UTF8.GetBytes(payloadJson);

            using (var hmac = new HMACSHA256(keyBytes))
            {
                byte[] expectedHash = hmac.ComputeHash(payloadBytes);
                string expectedSig = Convert.ToBase64String(expectedHash);

                if (!ConstantTimeEquals(savedSig, expectedSig))
                {
                    throw new SecurityException("HMAC signature mismatch — batch may be tampered");
                }
            }
        }

        private static bool ConstantTimeEquals(string a, string b)
        {
            if (a == null || b == null) return false;

            byte[] ba;
            byte[] bb;
            try
            {
                ba = Convert.FromBase64String(a);
                bb = Convert.FromBase64String(b);
            }
            catch (FormatException)
            {
                return false;
            }

            if (ba.Length != bb.Length) return false;

            int diff = 0;
            for (int i = 0; i < ba.Length; i++)
            {
                diff |= ba[i] ^ bb[i];
            }
            return diff == 0;
        }

        private static void ValidateBatch(SyncBatch batch)
        {
            if (batch.SchemaVersion != SyncConfig.SchemaVersion)
            {
                throw new InvalidOperationException(
                    string.Format("Schema version mismatch: expected {0}, got {1}",
                        SyncConfig.SchemaVersion, batch.SchemaVersion));
            }

            foreach (var evt in batch.Events)
            {
                if (!SyncConfig.SyncedTables.Contains(evt.TableName))
                {
                    throw new InvalidOperationException(
                        "Table not in sync whitelist: " + evt.TableName);
                }
            }
        }

        private int ApplyBatch(SyncBatch batch)
        {
            int applied = 0;

            using (var txn = _db.BeginTransaction())
            {
                try
                {
                    foreach (var evt in batch.Events)
                    {
                        switch (evt.Operation)
                        {
                            case "I":
                                ApplyInsert(evt);
                                applied++;
                                break;
                            case "U":
                                ApplyUpdate(evt);
                                applied++;
                                break;
                            case "D":
                                ApplyDelete(evt);
                                applied++;
                                break;
                        }
                    }

                    txn.Commit();
                }
                catch
                {
                    txn.Rollback();
                    throw;
                }
            }

            return applied;
        }

        private void ApplyInsert(SyncEvent evt)
        {
            if (evt.Data == null || evt.Data.Count == 0) return;

            var columns = new List<string>();
            var paramNames = new List<string>();
            var parameters = new List<SQLiteParameter>();

            int i = 0;
            foreach (var kvp in evt.Data)
            {
                if (!ValidColumnName.IsMatch(kvp.Key)) continue;
                columns.Add("[" + kvp.Key + "]");
                string paramName = "@p" + i;
                paramNames.Add(paramName);
                parameters.Add(new SQLiteParameter(paramName, kvp.Value ?? DBNull.Value));
                i++;
            }

            // INSERT OR IGNORE — skip if already exists
            string sql = string.Format("INSERT OR IGNORE INTO [{0}] ({1}) VALUES ({2})",
                evt.TableName,
                string.Join(", ", columns),
                string.Join(", ", paramNames));

            using (var cmd = new SQLiteCommand(sql, _db))
            {
                foreach (var p in parameters)
                {
                    cmd.Parameters.Add(p);
                }
                cmd.ExecuteNonQuery();
            }
        }

        private void ApplyUpdate(SyncEvent evt)
        {
            if (evt.Data == null || evt.Data.Count == 0) return;

            string keyColumn = PushService.GetKeyColumn(evt.TableName);
            if (keyColumn == null) return;

            // First try INSERT OR IGNORE (in case the row doesn't exist yet)
            ApplyInsert(evt);

            // Then UPDATE
            var setClauses = new List<string>();
            var parameters = new List<SQLiteParameter>();

            int i = 0;
            foreach (var kvp in evt.Data)
            {
                if (kvp.Key == "id") continue; // Don't update PK
                if (!ValidColumnName.IsMatch(kvp.Key)) continue;
                string paramName = "@u" + i;
                setClauses.Add(string.Format("[{0}] = {1}", kvp.Key, paramName));
                parameters.Add(new SQLiteParameter(paramName, kvp.Value ?? DBNull.Value));
                i++;
            }

            parameters.Add(new SQLiteParameter("@key", evt.RecordKey));

            string sql = string.Format("UPDATE [{0}] SET {1} WHERE [{2}] = @key",
                evt.TableName,
                string.Join(", ", setClauses),
                keyColumn);

            using (var cmd = new SQLiteCommand(sql, _db))
            {
                foreach (var p in parameters)
                {
                    cmd.Parameters.Add(p);
                }
                cmd.ExecuteNonQuery();
            }
        }

        private void ApplyDelete(SyncEvent evt)
        {
            string keyColumn = PushService.GetKeyColumn(evt.TableName);
            if (keyColumn == null) return;

            string sql = string.Format("DELETE FROM [{0}] WHERE [{1}] = @key",
                evt.TableName, keyColumn);

            using (var cmd = new SQLiteCommand(sql, _db))
            {
                cmd.Parameters.AddWithValue("@key", evt.RecordKey);
                cmd.ExecuteNonQuery();
            }
        }

        private static SyncBatch DeserializeBatch(string json)
        {
            return JsonConvert.DeserializeObject<SyncBatch>(json);
        }
    }

    public class PullResult
    {
        public bool Success { get; set; }
        public int AppliedCount { get; set; }
        public int SkippedCount { get; set; }
        public string Error { get; set; }
    }
}
