using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Npgsql;
using NpgsqlTypes;
using Kasir.CloudSync.Generation;

namespace Kasir.CloudSync.Sinks
{
    // Single sink that handles UPSERT for any TableMapping. Replaces the per-table
    // PostgresSink classes that would otherwise multiply across 17 tables. Gives
    // up some compile-time type safety vs hand-written sinks but pairs with the
    // schema-drift CI check (US-B3) to catch drift before runtime.
    public class GenericSink
    {
        private readonly string _connectionString;

        public GenericSink(string connectionString)
        {
            _connectionString = connectionString;
        }

        public async Task<int> UpsertAsync(
            TableMapping mapping,
            IReadOnlyCollection<IDictionary<string, object>> rows,
            CancellationToken ct)
        {
            if (rows == null || rows.Count == 0) return 0;

            await using var conn = new NpgsqlConnection(_connectionString);
            await conn.OpenAsync(ct).ConfigureAwait(false);

            await using var cmd = conn.CreateCommand();
            cmd.CommandText = UpsertSqlBuilder.Build(mapping, rows.Count);
            BindParameters(cmd, mapping, rows);

            return await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }

        internal static void BindParameters(
            NpgsqlCommand cmd,
            TableMapping mapping,
            IReadOnlyCollection<IDictionary<string, object>> rows)
        {
            int rowIndex = 0;
            foreach (var row in rows)
            {
                foreach (var col in mapping.Columns)
                {
                    var p = cmd.CreateParameter();
                    p.ParameterName = $"@{col.Name}_{rowIndex}";
                    p.NpgsqlDbType = ToNpgsqlType(col.Kind);

                    object value = row.TryGetValue(col.Name, out var v) ? v : null;
                    p.Value = value ?? (object)DBNull.Value;
                    cmd.Parameters.Add(p);
                }
                rowIndex++;
            }
        }

        private static NpgsqlDbType ToNpgsqlType(ColumnKind kind)
        {
            switch (kind)
            {
                case ColumnKind.Text:        return NpgsqlDbType.Text;
                case ColumnKind.BigintMoney: return NpgsqlDbType.Bigint;
                case ColumnKind.BigintQty:   return NpgsqlDbType.Bigint;
                case ColumnKind.Int:         return NpgsqlDbType.Integer;
                case ColumnKind.TimestampTz: return NpgsqlDbType.TimestampTz;
                default: throw new ArgumentOutOfRangeException(nameof(kind));
            }
        }
    }
}
