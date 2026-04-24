using System;
using System.Collections.Generic;
using Microsoft.Data.Sqlite;
using Kasir.CloudSync.Mappers;

namespace Kasir.CloudSync.Generation
{
    // Runtime mapper: reads a SQLite row and produces a column-name -> Postgres-
    // ready value dictionary, applying per-column type conversions defined in the
    // TableMapping. The output is fed straight to GenericSink.UpsertAsync.
    //
    // Why runtime metadata instead of Roslyn source generation: source generators
    // require build-time tooling that complicates CI and IDE integration. The
    // value of compile-time enforcement is captured instead by the schema-drift
    // CI check (US-B3), which compares Schema.sql column lists against the
    // TableMapping declarations and fails the build on drift.
    public static class RowMapper
    {
        public static IDictionary<string, object> FromReader(
            TableMapping mapping,
            SqliteDataReader reader,
            out List<string> warnings)
        {
            warnings = new List<string>();
            var result = new Dictionary<string, object>(mapping.Columns.Count);

            foreach (var col in mapping.Columns)
            {
                int ordinal = FindOrdinal(reader, col.Name);
                if (ordinal < 0 || reader.IsDBNull(ordinal))
                {
                    result[col.Name] = null;
                    continue;
                }

                switch (col.Kind)
                {
                    case ColumnKind.Text:
                        result[col.Name] = reader.GetString(ordinal);
                        break;
                    case ColumnKind.BigintMoney:
                    case ColumnKind.BigintQty:
                        result[col.Name] = reader.GetInt64(ordinal);
                        break;
                    case ColumnKind.Int:
                        result[col.Name] = reader.GetInt32(ordinal);
                        break;
                    case ColumnKind.TimestampTz:
                        var raw = reader.GetString(ordinal);
                        var parsed = DateParser.TryParseIso(raw, out bool warn);
                        if (warn)
                        {
                            warnings.Add($"{mapping.TableName}.{col.Name} unparseable: {raw}");
                            result[col.Name] = null;
                        }
                        else
                        {
                            result[col.Name] = parsed.HasValue ? (object)parsed.Value : null;
                        }
                        break;
                    default:
                        throw new InvalidOperationException(
                            $"Unhandled ColumnKind {col.Kind} for {mapping.TableName}.{col.Name}");
                }
            }
            return result;
        }

        private static int FindOrdinal(SqliteDataReader r, string column)
        {
            for (int i = 0; i < r.FieldCount; i++)
            {
                if (string.Equals(r.GetName(i), column, StringComparison.OrdinalIgnoreCase))
                    return i;
            }
            return -1;
        }
    }
}
