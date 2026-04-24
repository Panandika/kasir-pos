using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Kasir.CloudSync.Models;
using Kasir.CloudSync.Sinks;

namespace Kasir.CloudSync.Tests.TestHelpers
{
    // Captures upserted products so OutboxReader can be tested without Postgres.
    public class InMemoryProductSink : IProductSink
    {
        public List<Product> Upserted { get; } = new List<Product>();
        public int UpsertCallCount { get; private set; }
        public Exception ThrowOnNextUpsert { get; set; }

        public Task<int> UpsertAsync(IReadOnlyCollection<Product> products, CancellationToken ct)
        {
            UpsertCallCount++;
            if (ThrowOnNextUpsert != null)
            {
                var ex = ThrowOnNextUpsert;
                ThrowOnNextUpsert = null;
                throw ex;
            }
            Upserted.AddRange(products);
            return Task.FromResult(products.Count);
        }
    }
}
