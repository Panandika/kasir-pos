using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Kasir.CloudSync.Mappers;
using Kasir.CloudSync.Models;
using Kasir.CloudSync.Sinks;
using Kasir.Data.Repositories;
using SyncQueueEntry = Kasir.Models.SyncQueueEntry;

namespace Kasir.CloudSync.Outbox
{
    // Polls sync_queue for cloud-pending rows (cloud_synced=0 AND status='synced'),
    // routes them through the matching mapper + sink, and marks MarkCloudSynced
    // on success. On sink failure the queue row is left unchanged so the next
    // tick retries.
    //
    // Phase A scope: products-only. US-A2 establishes the pattern; other tables
    // register with the router in Phases B/C.
    public class OutboxReader
    {
        private readonly SqliteConnection _db;
        private readonly SyncQueueRepository _queueRepo;
        private readonly IProductSink _productSink;
        private readonly ILogger<OutboxReader> _logger;

        public OutboxReader(
            SqliteConnection db,
            SyncQueueRepository queueRepo,
            IProductSink productSink,
            ILogger<OutboxReader> logger)
        {
            _db = db;
            _queueRepo = queueRepo;
            _productSink = productSink;
            _logger = logger;
        }

        // Process up to batchSize rows. Returns the number of rows shipped to
        // the cloud (may be zero if no pending work or all rows failed).
        public async Task<int> TickAsync(int batchSize, CancellationToken ct)
        {
            var pending = _queueRepo.GetPendingCloud(batchSize);
            if (pending.Count == 0) return 0;

            // Group by table_name. Phase A only handles products; others are
            // logged and left in the queue — they'll be revisited once their
            // mapper ships in Phases B/C.
            var products = new List<Product>();
            var productEntries = new List<SyncQueueEntry>();

            foreach (var entry in pending)
            {
                if (ct.IsCancellationRequested) break;
                if (entry.TableName != "products") continue;

                // DELETE operations carry the payload from sync_queue. INSERT/UPDATE
                // fetch the current row from the data table.
                Product product = null;
                if (entry.Operation == "D")
                {
                    // Phase A: soft-delete in Postgres = status='I'. Hard-delete is
                    // deferred (see open-questions.md). For now, construct a minimal
                    // Product carrying the key + tombstone marker.
                    product = new Product
                    {
                        ProductCode = entry.RecordKey,
                        Status = "D"
                    };
                }
                else
                {
                    product = FetchProduct(entry.RecordKey);
                    if (product == null)
                    {
                        _logger.LogWarning(
                            "products row for queue id={Id} key={Key} no longer exists; skipping",
                            entry.Id, entry.RecordKey);
                        continue;
                    }
                }

                products.Add(product);
                productEntries.Add(entry);
            }

            if (products.Count == 0) return 0;

            int upserted;
            try
            {
                upserted = await _productSink.UpsertAsync(products, ct).ConfigureAwait(false);
            }
            catch (System.Exception ex)
            {
                _logger.LogError(ex,
                    "PostgresSink upsert failed for batch of {Count}; rows stay cloud_synced=0 for retry",
                    products.Count);
                return 0;
            }

            // On success mark each source sync_queue row as cloud-synced.
            foreach (var entry in productEntries)
            {
                _queueRepo.MarkCloudSynced(entry.Id);
            }

            _logger.LogInformation("Cloud-synced {Count} products", productEntries.Count);
            return productEntries.Count;
        }

        private Product FetchProduct(string productCode)
        {
            using var cmd = _db.CreateCommand();
            cmd.CommandText = "SELECT * FROM products WHERE product_code = @code;";
            var param = cmd.CreateParameter();
            param.ParameterName = "@code";
            param.Value = productCode;
            cmd.Parameters.Add(param);

            using var reader = cmd.ExecuteReader();
            if (!reader.Read()) return null;

            var product = ProductMapper.FromReader(reader, out var warnings);
            if (warnings.Count > 0)
            {
                foreach (var w in warnings) _logger.LogWarning("{Warning}", w);
            }
            return product;
        }
    }
}
