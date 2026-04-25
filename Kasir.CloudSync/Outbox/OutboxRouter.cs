using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using Kasir.CloudSync.Generation;
using Kasir.CloudSync.Sinks;
using Kasir.Data.Repositories;
using SyncQueueEntry = Kasir.Models.SyncQueueEntry;

namespace Kasir.CloudSync.Outbox
{
    // Generic outbox dispatcher. Replaces the products-only OutboxReader by
    // routing every queue row through the matching TableMapping in the
    // TableMappings registry. Rows whose table_name is not registered are
    // logged and left in the queue so a later phase's mapping can pick them up.
    public class OutboxRouter
    {
        private readonly SqliteConnection _db;
        private readonly SyncQueueRepository _queueRepo;
        private readonly GenericSink _sink;
        private readonly ILogger<OutboxRouter> _logger;

        public OutboxRouter(
            SqliteConnection db,
            SyncQueueRepository queueRepo,
            GenericSink sink,
            ILogger<OutboxRouter> logger)
        {
            _db = db;
            _queueRepo = queueRepo;
            _sink = sink;
            _logger = logger;
        }

        public async Task<int> TickAsync(int batchSize, CancellationToken ct)
        {
            var pending = _queueRepo.GetPendingCloud(batchSize);
            if (pending.Count == 0) return 0;

            // Group entries by table so we can ship each table's rows in one
            // upsert call. Also preserves the ON CONFLICT semantics — UPSERT
            // batches must share a target table.
            var byTable = new Dictionary<string, List<SyncQueueEntry>>();
            foreach (var entry in pending)
            {
                if (!byTable.TryGetValue(entry.TableName, out var list))
                {
                    list = new List<SyncQueueEntry>();
                    byTable[entry.TableName] = list;
                }
                list.Add(entry);
            }

            int totalShipped = 0;
            foreach (var kv in byTable)
            {
                if (ct.IsCancellationRequested) break;
                var mapping = TableMappings.Get(kv.Key);
                if (mapping == null)
                {
                    _logger.LogDebug(
                        "Skipping table {Table}; no TableMapping registered (will be picked up by future phase)",
                        kv.Key);
                    continue;
                }

                int shipped = await ShipTableAsync(mapping, kv.Value, ct).ConfigureAwait(false);
                totalShipped += shipped;
            }
            return totalShipped;
        }

        private async Task<int> ShipTableAsync(
            TableMapping mapping,
            List<SyncQueueEntry> entries,
            CancellationToken ct)
        {
            var rows = new List<IDictionary<string, object>>(entries.Count);
            var mapped = new List<SyncQueueEntry>(entries.Count);

            foreach (var entry in entries)
            {
                if (entry.Operation == "D")
                {
                    var tomb = new Dictionary<string, object>();
                    foreach (var pk in mapping.PrimaryKeyColumns) tomb[pk] = entry.RecordKey;
                    if (TableHasColumn(mapping, "status")) tomb["status"] = "D";
                    rows.Add(tomb);
                    mapped.Add(entry);
                    continue;
                }

                var row = FetchRow(mapping, entry.RecordKey);
                if (row == null)
                {
                    _logger.LogWarning(
                        "{Table} row for queue id={Id} key={Key} no longer exists; skipping",
                        mapping.TableName, entry.Id, entry.RecordKey);
                    continue;
                }
                rows.Add(row);
                mapped.Add(entry);
            }

            if (rows.Count == 0) return 0;

            try
            {
                await _sink.UpsertAsync(mapping, rows, ct).ConfigureAwait(false);
            }
            catch (System.Exception ex)
            {
                _logger.LogError(ex,
                    "GenericSink upsert failed for {Table} batch of {Count}; rows stay cloud_synced=0 for retry",
                    mapping.TableName, rows.Count);
                return 0;
            }

            foreach (var entry in mapped) _queueRepo.MarkCloudSynced(entry.Id);
            _logger.LogInformation("Cloud-synced {Count} rows for {Table}", mapped.Count, mapping.TableName);
            return mapped.Count;
        }

        private IDictionary<string, object> FetchRow(TableMapping mapping, string recordKey)
        {
            // record_key in sync_queue maps to whichever business key the
            // PushService uses; for table_name='products' that's product_code,
            // for 'sales' it's journal_no, etc. We use the first PK column
            // of the mapping as the lookup key, matching PushService.GetKeyColumn.
            string keyColumn = mapping.PrimaryKeyColumns.Count > 0
                ? mapping.PrimaryKeyColumns[0]
                : null;
            if (keyColumn == null) return null;

            using var cmd = _db.CreateCommand();
            cmd.CommandText = $"SELECT * FROM [{mapping.TableName}] WHERE [{keyColumn}] = @key;";
            var p = cmd.CreateParameter();
            p.ParameterName = "@key";
            p.Value = recordKey;
            cmd.Parameters.Add(p);
            using var reader = cmd.ExecuteReader();
            if (!reader.Read()) return null;

            var row = RowMapper.FromReader(mapping, reader, out var warnings);
            foreach (var w in warnings) _logger.LogWarning("{Warning}", w);
            return row;
        }

        private static bool TableHasColumn(TableMapping mapping, string column)
        {
            foreach (var c in mapping.Columns)
                if (string.Equals(c.Name, column, System.StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }
    }
}
