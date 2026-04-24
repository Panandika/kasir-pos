using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using Npgsql;
using Kasir.CloudSync.Generation;
using Kasir.CloudSync.Sinks;

namespace Kasir.CloudSync.Loader
{
    // One-shot bulk loader for Phase C. Pulls every row of every registered
    // TableMapping from local SQLite and ships them to Supabase in batches,
    // with FK constraints temporarily disabled so child rows can land before
    // their parents on the wire.
    //
    // Invocation:
    //   dotnet run --project Kasir.CloudSync -- --initial-load
    //
    // Phases of the load:
    //   1. Disable FK enforcement (SET session_replication_role = replica)
    //   2. For each table in LoadOrder, COPY-or-INSERT all SQLite rows
    //   3. Re-enable FK (SET session_replication_role = origin)
    //   4. Row-count parity check per table; exit 1 if any mismatch
    //
    // The order in LoadOrder is FK-dependency-safe: parents (departments,
    // accounts, subsidiaries, products, members, locations) before children
    // (sales, purchases, sale_items, etc.). Even with FK off, this order
    // makes manual debugging easier.
    public class InitialLoader
    {
        private readonly SqliteConnection _sqlite;
        private readonly string _pgConnectionString;
        private readonly ILogger<InitialLoader> _logger;

        // Tables ordered parent-first. The order is best-effort; FK enforcement
        // is disabled during load so cycle-safe and partial orderings still work.
        public static readonly IReadOnlyList<string> LoadOrder = new[]
        {
            "departments", "accounts", "locations", "credit_cards",
            "subsidiaries", "members",
            "products", "product_barcodes",
            "discounts", "discount_partners",
            "purchases",
            "sales", "sale_items",
            "cash_transactions", "memorial_journals",
            "orders",
            "stock_transfers", "stock_adjustments",
            "stock_movements"
        };

        public InitialLoader(SqliteConnection sqlite, string pgConnectionString, ILogger<InitialLoader> logger)
        {
            _sqlite = sqlite;
            _pgConnectionString = pgConnectionString;
            _logger = logger;
        }

        public async Task<InitialLoadResult> RunAsync(CancellationToken ct)
        {
            _logger.LogInformation("Initial load starting against {Connection}",
                MaskCredentials(_pgConnectionString));

            await using var pg = new NpgsqlConnection(_pgConnectionString);
            await pg.OpenAsync(ct).ConfigureAwait(false);

            await SetReplicationRole(pg, "replica", ct).ConfigureAwait(false);
            _logger.LogInformation("FK constraints disabled (session_replication_role=replica)");

            var perTable = new Dictionary<string, (long sqlite, long postgres)>();
            try
            {
                foreach (var tableName in LoadOrder)
                {
                    if (ct.IsCancellationRequested) break;
                    var mapping = TableMappings.Get(tableName);
                    if (mapping == null)
                    {
                        _logger.LogWarning("No TableMapping for {Table}; skipping", tableName);
                        continue;
                    }
                    var counts = await LoadTableAsync(pg, mapping, ct).ConfigureAwait(false);
                    perTable[tableName] = counts;
                }
            }
            finally
            {
                await SetReplicationRole(pg, "origin", ct).ConfigureAwait(false);
                _logger.LogInformation("FK constraints re-enabled (session_replication_role=origin)");
            }

            // Final parity report
            int mismatches = 0;
            foreach (var kv in perTable)
            {
                var (s, p) = kv.Value;
                if (s != p)
                {
                    mismatches++;
                    _logger.LogError("Parity MISMATCH {Table}: sqlite={Sqlite} postgres={Postgres}",
                        kv.Key, s, p);
                }
                else
                {
                    _logger.LogInformation("Parity OK {Table}: {Count} rows", kv.Key, s);
                }
            }

            return new InitialLoadResult
            {
                PerTableCounts = perTable,
                Mismatches = mismatches
            };
        }

        private async Task<(long sqlite, long postgres)> LoadTableAsync(
            NpgsqlConnection pg, TableMapping mapping, CancellationToken ct)
        {
            // Read everything from SQLite. For 343K rows this is fine; SQLite
            // stream-reads are cheap. For larger tables we'd batch via OFFSET
            // but staying simple for now.
            var rows = new List<IDictionary<string, object>>();
            using (var cmd = _sqlite.CreateCommand())
            {
                cmd.CommandText = $"SELECT * FROM [{mapping.TableName}];";
                using var reader = cmd.ExecuteReader();
                while (reader.Read())
                {
                    var row = RowMapper.FromReader(mapping, reader, out var warns);
                    foreach (var w in warns) _logger.LogDebug("{Warning}", w);
                    rows.Add(row);
                }
            }
            long sqliteCount = rows.Count;

            // Truncate the target table first so this command is idempotent.
            // For Phase C "fresh load on an empty Supabase project" this is safe;
            // for re-runs it ensures clean state. If anyone uses this against a
            // populated Supabase they'll see the warning in the runbook.
            await using (var trunc = pg.CreateCommand())
            {
                trunc.CommandText = $"TRUNCATE {mapping.TableName} CASCADE;";
                await trunc.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
            }

            // Ship in batches of 1000 via parameterised UPSERT (UpsertSqlBuilder).
            // NpgsqlBinaryImporter (COPY) would be faster but needs per-column
            // type binding code; for the 55MB / 343K-row scale, batched UPSERT
            // completes in a few minutes which is acceptable for a one-shot.
            const int batchSize = 1000;
            int loaded = 0;
            for (int offset = 0; offset < sqliteCount; offset += batchSize)
            {
                if (ct.IsCancellationRequested) break;
                var slice = rows.GetRange(offset, Math.Min(batchSize, rows.Count - offset));
                await using var cmd = pg.CreateCommand();
                cmd.CommandText = UpsertSqlBuilder.Build(mapping, slice.Count);
                GenericSink.BindParameters(cmd, mapping, slice);
                loaded += await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
                if (offset % 10000 == 0)
                {
                    _logger.LogInformation("{Table}: loaded {Loaded}/{Total}",
                        mapping.TableName, offset + slice.Count, sqliteCount);
                }
            }

            // Postgres row count after load
            long pgCount;
            await using (var c = pg.CreateCommand())
            {
                c.CommandText = $"SELECT COUNT(*) FROM {mapping.TableName};";
                pgCount = (long)(await c.ExecuteScalarAsync(ct).ConfigureAwait(false));
            }
            return (sqliteCount, pgCount);
        }

        private static async Task SetReplicationRole(NpgsqlConnection pg, string role, CancellationToken ct)
        {
            await using var cmd = pg.CreateCommand();
            cmd.CommandText = $"SET session_replication_role = {role};";
            await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }

        private static string MaskCredentials(string conn)
        {
            // Strip Password=... from log output
            var idx = conn.IndexOf("Password=", StringComparison.OrdinalIgnoreCase);
            if (idx < 0) return conn;
            var end = conn.IndexOf(';', idx);
            if (end < 0) end = conn.Length;
            return conn.Substring(0, idx) + "Password=***" + conn.Substring(end);
        }
    }

    public class InitialLoadResult
    {
        public IReadOnlyDictionary<string, (long Sqlite, long Postgres)> PerTableCounts { get; set; }
        public int Mismatches { get; set; }
    }
}
