using System.Threading;
using System.Threading.Tasks;

namespace Kasir.CloudSync.Pull
{
    // Supabase -> POS pull step of the worker tick (pos_stock_requests consumer).
    // WP-04 provides the real implementation; until then the worker runs the no-op.
    public interface IPullService
    {
        // Applies pending requests; returns how many were applied.
        Task<int> TickAsync(CancellationToken ct);
    }

    public sealed class NoOpPullService : IPullService
    {
        public Task<int> TickAsync(CancellationToken ct) => Task.FromResult(0);
    }
}
