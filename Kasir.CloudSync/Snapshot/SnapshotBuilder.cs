using System;
using System.Collections.Generic;
using System.Data.Common;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Kasir.CloudSync.Generation;
using Kasir.CloudSync.Loader;
using Kasir.CloudSync.Mappers;
using Microsoft.Data.Sqlite;
using Npgsql;

namespace Kasir.CloudSync.Snapshot
{
    // Reads every mirror table from Postgres and writes a self-contained SQLite
    // snapshot at outputPath. Schema applied from Kasir.Core's embedded
    // Schema.sql (the local POS shape, not the cloud mirror DDL).
    //
    // Hub side of the polling protocol; the BackgroundService wrapper lives in
    // SnapshotBuilderWorker (uses Npgsql to poll snapshot_build_requests).
    //
    // PRD story: US-P2-2
    // Plan: ../../../.omc/plans/cloud-import-pairing.md §6
    public class SnapshotBuilder
    {
        // Version of the snapshot *file* contract, checked by CloudSnapshotRestorer and
        // the snapshot-download edge function (both reject anything above 1 in v2.9.x).
        // Bump only when an older POS can no longer restore the file (e.g. a local
        // Schema.sql change it can't open) - not for mirror-only changes such as adding
        // a TableMapping, which just fills a table every POS schema already has.
        public const int SupportedSchemaVersion = 1;

        public class SnapshotResult
        {
            public string Path;
            public string Sha256;
            public long SizeBytes;
            public IReadOnlyDictionary<string, long> MaxIds;
            public int RowCount;
            // Mapped columns/tables the cloud mirror doesn't have (schema drift); the
            // snapshot keeps the local defaults for them.
            public List<string> MissingInCloud = new List<string>();
        }

        // FK-safe order of the tables a snapshot restores: LoadOrder first, then
        // mapped tables LoadOrder doesn't cover (today: purchase_items). Mirror-only
        // mappings (RestoreToRegister = false, today: shifts) are left out, so their
        // local tables stay empty in the snapshot exactly as before they were mirrored.
        public static IEnumerable<string> OrderedTableNames()
        {
            var loadOrder = InitialLoader.LoadOrder;
            var restorable = TableMappings.All
                .Where(kv => kv.Value.RestoreToRegister)
                .Select(kv => kv.Key)
                .ToList();

            foreach (var t in loadOrder)
                if (restorable.Contains(t)) yield return t;
            foreach (var t in restorable)
                if (!loadOrder.Contains(t)) yield return t;
        }

        public static async Task<SnapshotResult> BuildAsync(
            NpgsqlConnection pgConn,
            string outputPath,
            CancellationToken ct,
            Action<string> warn = null)
        {
            if (pgConn == null) throw new ArgumentNullException(nameof(pgConn));
            if (string.IsNullOrWhiteSpace(outputPath)) throw new ArgumentException(nameof(outputPath));

            if (File.Exists(outputPath)) File.Delete(outputPath);
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(outputPath)) ?? ".");

