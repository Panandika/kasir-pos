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
            }
            sb.Append(';');
            return sb.ToString();
        }
    }
}
