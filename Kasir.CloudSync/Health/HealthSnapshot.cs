using System;
using System.Collections.Generic;

namespace Kasir.CloudSync.Health
{
    // Plain JSON-serializable shape returned by GET /health and pushed to
    // Supabase _sync_health by RemoteHealthPublisher.
    public class HealthSnapshot
    {
        public string Status { get; set; }                              // "healthy" | "degraded" | "critical"
        public long UptimeSeconds { get; set; }
        public Dictionary<string, TableHealth> Tables { get; set; }
        public long OutboxDepth { get; set; }
        public long? SupabaseDbSizeMb { get; set; }
        public List<Alert> Alerts { get; set; }
        public DateTimeOffset GeneratedAtUtc { get; set; }
    }

    public class TableHealth
    {
        public DateTimeOffset? LastSyncUtc { get; set; }
        public long? LagSeconds { get; set; }
        public long SqliteRowCount { get; set; }
        public int ErrorCount5m { get; set; }
    }

    public class Alert
    {
        public string Severity { get; set; }   // "WARNING" | "CRITICAL"
        public string Code { get; set; }
        public string Message { get; set; }
    }
}
