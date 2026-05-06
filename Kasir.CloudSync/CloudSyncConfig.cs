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
    }
}
