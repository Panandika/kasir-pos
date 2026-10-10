using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Kasir.CloudSync.Generation;
using Kasir.CloudSync.Sinks;
using Kasir.Data.Repositories;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;

namespace Kasir.CloudSync.Push
{
    // WP-02: pushes stock_movements and shifts to the Supabase mirror by reading the
    // tables directly above a stored watermark (last pushed id).
    //
    // Why not OutboxRouter: neither table is in the sync_queue CHECK list and neither
    // has sync triggers (Schema.sql sync_queue / Section 7c), so their rows never reach
    // the outbox. Adding them would change the LAN sync contract, so this class
    // bypasses sync_queue entirely.
    //
    // Guarantees:
    // - Only rows above the watermark are read: WHERE id > @wm ORDER BY id LIMIT @batch.
    // - The watermark advances only after the sink accepted the batch; on a sink
    //   failure it stays, and the same rows are retried next tick.
    // - Replay is idempotent: rows are UPSERTed on the cloud primary key, so a crash
    //   between the upsert and the watermark write re-sends identical rows.
    // - stock_movements ids >= 5,000,000,000 are dashboard-originated (PullService,
    //   WP-04, reserved range OB-13) and are never pushed back (circular flow, PV-3).
    // - stock_movements rows with qty_in = qty_out = 0 carry no stock and are skipped.
    // - A stock_movements id already held in the cloud by a DIFFERENT row (a legacy
    //   32-bit row-hash id equal to a POS rowid) is not overwritten: the upsert only
    //   updates a row whose journal_no / product_code / movement_type match. Refused
    //   ids are logged and kept in config (cloud_push_conflicts_stock_movements) so
    //   they can be reconciled; the watermark still moves past them so one collision
    //   cannot stall the push forever.
    // - Shifts change after insert (closed_at, closing cash). A shift pushed while
    //   open is remembered (cloud_push_open_shifts) and re-pushed every tick until it
    //   has been pushed closed.
    public class WatermarkPusher
    {
        public const string StockMovementsTable = "stock_movements";
        public const string ShiftsTable = "shifts";

        // OB-13: PullService (WP-04) allocates dashboard-originated movement ids from here.
        public const long DashboardIdFloor = 5_000_000_000L;

        public const string StockMovementsWatermarkKey = "cloud_push_wm_stock_movements";
        public const string ShiftsWatermarkKey = "cloud_push_wm_shifts";
        public const string OpenShiftsKey = "cloud_push_open_shifts";
        public const string ConflictsKey = "cloud_push_conflicts_stock_movements";

        // Cap on remembered conflict ids so the config value cannot grow unbounded.
        internal const int MaxRememberedConflicts = 500;

        // Same logical movement: used to refuse overwriting a different cloud row.
        internal static readonly IReadOnlyList<string> MovementIdentityColumns =
            new[] { "journal_no", "product_code", "movement_type" };

        private readonly SqliteConnection _db;
        private readonly IMirrorSink _sink;
        private readonly ILogger<WatermarkPusher> _logger;
        private readonly ConfigRepository _config;

        // stock_movements.id is bigint in the cloud and legacy ids exceed int.MaxValue,
        // so the push reads and binds it as 64-bit. Only the id kind differs from the
        // registry mapping (kept unchanged: it feeds the schema hash and snapshots).
        private static readonly TableMapping StockMovementsPushMapping = WithBigintId(TableMappings.StockMovements);

        public WatermarkPusher(SqliteConnection db, IMirrorSink sink, ILogger<WatermarkPusher> logger)
        {
            _db = db ?? throw new ArgumentNullException(nameof(db));
            _sink = sink ?? throw new ArgumentNullException(nameof(sink));
            _logger = logger;
            _config = new ConfigRepository(db);
        }

        public static string WatermarkKeyFor(string tableName) => "cloud_push_wm_" + tableName;

        // One batch. Returns what happened; never throws for a sink failure (the
        // result is Failed and the watermark is unchanged), so the worker can carry on
        // with its other steps.
        public async Task<PushResult> PushTableAsync(string tableName, string watermarkKey, int batchSize, CancellationToken ct)
        {
            if (batchSize <= 0) throw new ArgumentOutOfRangeException(nameof(batchSize));
            switch (tableName)
            {
                case StockMovementsTable:
                    return await PushStockMovementsAsync(watermarkKey, batchSize, ct).ConfigureAwait(false);
                case ShiftsTable:
                    return await PushShiftsAsync(watermarkKey, batchSize, ct).ConfigureAwait(false);
                default:
                    throw new ArgumentException("WatermarkPusher does not handle table " + tableName, nameof(tableName));
            }
        }

