using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Kasir.CloudSync.Generation;
using Kasir.CloudSync.Sinks;

namespace Kasir.CloudSync.Tests.TestHelpers
{
    // Cloud mirror stand-in: one dictionary per table keyed by the mapping's primary
    // key, with the same upsert semantics as GenericSink (including the guarded
    // UpsertMatchingAsync). FailNext makes the next call throw like a dropped link.
    public sealed class InMemoryMirrorSink : IMirrorSink
    {
        public Dictionary<string, Dictionary<string, IDictionary<string, object>>> Tables { get; } =
            new Dictionary<string, Dictionary<string, IDictionary<string, object>>>();

        public int Calls { get; private set; }
        public List<int> BatchSizes { get; } = new List<int>();
        public bool FailNext { get; set; }

        public Dictionary<string, IDictionary<string, object>> Table(string name)
        {
            if (!Tables.TryGetValue(name, out var t))
            {
                t = new Dictionary<string, IDictionary<string, object>>();
                Tables[name] = t;
            }
            return t;
        }

        private static string Key(TableMapping m, IDictionary<string, object> row) =>
            string.Join("|", m.PrimaryKeyColumns.Select(c => Convert.ToString(row[c])));

        private void Enter(int count)
        {
            Calls++;
            BatchSizes.Add(count);
            if (FailNext)
            {
                FailNext = false;
                throw new InvalidOperationException("simulated Supabase outage");
            }
        }

        public Task<int> UpsertAsync(TableMapping mapping, IReadOnlyCollection<IDictionary<string, object>> rows, CancellationToken ct)
        {
            Enter(rows.Count);
            var t = Table(mapping.TableName);
            foreach (var r in rows) t[Key(mapping, r)] = new Dictionary<string, object>(r);
            return Task.FromResult(rows.Count);
        }

        public Task<IReadOnlyCollection<long>> UpsertMatchingAsync(TableMapping mapping,
            IReadOnlyCollection<IDictionary<string, object>> rows, IReadOnlyList<string> identityColumns,
            string returningColumn, CancellationToken ct)
        {
            Enter(rows.Count);
            var t = Table(mapping.TableName);
            var written = new List<long>();
            foreach (var r in rows)
            {
                string k = Key(mapping, r);
                if (t.TryGetValue(k, out var existing)
                    && identityColumns.Any(c => !Equals(existing.TryGetValue(c, out var v) ? v : null, r[c])))
                    continue; // a different row holds this key
                t[k] = new Dictionary<string, object>(r);
                written.Add(Convert.ToInt64(r[returningColumn]));
            }
            return Task.FromResult<IReadOnlyCollection<long>>(written);
        }
    }
}
