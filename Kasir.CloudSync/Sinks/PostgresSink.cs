using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Npgsql;
using NpgsqlTypes;
using Kasir.CloudSync.Models;

namespace Kasir.CloudSync.Sinks
{
    // Upserts Product rows into Supabase Postgres via parameterised INSERT ...
    // ON CONFLICT (product_code) DO UPDATE. The SQL is built once per call and
    // keyed off the batch size; Npgsql handles parameter binding.
    //
    // Phase A scope: single-table (products). US-A2 proves the pattern;
    // other tables' sinks follow the same shape in Phases B/C via the mapper
    // generator.
    public class PostgresSink : IProductSink
    {
        private readonly string _connectionString;

        public PostgresSink(string connectionString)
        {
            _connectionString = connectionString;
        }

        public async Task<int> UpsertAsync(IReadOnlyCollection<Product> products, CancellationToken ct)
        {
            if (products == null || products.Count == 0) return 0;

            await using var conn = new NpgsqlConnection(_connectionString);
            await conn.OpenAsync(ct).ConfigureAwait(false);

            await using var cmd = conn.CreateCommand();
            cmd.CommandText = BuildUpsertSql(products.Count);
            BindParameters(cmd, products);

            return await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }

        // Internal so tests can assert the generated SQL without requiring a
        // live Postgres connection.
        internal static string BuildUpsertSql(int batchSize)
        {
            var columns = new[]
            {
                "product_code","name","barcode","dept_code","account_code","category_code",
                "unit","unit1","unit2","status","vendor_code","location",
                "is_consignment","open_price",
                "price","price1","price2","price3","price4","buying_price",
                "qty_min","qty_max","qty_order","factor","conversion1","conversion2",
                "qty_break2","qty_break3","changed_at","changed_by"
            };

            var sb = new StringBuilder();
            sb.Append("INSERT INTO products (");
            sb.Append(string.Join(", ", columns));
            sb.Append(") VALUES ");

            for (int row = 0; row < batchSize; row++)
            {
                if (row > 0) sb.Append(", ");
                sb.Append('(');
                for (int c = 0; c < columns.Length; c++)
                {
                    if (c > 0) sb.Append(", ");
                    sb.Append('@').Append(columns[c]).Append('_').Append(row);
                }
                sb.Append(')');
            }

            sb.Append(" ON CONFLICT (product_code) DO UPDATE SET ");
            for (int c = 0; c < columns.Length; c++)
            {
                if (columns[c] == "product_code") continue;
                if (c > 0) sb.Append(", ");
                sb.Append(columns[c]).Append(" = EXCLUDED.").Append(columns[c]);
            }
            sb.Append(';');
            return sb.ToString();
        }

        internal static void BindParameters(NpgsqlCommand cmd, IReadOnlyCollection<Product> products)
        {
            int row = 0;
            foreach (var p in products)
            {
                AddParam(cmd, "product_code", row, NpgsqlDbType.Text, p.ProductCode);
                AddParam(cmd, "name", row, NpgsqlDbType.Text, p.Name);
                AddParam(cmd, "barcode", row, NpgsqlDbType.Text, (object)p.Barcode ?? System.DBNull.Value);
                AddParam(cmd, "dept_code", row, NpgsqlDbType.Text, (object)p.DeptCode ?? System.DBNull.Value);
                AddParam(cmd, "account_code", row, NpgsqlDbType.Text, (object)p.AccountCode ?? System.DBNull.Value);
                AddParam(cmd, "category_code", row, NpgsqlDbType.Text, (object)p.CategoryCode ?? System.DBNull.Value);
                AddParam(cmd, "unit", row, NpgsqlDbType.Text, (object)p.Unit ?? System.DBNull.Value);
                AddParam(cmd, "unit1", row, NpgsqlDbType.Text, (object)p.Unit1 ?? System.DBNull.Value);
                AddParam(cmd, "unit2", row, NpgsqlDbType.Text, (object)p.Unit2 ?? System.DBNull.Value);
                AddParam(cmd, "status", row, NpgsqlDbType.Text, (object)p.Status ?? System.DBNull.Value);
                AddParam(cmd, "vendor_code", row, NpgsqlDbType.Text, (object)p.VendorCode ?? System.DBNull.Value);
                AddParam(cmd, "location", row, NpgsqlDbType.Text, (object)p.Location ?? System.DBNull.Value);
                AddParam(cmd, "is_consignment", row, NpgsqlDbType.Text, (object)p.IsConsignment ?? System.DBNull.Value);
                AddParam(cmd, "open_price", row, NpgsqlDbType.Text, (object)p.OpenPrice ?? System.DBNull.Value);
                AddParam(cmd, "price", row, NpgsqlDbType.Bigint, p.Price);
                AddParam(cmd, "price1", row, NpgsqlDbType.Bigint, p.Price1);
                AddParam(cmd, "price2", row, NpgsqlDbType.Bigint, p.Price2);
                AddParam(cmd, "price3", row, NpgsqlDbType.Bigint, p.Price3);
                AddParam(cmd, "price4", row, NpgsqlDbType.Bigint, p.Price4);
                AddParam(cmd, "buying_price", row, NpgsqlDbType.Bigint, p.BuyingPrice);
                AddParam(cmd, "qty_min", row, NpgsqlDbType.Bigint, p.QtyMin);
                AddParam(cmd, "qty_max", row, NpgsqlDbType.Bigint, p.QtyMax);
                AddParam(cmd, "qty_order", row, NpgsqlDbType.Bigint, p.QtyOrder);
                AddParam(cmd, "factor", row, NpgsqlDbType.Bigint, p.Factor);
                AddParam(cmd, "conversion1", row, NpgsqlDbType.Bigint, p.Conversion1);
                AddParam(cmd, "conversion2", row, NpgsqlDbType.Bigint, p.Conversion2);
                AddParam(cmd, "qty_break2", row, NpgsqlDbType.Bigint, p.QtyBreak2);
                AddParam(cmd, "qty_break3", row, NpgsqlDbType.Bigint, p.QtyBreak3);
                AddParam(cmd, "changed_at", row, NpgsqlDbType.TimestampTz,
                    p.ChangedAt.HasValue ? (object)p.ChangedAt.Value : System.DBNull.Value);
                AddParam(cmd, "changed_by", row, NpgsqlDbType.Integer,
                    p.ChangedBy.HasValue ? (object)p.ChangedBy.Value : System.DBNull.Value);
                row++;
            }
        }

        private static void AddParam(NpgsqlCommand cmd, string name, int row, NpgsqlDbType type, object value)
        {
            var p = cmd.CreateParameter();
            p.ParameterName = $"@{name}_{row}";
            p.NpgsqlDbType = type;
            p.Value = value ?? System.DBNull.Value;
            cmd.Parameters.Add(p);
        }
    }
}
