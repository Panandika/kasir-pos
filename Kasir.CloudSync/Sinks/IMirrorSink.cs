using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Kasir.CloudSync.Generation;

namespace Kasir.CloudSync.Sinks
{
    // The cloud-mirror write surface used by OutboxRouter and WatermarkPusher.
    // GenericSink is the Npgsql implementation; tests substitute an in-memory one.
    public interface IMirrorSink
    {
        // Plain UPSERT on the mapping's primary key. Returns rows affected.
        Task<int> UpsertAsync(
            TableMapping mapping,
            IReadOnlyCollection<IDictionary<string, object>> rows,
            CancellationToken ct);

        // UPSERT that only overwrites an existing row when every identityColumns value
        // matches (same logical row). Returns the values of returningColumn for the rows
        // actually inserted or updated; a row missing from the result was refused
        // because a different row already holds its key.
        Task<IReadOnlyCollection<long>> UpsertMatchingAsync(
            TableMapping mapping,
            IReadOnlyCollection<IDictionary<string, object>> rows,
            IReadOnlyList<string> identityColumns,
            string returningColumn,
            CancellationToken ct);
    }
}