            // Pooling off: a pooled handle keeps the file open after Close(), so the
            // WAL is never checkpointed and the .db we hash/upload would miss data.
            using (var sqlite = new SqliteConnection($"Data Source={outputPath};Pooling=False"))
            {
                sqlite.Open();
                ApplySchemaFromKasirCore(sqlite);

                long totalRows = 0;
                var maxIds = new Dictionary<string, long>();
                var missing = new List<string>();
                var cloudColumns = await LoadCloudColumnsAsync(pgConn, ct).ConfigureAwait(false);

                // Disable FK while bulk-loading; matches InitialLoader's
                // replication_role=replica trick on the forward direction. Must run
                // OUTSIDE the transaction: SQLite ignores PRAGMA foreign_keys inside one,
                // so legacy orphan rows (e.g. lines whose header was never mirrored)
                // aborted the whole build.
                using (var cmd = sqlite.CreateCommand())
                {
                    cmd.CommandText = "PRAGMA foreign_keys=OFF;";
                    cmd.ExecuteNonQuery();
                }

                using (var tx = sqlite.BeginTransaction())
                {
                    foreach (var table in OrderedTableNames())
                    {
                        ct.ThrowIfCancellationRequested();
                        var mapping = TableMappings.Get(table);
                        if (mapping == null) continue;

                        if (!cloudColumns.TryGetValue(mapping.TableName, out var present))
                        {
                            missing.Add(mapping.TableName);
                            warn?.Invoke($"table {mapping.TableName} not in cloud mirror; left empty");
                            continue;
                        }
                        var absent = mapping.Columns.Where(c => !present.Contains(c.Name)).Select(c => c.Name).ToList();
                        if (absent.Count > 0)
                        {
                            foreach (var c in absent) missing.Add(mapping.TableName + "." + c);
                            warn?.Invoke($"{mapping.TableName}: column(s) not in cloud mirror, using local defaults: {string.Join(", ", absent)}");
                            mapping = new TableMapping(mapping.TableName,
                                mapping.Columns.Where(c => present.Contains(c.Name)).ToList());
                        }

                        var loaded = await CopyTableAsync(pgConn, sqlite, tx, mapping, ct).ConfigureAwait(false);
                        totalRows += loaded.RowsCopied;
                        if (loaded.MaxId.HasValue) maxIds[table] = loaded.MaxId.Value;
                    }

                    // Schema.sql's sync triggers queue every inserted row in the outbox.
                    // A snapshot is a baseline, not pending local changes: a register
                    // restored from it must not push ~all rows back to the hub as new.
                    using (var clear = sqlite.CreateCommand())
                    {
                        clear.Transaction = tx;
                        clear.CommandText = "DELETE FROM sync_queue;";
                        clear.ExecuteNonQuery();
                    }

                    tx.Commit();
                }

                using (var cmd = sqlite.CreateCommand())
                {
                    cmd.CommandText = "PRAGMA foreign_keys=ON;";
                    cmd.ExecuteNonQuery();
                    cmd.CommandText = "PRAGMA integrity_check;";
                    using var rd = cmd.ExecuteReader();
                    if (!rd.Read() || rd.GetString(0) != "ok")
                    {
                        throw new InvalidOperationException("PRAGMA integrity_check failed on built snapshot");
                    }
                }
                FinalizeSingleFile(sqlite);
                sqlite.Close();

                return new SnapshotResult
                {
                    Path = outputPath,
                    Sha256 = ComputeSha256(outputPath),
                    SizeBytes = new FileInfo(outputPath).Length,
                    MaxIds = maxIds,
                    RowCount = checked((int)totalRows),
                    MissingInCloud = missing,
                };
            }
        }

        private static void ApplySchemaFromKasirCore(SqliteConnection sqlite)
        {
            // Schema.sql is embedded in Kasir.Core under the resource name
            // "Kasir.Data.Schema.sql" (see Kasir.Core.csproj).
            var coreAsm = typeof(Kasir.Data.DbConnection).Assembly;
            using var stream = coreAsm.GetManifestResourceStream("Kasir.Data.Schema.sql")
                ?? throw new InvalidOperationException("Schema.sql not embedded in Kasir.Core assembly");
            using var rdr = new StreamReader(stream, Encoding.UTF8);
            var ddl = rdr.ReadToEnd();
            using var cmd = sqlite.CreateCommand();
            cmd.CommandText = ddl;
            cmd.ExecuteNonQuery();
        }

        // public-schema table -> column names, so drift between TableMappings (local
        // POS shape) and the cloud mirror doesn't abort the whole snapshot.
        private static async Task<Dictionary<string, HashSet<string>>> LoadCloudColumnsAsync(
            NpgsqlConnection pg, CancellationToken ct)
        {
            var map = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
            using var cmd = pg.CreateCommand();
            cmd.CommandText = "SELECT table_name, column_name FROM information_schema.columns WHERE table_schema = 'public'";
            using var rd = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
            while (await rd.ReadAsync(ct).ConfigureAwait(false))
            {
                var t = rd.GetString(0);
                if (!map.TryGetValue(t, out var set)) map[t] = set = new HashSet<string>(StringComparer.Ordinal);
                set.Add(rd.GetString(1));
            }
            return map;
        }

        private class TableLoadResult
        {
            public long RowsCopied;
            public long? MaxId;
        }

        private static async Task<TableLoadResult> CopyTableAsync(
            NpgsqlConnection pg,
            SqliteConnection sqlite,
            SqliteTransaction tx,
            TableMapping mapping,
            CancellationToken ct)
        {
            var cols = mapping.Columns.Select(c => c.Name).ToList();
            string select = $"SELECT {string.Join(", ", cols)} FROM {mapping.TableName};";

            string insert = BuildInsertSql(mapping, cols);

            long rowsCopied = 0;
            long? maxId = null;

            using var pgCmd = pg.CreateCommand();
            pgCmd.CommandText = select;
            using var reader = await pgCmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
            while (await reader.ReadAsync(ct).ConfigureAwait(false))
            {
                var row = ReverseRowMapper.FromReaderCore(mapping, reader);
                using var ins = sqlite.CreateCommand();
                ins.Transaction = tx;
                ins.CommandText = insert;
                foreach (var col in cols)
                {
                    var p = ins.CreateParameter();
                    p.ParameterName = "@" + col;
                    p.Value = row[col] ?? DBNull.Value;
                    ins.Parameters.Add(p);
                }
                ins.ExecuteNonQuery();
                rowsCopied++;

                if (cols.Contains("id") && row["id"] is long lid)
                {
                    if (!maxId.HasValue || lid > maxId.Value) maxId = lid;
                }
                else if (cols.Contains("id") && row["id"] is int iid)
                {
                    if (!maxId.HasValue || iid > maxId.Value) maxId = iid;
                }
            }
            return new TableLoadResult { RowsCopied = rowsCopied, MaxId = maxId };
        }

        internal static string BuildInsertSql(TableMapping mapping, IReadOnlyList<string> cols)
        {
            var colList = string.Join(", ", cols.Select(c => "[" + c + "]"));
            var paramList = string.Join(", ", cols.Select(c => "@" + c));
            return $"INSERT OR REPLACE INTO [{mapping.TableName}] ({colList}) VALUES ({paramList});";
        }

        // Schema.sql turns on WAL. Fold the WAL back into the main file and switch to
        // rollback journaling so the snapshot is one self-contained .db (the -wal file
        // is never uploaded) and its SHA-256 covers all the data.
        internal static void FinalizeSingleFile(SqliteConnection sqlite)
        {
            using var cmd = sqlite.CreateCommand();
            cmd.CommandText = "PRAGMA wal_checkpoint(TRUNCATE);";
            cmd.ExecuteNonQuery();
            cmd.CommandText = "PRAGMA journal_mode=DELETE;";
            cmd.ExecuteNonQuery();
            // Reclaim space from the cleared outbox and bulk-load churn: the snapshot
            // is downloaded by every register being commissioned.
            cmd.CommandText = "VACUUM;";
            cmd.ExecuteNonQuery();
        }

        public static string ComputeSha256(string filePath)
        {
            using var sha = SHA256.Create();
            using var fs = File.OpenRead(filePath);
            var hash = sha.ComputeHash(fs);
            var sb = new StringBuilder(hash.Length * 2);
            foreach (var b in hash) sb.Append(b.ToString("x2"));
            return sb.ToString();
        }
    }
}
