using System;
using System.Collections.Generic;
using Microsoft.Data.Sqlite;
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

        private readonly SqliteConnection _db;
        private readonly ConfigRepository _configRepo;
        private readonly ISyncFileReader _fileReader;

        public PullService(
            SqliteConnection db,
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
                // Skip files from our own register.
                // Path.GetFileName only splits on the OS-native separator, so handle both
                // '\' (Windows SMB paths) and '/' explicitly.
                string fileName = file;
                int sep = fileName.LastIndexOfAny(new[] { '\\', '/' });
                if (sep >= 0) fileName = fileName.Substring(sep + 1);
                if (fileName.StartsWith(registerId + "_"))
                {
                    continue;
                }

                // Phase 1 — parse/verify/validate. Any failure here means the file is
                // permanently bad (unparseable, tampered, or invalid), so it is moved to
                // quarantine; leaving it in place would let it re-consume the limited inbox
                // window on every pull and starve good files (F44).
                SyncBatch batch;
                try
                {
                    string json = _fileReader.Read(file);
                    if (json == null) continue;

                    batch = DeserializeBatch(json);
                    VerifySignature(batch, json);
                    ValidateBatch(batch);
                }
                catch (SecurityException ex)
                {
                    lastError = "HMAC verification failed: " + ex.Message;
                    totalSkipped++;
                    _fileReader.MoveToQuarantine(file);
                    continue;
                }
                catch (InvalidOperationException ex)
                {
                    lastError = "Validation failed: " + ex.Message;
                    totalSkipped++;
                    _fileReader.MoveToQuarantine(file);
                    continue;
                }
                catch (Exception ex)
                {
                    // Unparseable JSON / oversized file etc. — also permanently bad.
                    lastError = ex.Message;
                    totalSkipped++;
                    _fileReader.MoveToQuarantine(file);
                    continue;
                }

                // Phase 2 — apply. A failure here (e.g. the DB is briefly locked) is
                // transient, so the file is LEFT in the inbox to retry on the next pull
                // rather than quarantined.
                try
                {
                    int applied = ApplyBatch(batch);
                    totalApplied += applied;
                    _fileReader.MoveToArchive(file);
                }
                catch (Exception ex)
                {
                    lastError = "Apply failed (will retry): " + ex.Message;
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
                                ApplyChildren(evt);
                                applied++;
                                break;
                            case "U":
                                ApplyUpdate(evt);
                                ApplyChildren(evt);
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

        // Replace the parent's child detail rows (e.g. sale_items) so they replicate with
        // the parent (F25). Child rows are keyed by journal_no; we delete the existing set
        // and re-insert the incoming set, skipping the source register's autoincrement id
        // so the local PK is assigned locally (same F24 rationale).
        private void ApplyChildren(SyncEvent evt)
        {
            if (evt.Children == null || evt.Children.Count == 0) return;

            foreach (var kv in evt.Children)
            {
                string childTable = kv.Key;
                // Whitelist: only known child tables may be written (the table name comes
                // from the batch, so it must be validated before use in SQL).
                if (!SyncConfig.ChildTables.ContainsValue(childTable)) continue;

                using (var del = _db.CreateCommand())
                {
                    del.CommandText = string.Format("DELETE FROM [{0}] WHERE [journal_no] = @key", childTable);
                    del.Parameters.AddWithValue("@key", evt.RecordKey);
                    del.ExecuteNonQuery();
                }

                foreach (var row in kv.Value)
                {
                    if (row == null || row.Count == 0) continue;

                    var columns = new List<string>();
                    var paramNames = new List<string>();
                    var parameters = new List<SqliteParameter>();

                    int i = 0;
                    foreach (var cell in row)
                    {
                        if (!ValidColumnName.IsMatch(cell.Key)) continue;
                        if (cell.Key == "id") continue; // let the local PK autoincrement
                        columns.Add("[" + cell.Key + "]");
                        string p = "@c" + i;
                        paramNames.Add(p);
                        parameters.Add(new SqliteParameter(p, cell.Value ?? DBNull.Value));
                        i++;
                    }
                    if (columns.Count == 0) continue;

                    string sql = string.Format("INSERT INTO [{0}] ({1}) VALUES ({2})",
                        childTable, string.Join(", ", columns), string.Join(", ", paramNames));
                    using (var cmd = _db.CreateCommand())
                    {
                        cmd.CommandText = sql;
                        foreach (var p in parameters) cmd.Parameters.Add(p);
                        cmd.ExecuteNonQuery();
                    }
                }
            }
        }

        private void ApplyInsert(SyncEvent evt)
        {
            if (evt.Data == null || evt.Data.Count == 0) return;

            // For tables with a natural unique key (journal_no, product_code, ...) the
            // local INTEGER PK must autoincrement locally — replicating the source
            // register's autoincrement id causes cross-register PK collisions that
            // INSERT OR IGNORE silently drops (F24). Idempotency comes from the natural
            // key's UNIQUE constraint instead. Only id-keyed tables keep their id.
            string keyColumn = PushService.GetKeyColumn(evt.TableName);
            bool skipSourceId = keyColumn != null && keyColumn != "id";

            var columns = new List<string>();
            var paramNames = new List<string>();
            var parameters = new List<SqliteParameter>();

            int i = 0;
            foreach (var kvp in evt.Data)
            {
                if (!ValidColumnName.IsMatch(kvp.Key)) continue;
                if (skipSourceId && kvp.Key == "id") continue;
                columns.Add("[" + kvp.Key + "]");
                string paramName = "@p" + i;
                paramNames.Add(paramName);
                parameters.Add(new SqliteParameter(paramName, kvp.Value ?? DBNull.Value));
                i++;
            }

            // INSERT OR IGNORE — skip if already exists
            string sql = string.Format("INSERT OR IGNORE INTO [{0}] ({1}) VALUES ({2})",
                evt.TableName,
                string.Join(", ", columns),
                string.Join(", ", paramNames));

            using (var cmd = _db.CreateCommand())
            {
                cmd.CommandText = sql;
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
            var parameters = new List<SqliteParameter>();

            int i = 0;
            foreach (var kvp in evt.Data)
            {
                if (kvp.Key == "id") continue; // Don't update PK
                if (!ValidColumnName.IsMatch(kvp.Key)) continue;
                string paramName = "@u" + i;
                setClauses.Add(string.Format("[{0}] = {1}", kvp.Key, paramName));
                parameters.Add(new SqliteParameter(paramName, kvp.Value ?? DBNull.Value));
                i++;
            }

            parameters.Add(new SqliteParameter("@key", evt.RecordKey));

            string sql = string.Format("UPDATE [{0}] SET {1} WHERE [{2}] = @key",
                evt.TableName,
                string.Join(", ", setClauses),
                keyColumn);

            using (var cmd = _db.CreateCommand())
            {
                cmd.CommandText = sql;
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

            using (var cmd = _db.CreateCommand())
            {
                cmd.CommandText = sql;
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
