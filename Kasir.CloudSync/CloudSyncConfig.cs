namespace Kasir.CloudSync
{
    // Configuration for the cloud-sync background worker. Values are bound from
    // appsettings.json and/or environment variables on startup. Keep this class
    // plain-POCO with no logic — validation happens in Program.cs.
    public class CloudSyncConfig
    {
        public const string SectionName = "CloudSync";

        // Postgres connection string for Supabase. The service-role key lives in
        // the Password= segment. In production this is loaded from a DPAPI-encrypted
        // file (Windows) or an env var (dev/CI). See Program.cs.
        public string SupabaseConnectionString { get; set; }

        // How often the outbox reader polls sync_queue for new cloud_synced=0 rows.
        public int PollIntervalSeconds { get; set; } = 30;

        // Number of rows to ship per tick per table. Keep small to bound the
        // blast radius of a failed Supabase transaction.
        public int BatchSize { get; set; } = 100;

        // Cloudflare R2 bucket name for Litestream WAL replication. Litestream
        // itself is configured separately in %ProgramData%\Litestream\litestream.yml;
        // this field is kept here for the health check to report bucket size.
        public string R2Bucket { get; set; }

        // Path to the local kasir.db that the worker reads. Typically the hub
        // machine's SMB outbox-consumer DB. Required in production; tests inject
        // an in-memory SqliteConnection directly.
        public string KasirDbPath { get; set; }

        // sync_queue tables OutboxRouter ships, comma-separated (WP-02 / OB-8 push
        // scope). Default "sales": products, purchases etc. stay legacy-sync-owned in
        // the cloud until the POS is their only writer. "*" = every mapped table.
        public string OutboxTables { get; set; } = "sales";

        // WatermarkPusher (stock_movements, shifts): rows per upsert and the most
        // batches per table per tick, so a first-run backlog drains over several ticks
        // instead of one huge transaction.
        public int PushBatchSize { get; set; } = 500;
        public int PushMaxBatchesPerTick { get; set; } = 20;

        // PullService (WP-04): apply dashboard pos_stock_requests to this kasir.db.
        // Only the hub runs CloudSync (single-hub-applicant model, OB-12), so this is on
        // by default; false keeps the worker push-only. PullBatchSize = requests per tick.
        public bool PullEnabled { get; set; } = true;
        public int PullBatchSize { get; set; } = 200;

        // Store time zone (D26): registers write store wall-clock text, so CloudSync
        // reads and writes timestamps at this zone's offset. IANA id, default
        // Asia/Makassar (WITA, UTC+08:00). Applied once at startup (StoreTimeZone).
        // Keep in step with the dashboard's public.store_tz().
        public string StoreTimeZone { get; set; } = Kasir.CloudSync.StoreTimeZone.DefaultId;

        // Parsed OutboxTables; null = no restriction ("*").
        public System.Collections.Generic.IReadOnlyCollection<string> OutboxTableList()
        {
            var raw = (OutboxTables ?? "").Trim();
            if (raw == "*") return null;
            var list = new System.Collections.Generic.List<string>();
            foreach (var part in raw.Split(','))
            {
                var t = part.Trim();
                if (t.Length > 0 && !list.Contains(t)) list.Add(t);
            }
            return list;
        }
    }
}
