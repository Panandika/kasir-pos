using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Kasir.CloudSync.Pull
{
    // Supabase side of the pull: pending pos_stock_requests for the hub, and the
    // applied_at mark. PostgresPosRequestSource is the Npgsql implementation (the
    // worker's service-role connection, ARCH-PV2); tests use an in-memory one.
    public interface IPosRequestSource
    {
        // Rows with applied_at IS NULL, failed_at IS NULL and target_register IN ('hub','ALL'), oldest
        // first (PosRequestKinds.ApplyOrder), at most `limit`.
        Task<IReadOnlyList<PosStockRequest>> FetchPendingAsync(int limit, CancellationToken ct);

        // Sets applied_at / applied_by_register if still unset. False when the row was
        // already marked (or is gone); throws on a connection failure.
        Task<bool> MarkAppliedAsync(Guid id, string registerId, DateTimeOffset appliedAt, CancellationToken ct);

        // Sets failed_at / failed_reason / failed_by_register (dashboard 0072) on a
        // request the hub rejects for good, if still pending; a failed row is no longer
        // fetched. False when the row was already applied / failed (or is gone).
        Task<bool> MarkFailedAsync(Guid id, string registerId, DateTimeOffset failedAt, string reason, CancellationToken ct);
    }
}
