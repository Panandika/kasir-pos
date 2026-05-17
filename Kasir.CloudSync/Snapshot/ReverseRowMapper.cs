using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Globalization;
using Kasir.CloudSync.Generation;
using Kasir.CloudSync.Mappers;
using Npgsql;

namespace Kasir.CloudSync.Snapshot
{
    // Inverse of RowMapper: reads a row from Postgres (via NpgsqlDataReader)
    // and produces a column-name -> SQLite-compatible value dictionary.
    //
    // Asymmetries to be aware of:
    //   * SQLite has no native TIMESTAMPTZ. We store ISO-8601 text and rely on
    //     Microsoft.Data.Sqlite to round-trip via string. Postgres can hand us
    //     DateTime (kind=Utc) or DateTimeOffset depending on Npgsql version;
    //     handle both.
    //   * SQLite cares about NULL vs "" because some legacy rows store empty
    //     strings where the schema allows it. Postgres NULL stays NULL, empty
    //     string stays empty string.
    //   * BigintMoney values are stored as cents in both worlds; no scaling.
    //
    // PRD story: US-P2-1
    public static class ReverseRowMapper
    {
        public static IDictionary<string, object> FromReader(
            TableMapping mapping,
            NpgsqlDataReader reader)
        {
            return FromReaderCore(mapping, reader);
        }

        // Test seam: works with any DbDataReader for in-memory unit tests.
        internal static IDictionary<string, object> FromReaderCore(
            TableMapping mapping,
            DbDataReader reader)
        {
            if (mapping == null) throw new ArgumentNullException(nameof(mapping));
            if (reader == null) throw new ArgumentNullException(nameof(reader));

            var result = new Dictionary<string, object>(mapping.Columns.Count);
            foreach (var col in mapping.Columns)
            {
                int ordinal = FindOrdinal(reader, col.Name);
                if (ordinal < 0)
                {
                    // Postgres has no such column; emit NULL so INSERT uses default.
                    result[col.Name] = null;
                    continue;
                }
                if (reader.IsDBNull(ordinal))
                {
                    result[col.Name] = null;
                    continue;
                }
                result[col.Name] = MapValue(col.Kind, reader, ordinal, col.Name, mapping.TableName);
            }
            return result;
        }

        // Per-kind conversion. Public for tests + clarity.
        internal static object MapValue(
            ColumnKind kind,
            DbDataReader reader,
            int ordinal,
            string columnName,
            string tableName)
        {
            switch (kind)
            {
                case ColumnKind.Text:
                    // Preserve empty-string vs NULL semantics. IsDBNull was already checked.
                    return reader.GetString(ordinal);

                case ColumnKind.BigintMoney:
                case ColumnKind.BigintQty:
                    // Postgres bigint -> long; SQLite INTEGER accepts long natively.
                    return reader.GetInt64(ordinal);

                case ColumnKind.Int:
                    // Postgres int -> int (Microsoft.Data.Sqlite stores in INTEGER).
                    // Some columns may come back as long via Npgsql when Postgres widened
                    // them; fall through to safe widening.
                    try
                    {
                        return reader.GetInt32(ordinal);
                    }
                    catch (InvalidCastException)
                    {
                        return reader.GetInt64(ordinal);
                    }

                case ColumnKind.TimestampTz:
                    // Postgres TIMESTAMPTZ comes back as DateTime (UTC) or
                    // DateTimeOffset depending on Npgsql config. Either way,
                    // emit a canonical ISO-8601 text string that DateParser
                    // (the forward direction) can re-parse symmetrically.
                    return GetTimestampAsIsoText(reader, ordinal);

                default:
                    throw new InvalidOperationException(
                        $"Unmapped ColumnKind {kind} for {tableName}.{columnName}");
            }
        }

        private static string GetTimestampAsIsoText(DbDataReader reader, int ordinal)
        {
            object raw = reader.GetValue(ordinal);
            switch (raw)
            {
                case DateTimeOffset dto:
                    return dto.ToUniversalTime()
                              .ToString("yyyy-MM-ddTHH:mm:ss.fffK", CultureInfo.InvariantCulture);
                case DateTime dt:
                    var asOffset = dt.Kind == DateTimeKind.Unspecified
                        ? new DateTimeOffset(DateTime.SpecifyKind(dt, DateTimeKind.Utc), TimeSpan.Zero)
                        : new DateTimeOffset(dt.ToUniversalTime(), TimeSpan.Zero);
                    return asOffset.ToString("yyyy-MM-ddTHH:mm:ss.fffK", CultureInfo.InvariantCulture);
                case string s:
                    // Defensive: some drivers may return text. Trust upstream.
                    return s;
                default:
                    throw new InvalidOperationException(
                        $"Unexpected TIMESTAMPTZ runtime type {raw.GetType().FullName}");
            }
        }

        private static int FindOrdinal(DbDataReader reader, string column)
        {
            for (int i = 0; i < reader.FieldCount; i++)
            {
                if (string.Equals(reader.GetName(i), column, StringComparison.OrdinalIgnoreCase))
                    return i;
            }
            return -1;
        }
    }
}
