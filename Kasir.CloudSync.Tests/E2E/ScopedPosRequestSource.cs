using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Kasir.CloudSync.Pull;

namespace Kasir.CloudSync.Tests.E2E
{
    // The real PostgresPosRequestSource (same SQL, same applied_at mark), limited to
    // the rows one cross-repo test run created, so a local stack that holds other
    // pending requests (left by Playwright runs) is neither read nor marked.
    // FailMarks simulates the link dropping after the hub applied a request locally.
    public sealed class ScopedPosRequestSource : IPosRequestSource
    {
        private readonly PostgresPosRequestSource _inner;
        private readonly Func<PosStockRequest, bool> _inScope;

        public ScopedPosRequestSource(string connectionString, Func<PosStockRequest, bool> inScope)
        {
            _inner = new PostgresPosRequestSource(connectionString);
            _inScope = inScope ?? throw new ArgumentNullException(nameof(inScope));
        }

        public int FailMarks { get; set; }
        public int Marks { get; private set; }
        public int FailedMarks { get; private set; }

        public async Task<IReadOnlyList<PosStockRequest>> FetchPendingAsync(int limit, CancellationToken ct)
        {
            var all = await _inner.FetchPendingAsync(limit, ct).ConfigureAwait(false);
            return all.Where(_inScope).ToList();
        }

        public Task<bool> MarkAppliedAsync(Guid id, string registerId, DateTimeOffset appliedAt, CancellationToken ct)
        {
            if (FailMarks > 0)
            {
                FailMarks--;
                throw new TimeoutException("simulated: connection to Supabase lost after the local apply");
            }
            Marks++;
            return _inner.MarkAppliedAsync(id, registerId, appliedAt, ct);
        }

        public Task<bool> MarkFailedAsync(Guid id, string registerId, DateTimeOffset failedAt, string reason, CancellationToken ct)
        {
            FailedMarks++;
            return _inner.MarkFailedAsync(id, registerId, failedAt, reason, ct);
        }
    }
}
