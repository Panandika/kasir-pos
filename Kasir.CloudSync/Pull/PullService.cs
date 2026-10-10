using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Kasir.Data.Repositories;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;

namespace Kasir.CloudSync.Pull
{
    // WP-04: Supabase -> hub pull step of the CloudSync tick. Polls
    // pos_stock_requests (applied_at IS NULL, target_register 'hub' or 'ALL'), applies
    // each locally through PosRequestApplier, then marks it applied in Supabase.
    //
    // Exactly-once: the local apply and its applied_requests row commit in one SQLite
    // transaction; the Supabase mark comes after. If the mark fails (network drop),
    // the request is fetched again next tick, found in applied_requests, and only
    // re-marked - never applied twice.
    //
    // Order: created_at, then kind priority (PosRequestKinds.ApplyOrder). A request
    // that cannot be applied stays pending (logged, retried every tick) and every
    // later stock-moving request (OPNAME / PURCHASE / RETURN_OUT) for the SAME product
    // is held back for this tick, so an OPNAME never compares against on-hand that is
    // missing an earlier failed PURCHASE. NEW_PRODUCT / PRODUCT_STATUS still run (a
    // NEW_PRODUCT is what a deferred PURCHASE is waiting for). Other products carry on.
    //
    // A request the applier REJECTS for good (PosRequestApplyException.Rejected: an
    // OPNAME / PURCHASE / RETURN_OUT on a non-stock code, K1/K4) is marked failed in
    // Supabase with its reason (failed_at, dashboard 0072) and is not fetched again.
    // It wrote nothing, so later requests for the product are not held back. A failed
    // failed-mark is logged and retried next tick (the reject repeats; nothing to undo).
    //
    // A failed fetch or mark throws, so the worker counts the tick as failed and
    // backs off; a request with bad data does not (it would stall the whole pull).
    public sealed class PullService : IPullService
    {
        private readonly SqliteConnection _db;
        private readonly IPosRequestSource _source;
        private readonly PosRequestApplier _applier;
        private readonly ILogger<PullService> _logger;
        private readonly int _batchSize;
        private readonly Func<DateTimeOffset> _utcNow;

        public PullService(SqliteConnection db, IPosRequestSource source, ILogger<PullService> logger,
            int batchSize = 200, Func<DateTimeOffset> utcNow = null)
        {
            _db = db ?? throw new ArgumentNullException(nameof(db));
            _source = source ?? throw new ArgumentNullException(nameof(source));
            _applier = new PosRequestApplier(db);
            _logger = logger;
            _batchSize = batchSize > 0 ? batchSize : 200;
            _utcNow = utcNow ?? (() => DateTimeOffset.UtcNow);
        }

        public PullTickResult LastResult { get; private set; } = new PullTickResult();

        public async Task<int> TickAsync(CancellationToken ct)
        {
            var result = new PullTickResult();
            LastResult = result;

            var pending = (await _source.FetchPendingAsync(_batchSize, ct).ConfigureAwait(false))
                .OrderBy(r => r, PosRequestKinds.ApplyOrder)
                .ToList();
            result.Fetched = pending.Count;
            if (pending.Count == 0) return 0;

            string registerId = new ConfigRepository(_db).Get("register_id") ?? "01";
            var heldProducts = new HashSet<string>(StringComparer.Ordinal);

            foreach (var r in pending)
            {
                ct.ThrowIfCancellationRequested();
                string product = string.IsNullOrWhiteSpace(r.ProductCode) ? null : r.ProductCode.Trim();
                if (product != null && PosRequestKinds.MovesStock(r.RequestKind) && heldProducts.Contains(product))
                {
                    result.HeldBack.Add(r.Id);
                    _logger?.LogWarning("Pull: {Kind} {Key} held back (an earlier request for {Product} failed this tick)",
                        r.RequestKind, r.IdempotencyKey, product);
                    continue;
                }

                ApplyOutcome outcome;
                try
                {
                    outcome = _applier.Apply(r);
                }
                catch (PosRequestApplyException rex) when (rex.Rejected)
                {
                    await MarkRejectedAsync(r, rex.Message, registerId, result, ct).ConfigureAwait(false);
                    continue;
                }
                catch (Exception ex) when (!(ex is OperationCanceledException))
                {
                    result.Failed.Add(r.Id);
                    if (product != null) heldProducts.Add(product);
                    bool deferred = ex is PosRequestApplyException pe && pe.Deferred;
                    _logger?.LogError(ex, "Pull: {Kind} {Key} ({Id}) not applied ({Why}); stays pending",
                        r.RequestKind, r.IdempotencyKey, r.Id, deferred ? "waiting for a prerequisite" : "rejected");
                    continue;
                }

                if (outcome == ApplyOutcome.Applied) result.Applied.Add(r.Id);
                else result.Remarked.Add(r.Id);

                try
                {
                    await _source.MarkAppliedAsync(r.Id, registerId, _utcNow(), ct).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    // Applied locally (and recorded) but not marked: the next tick re-marks it.
                    result.MarkFailed = true;
                    _logger?.LogError(ex, "Pull: applied {Kind} {Key} locally but could not mark it in Supabase; will re-mark next tick",
                        r.RequestKind, r.IdempotencyKey);
                    throw new PullMarkException(
                        "Marking pos_stock_requests " + r.Id + " applied failed: " + ex.Message, ex);
                }
            }

            if (result.Applied.Count > 0 || result.Failed.Count > 0 || result.Rejected.Count > 0)
                _logger?.LogInformation(
                    "Pull: {Applied} applied, {Remarked} re-marked, {Failed} failed, {Rejected} rejected, {Held} held back of {Fetched}",
                    result.Applied.Count, result.Remarked.Count, result.Failed.Count, result.Rejected.Count,
                    result.HeldBack.Count, result.Fetched);
            return result.Applied.Count;
        }

        private async Task MarkRejectedAsync(PosStockRequest r, string reason, string registerId, PullTickResult result,
            CancellationToken ct)
        {
            _logger?.LogWarning("Pull: {Kind} {Key} ({Id}) rejected for good: {Reason}",
                r.RequestKind, r.IdempotencyKey, r.Id, reason);
            try
            {
                await _source.MarkFailedAsync(r.Id, registerId, _utcNow(), reason, ct).ConfigureAwait(false);
                result.Rejected.Add(r.Id);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                // Nothing was written locally; the request is fetched and rejected again next tick.
                result.Failed.Add(r.Id);
                _logger?.LogError(ex, "Pull: could not mark {Kind} {Key} failed in Supabase; stays pending",
                    r.RequestKind, r.IdempotencyKey);
            }
        }
    }

    public sealed class PullTickResult
    {
        public int Fetched { get; set; }
        public List<Guid> Applied { get; } = new List<Guid>();
        // Already applied locally by an earlier tick; only the Supabase mark was redone.
        public List<Guid> Remarked { get; } = new List<Guid>();
        public List<Guid> Failed { get; } = new List<Guid>();
        // Rejected for good and marked failed in Supabase (failed_at, dashboard 0072).
        public List<Guid> Rejected { get; } = new List<Guid>();
        public List<Guid> HeldBack { get; } = new List<Guid>();
        public bool MarkFailed { get; set; }
    }

    public sealed class PullMarkException : Exception
    {
        public PullMarkException(string message, Exception inner) : base(message, inner) { }
    }
}