        // Repeats PushTableAsync until a short batch (caught up), a failure, or
        // maxBatches. Lets a first run drain a large backlog without one huge upsert.
        public async Task<PushResult> DrainAsync(string tableName, string watermarkKey, int batchSize, int maxBatches, CancellationToken ct)
        {
            var total = new PushResult { Table = tableName, WatermarkBefore = ReadWatermark(watermarkKey) };
            total.WatermarkAfter = total.WatermarkBefore;
            for (int i = 0; i < Math.Max(1, maxBatches); i++)
            {
                if (ct.IsCancellationRequested) break;
                var r = await PushTableAsync(tableName, watermarkKey, batchSize, ct).ConfigureAwait(false);
                total.Read += r.Read;
                total.Pushed += r.Pushed;
                total.Conflicts.AddRange(r.Conflicts);
                total.WatermarkAfter = r.WatermarkAfter;
                total.Batches++;
                if (r.Failed)
                {
                    total.Failed = true;
                    total.Error = r.Error;
                    break;
                }
                if (r.NewRows < batchSize) break;
            }
            return total;
        }

        private async Task<PushResult> PushStockMovementsAsync(string watermarkKey, int batchSize, CancellationToken ct)
        {
            long wm = ReadWatermark(watermarkKey);
            var result = new PushResult { Table = StockMovementsTable, WatermarkBefore = wm, WatermarkAfter = wm };

            var rows = new List<IDictionary<string, object>>();
            var ids = new List<long>();
            long maxSeen = wm;
            using (var cmd = _db.CreateCommand())
            {
                cmd.CommandText =
                    @"SELECT * FROM stock_movements
                      WHERE id > @wm AND id < @floor
                        AND (qty_in <> 0 OR qty_out <> 0)
                      ORDER BY id ASC
                      LIMIT @batch";
                cmd.Parameters.AddWithValue("@wm", wm);
                cmd.Parameters.AddWithValue("@floor", DashboardIdFloor);
                cmd.Parameters.AddWithValue("@batch", batchSize);
                using var reader = cmd.ExecuteReader();
                int idOrdinal = reader.GetOrdinal("id");
                while (reader.Read())
                {
                    long id = reader.GetInt64(idOrdinal);
                    rows.Add(MapRow(StockMovementsPushMapping, reader));
                    ids.Add(id);
                    if (id > maxSeen) maxSeen = id;
                }
            }

            result.Read = rows.Count;
            result.NewRows = rows.Count;
            if (rows.Count == 0) return result;

            IReadOnlyCollection<long> written;
            try
            {
                written = await _sink.UpsertMatchingAsync(
                    StockMovementsPushMapping, rows, MovementIdentityColumns, "id", ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                result.Failed = true;
                result.Error = ex.Message;
                _logger?.LogError(ex,
                    "stock_movements push of {Count} rows above id {Wm} failed; watermark stays for retry",
                    rows.Count, wm);
                return result;
            }

            var writtenSet = new HashSet<long>(written);
            foreach (long id in ids)
                if (!writtenSet.Contains(id)) result.Conflicts.Add(id);
            result.Pushed = ids.Count - result.Conflicts.Count;

            if (result.Conflicts.Count > 0)
            {
                _logger?.LogError(
                    "stock_movements ids already held in the cloud by a different movement, NOT pushed: {Ids}",
                    string.Join(",", result.Conflicts));
                RememberConflicts(result.Conflicts);
            }

            WriteWatermark(watermarkKey, maxSeen);
            result.WatermarkAfter = maxSeen;
            _logger?.LogInformation("Cloud-pushed {Count} stock_movements (watermark {From} -> {To})",
                result.Pushed, wm, maxSeen);
            return result;
        }

        private async Task<PushResult> PushShiftsAsync(string watermarkKey, int batchSize, CancellationToken ct)
        {
            long wm = ReadWatermark(watermarkKey);
            var result = new PushResult { Table = ShiftsTable, WatermarkBefore = wm, WatermarkAfter = wm };
            var mapping = TableMappings.Shifts;

            var byId = new SortedDictionary<long, IDictionary<string, object>>();
            long maxSeen = wm;
            int newRows = 0;
            using (var cmd = _db.CreateCommand())
            {
                cmd.CommandText = "SELECT * FROM shifts WHERE id > @wm ORDER BY id ASC LIMIT @batch";
                cmd.Parameters.AddWithValue("@wm", wm);
                cmd.Parameters.AddWithValue("@batch", batchSize);
                using var reader = cmd.ExecuteReader();
                int idOrdinal = reader.GetOrdinal("id");
                while (reader.Read())
                {
                    long id = reader.GetInt64(idOrdinal);
                    byId[id] = MapRow(mapping, reader);
                    newRows++;
                    if (id > maxSeen) maxSeen = id;
                }
            }

            // Shifts already pushed while open: send their current state again.
            var pendingOpen = ReadIdSet(OpenShiftsKey);
            var stillPresent = new HashSet<long>();
            if (pendingOpen.Count > 0)
            {
                using var cmd = _db.CreateCommand();
                var names = new List<string>();
                int i = 0;
                foreach (long id in pendingOpen)
                {
                    string n = "@o" + i++;
                    names.Add(n);
                    cmd.Parameters.AddWithValue(n, id);
                }
                cmd.CommandText = "SELECT * FROM shifts WHERE id IN (" + string.Join(",", names) + ")";
                using var reader = cmd.ExecuteReader();
                int idOrdinal = reader.GetOrdinal("id");
                while (reader.Read())
                {
                    long id = reader.GetInt64(idOrdinal);
                    stillPresent.Add(id);
                    if (!byId.ContainsKey(id)) byId[id] = MapRow(mapping, reader);
                }
            }

            result.Read = byId.Count;
            result.NewRows = newRows;
            if (byId.Count == 0)
            {
                if (pendingOpen.Count > 0) WriteIdSet(OpenShiftsKey, new SortedSet<long>()); // all gone locally
                return result;
            }

            try
            {
                await _sink.UpsertAsync(mapping, byId.Values.ToList(), ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                result.Failed = true;
                result.Error = ex.Message;
                _logger?.LogError(ex, "shifts push of {Count} rows failed; watermark stays for retry", byId.Count);
                return result;
            }

            var nextOpen = new SortedSet<long>();
            foreach (var kv in byId)
            {
                var status = kv.Value.TryGetValue("status", out var s) ? s as string : null;
                if (!string.Equals(status, "C", StringComparison.Ordinal)) nextOpen.Add(kv.Key);
            }
            WriteIdSet(OpenShiftsKey, nextOpen);
            WriteWatermark(watermarkKey, maxSeen);

            result.Pushed = byId.Count;
            result.WatermarkAfter = maxSeen;
            _logger?.LogInformation("Cloud-pushed {Count} shifts ({Open} still open; watermark {From} -> {To})",
                byId.Count, nextOpen.Count, wm, maxSeen);
            return result;
        }

        private IDictionary<string, object> MapRow(TableMapping mapping, SqliteDataReader reader)
        {
            var row = RowMapper.FromReader(mapping, reader, out var warnings);
            foreach (var w in warnings) _logger?.LogWarning("{Warning}", w);
            return row;
        }

        internal long ReadWatermark(string key)
        {
            var raw = _config.Get(key);
            return long.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) && v > 0 ? v : 0;
        }

        private void WriteWatermark(string key, long value)
        {
            _config.Set(key, value.ToString(CultureInfo.InvariantCulture));
        }

        private SortedSet<long> ReadIdSet(string key)
        {
            var set = new SortedSet<long>();
            var raw = _config.Get(key);
            if (string.IsNullOrWhiteSpace(raw)) return set;
            foreach (var part in raw.Split(','))
                if (long.TryParse(part.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var id))
                    set.Add(id);
            return set;
        }

        private void WriteIdSet(string key, IEnumerable<long> ids)
        {
            _config.Set(key, string.Join(",", ids.Select(i => i.ToString(CultureInfo.InvariantCulture))));
        }

        private void RememberConflicts(IEnumerable<long> ids)
        {
            var all = ReadIdSet(ConflictsKey);
            foreach (var id in ids) all.Add(id);
            // Keep the newest (highest) ids if over the cap.
            var kept = all.Count > MaxRememberedConflicts ? all.Skip(all.Count - MaxRememberedConflicts) : all;
            WriteIdSet(ConflictsKey, kept.ToList());
        }

        private static TableMapping WithBigintId(TableMapping source)
        {
            var cols = source.Columns
                .Select(c => c.Name == "id"
                    ? new ColumnMapping("id", ColumnKind.BigintQty, isPrimaryKey: true)
                    : c)
                .ToList();
            return new TableMapping(source.TableName, cols, source.RestoreToRegister);
        }
    }

    public class PushResult
    {
        public string Table { get; set; }
        public int Batches { get; set; }
        // Rows read from SQLite (shifts: new rows plus re-sent open shifts).
        public int Read { get; set; }
        // Rows above the watermark in this batch (drives "caught up" detection).
        public int NewRows { get; set; }
        public int Pushed { get; set; }
        public List<long> Conflicts { get; } = new List<long>();
        public long WatermarkBefore { get; set; }
        public long WatermarkAfter { get; set; }
        public bool Failed { get; set; }
        public string Error { get; set; }
    }
}
