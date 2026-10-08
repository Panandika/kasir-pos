using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Npgsql;
using NpgsqlTypes;

namespace Kasir.CloudSync.Pull
{
    public sealed class PostgresPosRequestSource : IPosRequestSource
    {
        // Targets the hub applies (single-hub-applicant model, OB-12).
        internal const string PendingSql =
            @"SELECT id, request_kind, idempotency_key, product_code, qty, unit_cost,
                     vendor_code, doc_no, target_register, payload::text, happened_at, created_at
              FROM pos_stock_requests
              WHERE applied_at IS NULL AND target_register IN ('hub', 'ALL')
              ORDER BY created_at ASC,
                       CASE request_kind
                         WHEN 'NEW_PRODUCT' THEN 0 WHEN 'PRODUCT_STATUS' THEN 1
                         WHEN 'BARCODE_LINK' THEN 2 WHEN 'PURCHASE' THEN 3
                         WHEN 'RETURN_OUT' THEN 4 WHEN 'OPNAME' THEN 5
                         WHEN 'VENDOR_BILL' THEN 6 ELSE 9 END ASC,
                       id ASC
              LIMIT @limit";

        internal const string MarkSql =
            @"UPDATE pos_stock_requests
              SET applied_at = @at, applied_by_register = @reg
              WHERE id = @id AND applied_at IS NULL";

        private readonly string _connectionString;

        public PostgresPosRequestSource(string connectionString)
        {
            _connectionString = connectionString;
        }

        public async Task<IReadOnlyList<PosStockRequest>> FetchPendingAsync(int limit, CancellationToken ct)
        {
            var list = new List<PosStockRequest>();
            await using var conn = new NpgsqlConnection(_connectionString);
            await conn.OpenAsync(ct).ConfigureAwait(false);
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = PendingSql;
            cmd.Parameters.AddWithValue("@limit", limit);
            await using var r = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
            while (await r.ReadAsync(ct).ConfigureAwait(false))
            {
                list.Add(new PosStockRequest
                {
                    Id = r.GetGuid(0),
                    RequestKind = r.GetString(1),
                    IdempotencyKey = r.GetString(2),
                    ProductCode = r.IsDBNull(3) ? null : r.GetString(3),
                    Qty = r.IsDBNull(4) ? (int?)null : Convert.ToInt32(r.GetValue(4)),
                    UnitCost = r.IsDBNull(5) ? (long?)null : Convert.ToInt64(r.GetValue(5)),
                    VendorCode = r.IsDBNull(6) ? null : r.GetString(6),
                    DocNo = r.IsDBNull(7) ? null : r.GetString(7),
                    TargetRegister = r.IsDBNull(8) ? null : r.GetString(8),
                    PayloadJson = r.IsDBNull(9) ? null : r.GetString(9),
                    HappenedAt = r.GetFieldValue<DateTimeOffset>(10),
                    CreatedAt = r.GetFieldValue<DateTimeOffset>(11)
                });
            }
            return list;
        }

        public async Task<bool> MarkAppliedAsync(Guid id, string registerId, DateTimeOffset appliedAt, CancellationToken ct)
        {
            await using var conn = new NpgsqlConnection(_connectionString);
            await conn.OpenAsync(ct).ConfigureAwait(false);
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = MarkSql;
            cmd.Parameters.Add(new NpgsqlParameter("@at", NpgsqlDbType.TimestampTz) { Value = appliedAt.ToUniversalTime() });
            cmd.Parameters.AddWithValue("@reg", registerId ?? "");
            cmd.Parameters.AddWithValue("@id", id);
            return await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false) == 1;
        }
    }
}
