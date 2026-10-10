using System.Linq;
using System.Text;

namespace Kasir.CloudSync.Generation
{
    // Generates parameterised multi-row INSERT ... ON CONFLICT DO UPDATE
    // SQL for any TableMapping. Internal so tests can assert without a
    // live Postgres connection.
    public static class UpsertSqlBuilder
    {
        public static string Build(TableMapping mapping, int batchSize)
        {
            return Build(mapping, batchSize, null, null);
        }

        // Guarded variant (WP-02 WatermarkPusher). On a primary-key conflict the
        // existing row is only updated when it is the same logical row, i.e. every
        // guardColumns value matches (IS NOT DISTINCT FROM, null-safe). A different row
        // that happens to hold the same id (a legacy 32-bit row-hash id colliding with a
        // POS rowid) is left untouched. RETURNING lists the keys that were inserted or
        // updated, so the caller can tell which rows were refused.
        public static string Build(TableMapping mapping, int batchSize,
            System.Collections.Generic.IReadOnlyList<string> guardColumns, string returningColumn)
        {
            var sb = new StringBuilder();
            var columnNames = mapping.Columns.Select(c => c.Name).ToList();

            sb.Append("INSERT INTO ").Append(mapping.TableName).Append(" (");
            sb.Append(string.Join(", ", columnNames));
            sb.Append(") VALUES ");

            for (int row = 0; row < batchSize; row++)
            {
                if (row > 0) sb.Append(", ");
                sb.Append('(');
                for (int c = 0; c < columnNames.Count; c++)
                {
                    if (c > 0) sb.Append(", ");
                    sb.Append('@').Append(columnNames[c]).Append('_').Append(row);
                }
                sb.Append(')');
            }

            // ON CONFLICT clause uses the primary key columns. Composite keys
            // are handled correctly because PrimaryKeyColumns preserves order.
            var pks = mapping.PrimaryKeyColumns;
            if (pks.Count > 0)
            {
                sb.Append(" ON CONFLICT (");
                sb.Append(string.Join(", ", pks));
                sb.Append(") DO UPDATE SET ");
                bool first = true;
                foreach (var col in mapping.NonKeyColumns)
                {
                    if (!first) sb.Append(", ");
                    sb.Append(col).Append(" = EXCLUDED.").Append(col);
                    first = false;
                }
                if (guardColumns != null && guardColumns.Count > 0)
                {
                    sb.Append(" WHERE ");
                    for (int g = 0; g < guardColumns.Count; g++)
                    {
                        if (g > 0) sb.Append(" AND ");
                        sb.Append(mapping.TableName).Append('.').Append(guardColumns[g])
                          .Append(" IS NOT DISTINCT FROM EXCLUDED.").Append(guardColumns[g]);
                    }
                }
            }
            if (!string.IsNullOrEmpty(returningColumn))
                sb.Append(" RETURNING ").Append(returningColumn);
            sb.Append(';');
            return sb.ToString();
        }
    }
}
