using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace Kasir.CloudSync.Generation
{
    // One INSERT ... ON CONFLICT DO UPDATE may not touch the same key twice:
    // Postgres rejects the whole statement with 21000 ("ON CONFLICT DO UPDATE
    // command cannot affect row a second time"). A record inserted and then
    // updated (e.g. a sale voided) inside one cloud tick has two sync_queue rows
    // for the same key, so a batch must be collapsed to one row per primary key
    // first. Generalises PostgresSink.DedupeByProductCode to any TableMapping.
    public static class RowDedup
    {
        // Keeps the LAST row per primary key (the newest queue entry wins) and
        // preserves the order keys first appeared in. Returns the input instance
        // when it holds no duplicates.
        public static IReadOnlyCollection<IDictionary<string, object>> ByPrimaryKey(
            TableMapping mapping,
            IReadOnlyCollection<IDictionary<string, object>> rows)
        {
            if (rows == null || rows.Count < 2) return rows;
            var pk = mapping.PrimaryKeyColumns;
            if (pk.Count == 0) return rows;

            var order = new List<string>(rows.Count);
            var latest = new Dictionary<string, IDictionary<string, object>>(StringComparer.Ordinal);
            foreach (var row in rows)
            {
                string key = KeyOf(pk, row);
                if (!latest.ContainsKey(key)) order.Add(key);
                latest[key] = row;
            }
            if (order.Count == rows.Count) return rows;

            var result = new List<IDictionary<string, object>>(order.Count);
            foreach (var key in order) result.Add(latest[key]);
            return result;
        }

        private static string KeyOf(IReadOnlyList<string> pk, IDictionary<string, object> row)
        {
            var sb = new StringBuilder();
            foreach (var col in pk)
            {
                row.TryGetValue(col, out var v);
                string s = v == null || v is DBNull ? "\0null" : Convert.ToString(v, CultureInfo.InvariantCulture);
                // Length-prefix each part so ("a|b","c") and ("a","b|c") never collide.
                sb.Append(s.Length).Append(':').Append(s).Append('|');
            }
            return sb.ToString();
        }
    }
}
