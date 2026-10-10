using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Kasir.CloudSync.Pull;

namespace Kasir.CloudSync.Tests.TestHelpers
{
    // Stand-in for Supabase pos_stock_requests with the same filter and order as
    // PostgresPosRequestSource. FailFetch / FailMarks simulate a dropped link.
    public sealed class InMemoryPosRequestSource : IPosRequestSource
    {
        public sealed class Row
        {
            public PosStockRequest Request;
            public DateTimeOffset? AppliedAt;
            public string AppliedBy;
            public int MarkCalls;
            public DateTimeOffset? FailedAt;
            public string FailedReason;
            public string FailedBy;
        }

        public List<Row> Rows { get; } = new List<Row>();
        public bool FailFetch { get; set; }
        public int FailMarks { get; set; }
        public int FetchCalls { get; private set; }
        public int FailFailedMarks { get; set; }

        public PosStockRequest Add(PosStockRequest r)
        {
            if (r.Id == Guid.Empty) r.Id = Guid.NewGuid();
            if (r.TargetRegister == null) r.TargetRegister = "ALL";
            if (Rows.Any(x => x.Request.RequestKind == r.RequestKind && x.Request.IdempotencyKey == r.IdempotencyKey))
                throw new InvalidOperationException("UNIQUE (request_kind, idempotency_key) violated");
            Rows.Add(new Row { Request = r });
            return r;
        }

        public Row RowOf(Guid id) => Rows.Single(x => x.Request.Id == id);

        public Task<IReadOnlyList<PosStockRequest>> FetchPendingAsync(int limit, CancellationToken ct)
        {
            FetchCalls++;
            if (FailFetch) throw new TimeoutException("connection to Supabase lost");
            IReadOnlyList<PosStockRequest> list = Rows
                .Where(x => x.AppliedAt == null && x.FailedAt == null
                            && (x.Request.TargetRegister == "hub" || x.Request.TargetRegister == "ALL"))
                .Select(x => x.Request)
                .OrderBy(x => x, PosRequestKinds.ApplyOrder)
                .Take(limit)
                .ToList();
            return Task.FromResult(list);
        }

        public Task<bool> MarkAppliedAsync(Guid id, string registerId, DateTimeOffset appliedAt, CancellationToken ct)
        {
            if (FailMarks > 0)
            {
                FailMarks--;
                throw new TimeoutException("connection to Supabase lost");
            }
            var row = Rows.SingleOrDefault(x => x.Request.Id == id);
            if (row == null) return Task.FromResult(false);
            row.MarkCalls++;
            if (row.AppliedAt != null) return Task.FromResult(false);
            row.AppliedAt = appliedAt;
            row.AppliedBy = registerId;
            return Task.FromResult(true);
        }

        public Task<bool> MarkFailedAsync(Guid id, string registerId, DateTimeOffset failedAt, string reason, CancellationToken ct)
        {
            if (FailFailedMarks > 0)
            {
                FailFailedMarks--;
                throw new TimeoutException("connection to Supabase lost");
            }
            var row = Rows.SingleOrDefault(x => x.Request.Id == id);
            if (row == null || row.AppliedAt != null || row.FailedAt != null) return Task.FromResult(false);
            row.FailedAt = failedAt;
            row.FailedReason = reason;
            row.FailedBy = registerId;
            return Task.FromResult(true);
        }
    }
}
