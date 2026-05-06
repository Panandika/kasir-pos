using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Kasir.CloudSync.Models;

namespace Kasir.CloudSync.Sinks
{
    // Abstraction over the destination for Product upserts. Production
    // implementation is PostgresSink (Npgsql -> Supabase). Test implementations
    // collect Products in-memory so OutboxReader can be exercised without a
    // live Postgres.
    public interface IProductSink
    {
        Task<int> UpsertAsync(IReadOnlyCollection<Product> products, CancellationToken ct);
    }
}
