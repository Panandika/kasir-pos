using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Diagnostics;
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
        public const int SupportedSchemaVersion = 1;

        public class SnapshotResult
        {
            public string Path;
            public string Sha256;
            public long SizeBytes;
            public IReadOnlyDictionary<string, long> MaxIds;
            public int RowCount;
        }

        // FK-safe table order. Iterates TableMappings.All.Keys (not LoadOrder)
        // because LoadOrder omits `shifts`; LoadOrder used only for ordering.
        public static IEnumerable<string> OrderedTableNames()
        {
            var loadOrder = InitialLoader.LoadOrder;
            var all = TableMappings.All.Keys.ToList();
            Debug.Assert(
                all.All(k => loadOrder.Contains(k) || k == "shifts"),
                "TableMappings.All contains an unexpected table not in LoadOrder and not 'shifts'");

            foreach (var t in loadOrder)
                if (all.Contains(t)) yield return t;
            // Tables present in All but not in LoadOrder (today: `shifts`)
            foreach (var t in all)
                if (!loadOrder.Contains(t)) yield return t;
        }

        public static async Task<SnapshotResult> BuildAsync(
            NpgsqlConnection pgConn,
            string outputPath,
            CancellationToken ct)
        {
            if (pgConn == null) throw new ArgumentNullException(nameof(pgConn));
            if (string.IsNullOrWhiteSpace(outputPath)) throw new ArgumentException(nameof(outputPath));

            if (File.Exists(outputPath)) File.Delete(outputPath);
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(outputPath)) ?? ".");

            using (var sqlite = new SqliteConnection($"Data Source={outputPath}"))
            {
                sqlite.Open();
                ApplySchemaFromKasirCore(sqlite);

                long totalRows = 0;
                var maxIds = new Dictionary<string, long>();

                using (var tx = sqlite.BeginTransaction())
                {
                    // Disable FK while bulk-loading; matches InitialLoader's
                    // replication_role=replica trick on the forward direction.
                    using (var cmd = sqlite.CreateCommand())
                    {
                        cmd.Transaction = tx;
                        cmd.CommandText = "PRAGMA foreign_keys=OFF;";
                        cmd.ExecuteNonQuery();
                    }

                    foreach (var table in OrderedTableNames())
                    {
                        ct.ThrowIfCancellationRequested();
                        var mapping = TableMappings.Get(table);
                        if (mapping == null) continue;

                        var loaded = await CopyTableAsync(pgConn, sqlite, tx, mapping, ct).ConfigureAwait(false);
                        totalRows += loaded.RowsCopied;
                        if (loaded.MaxId.HasValue) maxIds[table] = loaded.MaxId.Value;
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
                sqlite.Close();

                return new SnapshotResult
                {
                    Path = outputPath,
                    Sha256 = ComputeSha256(outputPath),
                    SizeBytes = new FileInfo(outputPath).Length,
                    MaxIds = maxIds,
                    RowCount = checked((int)totalRows),
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
