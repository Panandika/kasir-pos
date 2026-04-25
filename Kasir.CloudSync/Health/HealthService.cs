using System;
using System.Collections.Generic;
using Microsoft.Data.Sqlite;
using Kasir.CloudSync.Generation;

namespace Kasir.CloudSync.Health
{
    // Computes the current HealthSnapshot. Pure data-shaping logic so it can be
    // unit tested without spinning up an HttpListener. The HTTP exposure lives
    // in HealthEndpoint.cs (separate so it can be swapped for a different
    // transport without touching the data layer).
    //
    // Alert thresholds (per plan section 6 Observability and capacity):
    //   - any table lag > 15 min  -> CRITICAL
    //   - any table lag >  5 min  -> WARNING
    //   - outbox_depth > 100,000  -> CRITICAL (also signals worker to pause)
    //   - outbox_depth >  10,000  -> WARNING
    //   - supabase_db_size_mb > 400 -> WARNING (Supabase free tier is 500MB)
    //   - supabase_db_size_mb > 475 -> CRITICAL
    //   - error rate > 10 / 5min  -> WARNING
    public class HealthService
    {
        private readonly SqliteConnection _db;
        private readonly DateTime _startedAtUtc;
        private readonly Func<DateTime> _utcNow;

        public const long LagWarningSeconds = 5 * 60;
        public const long LagCriticalSeconds = 15 * 60;
        public const long OutboxWarningDepth = 10_000;
        public const long OutboxCriticalDepth = 100_000;
        public const long SupabaseDbWarningMb = 400;
        public const long SupabaseDbCriticalMb = 475;
        public const int ErrorRateWarningPer5m = 10;

        public HealthService(SqliteConnection db, Func<DateTime> utcNow = null)
        {
            _db = db;
            _utcNow = utcNow ?? (() => DateTime.UtcNow);
            _startedAtUtc = _utcNow();
        }

        public HealthSnapshot Snapshot(long? supabaseDbSizeMb = null)
        {
            var now = _utcNow();
            var snap = new HealthSnapshot
            {
                GeneratedAtUtc = new DateTimeOffset(now, TimeSpan.Zero),
                UptimeSeconds = (long)(now - _startedAtUtc).TotalSeconds,
                Tables = new Dictionary<string, TableHealth>(),
                Alerts = new List<Alert>(),
                SupabaseDbSizeMb = supabaseDbSizeMb
            };

            // outbox_depth across all tables
            using (var cmd = _db.CreateCommand())
            {
                cmd.CommandText = "SELECT COUNT(*) FROM sync_queue WHERE cloud_synced = 0;";
                snap.OutboxDepth = Convert.ToInt64(cmd.ExecuteScalar());
            }

            foreach (var kv in TableMappings.All)
            {
                snap.Tables[kv.Key] = ComputeTableHealth(kv.Key, now);
            }

            ApplyAlertRules(snap);
            return snap;
        }

        private TableHealth ComputeTableHealth(string tableName, DateTime now)
        {
            var th = new TableHealth();

            using (var cmd = _db.CreateCommand())
            {
                cmd.CommandText = $"SELECT COUNT(*) FROM [{tableName}];";
                try { th.SqliteRowCount = Convert.ToInt64(cmd.ExecuteScalar()); }
                catch { th.SqliteRowCount = 0; }
            }

            using (var cmd = _db.CreateCommand())
            {
                cmd.CommandText = @"SELECT MAX(cloud_synced_at) FROM sync_queue
                                    WHERE table_name = @t AND cloud_synced = 1;";
                var p = cmd.CreateParameter();
                p.ParameterName = "@t";
                p.Value = tableName;
                cmd.Parameters.Add(p);
                var raw = cmd.ExecuteScalar();
                if (raw != null && raw != DBNull.Value)
                {
                    if (DateTime.TryParse(raw.ToString(), out var ts))
                    {
                        th.LastSyncUtc = new DateTimeOffset(DateTime.SpecifyKind(ts, DateTimeKind.Utc), TimeSpan.Zero);
                        th.LagSeconds = (long)(now - ts).TotalSeconds;
                    }
                }
            }

            // Error count from sync_log within last 5 minutes (best-effort;
            // table may or may not exist in test DBs)
            try
            {
                using var cmd = _db.CreateCommand();
                cmd.CommandText = @"SELECT COUNT(*) FROM sync_log
                                    WHERE table_name = @t AND status = 'error'
                                      AND created_at > datetime(@cutoff);";
                var pt = cmd.CreateParameter(); pt.ParameterName = "@t"; pt.Value = tableName;
                cmd.Parameters.Add(pt);
                var pc = cmd.CreateParameter();
                pc.ParameterName = "@cutoff";
                pc.Value = now.AddMinutes(-5).ToString("yyyy-MM-dd HH:mm:ss");
                cmd.Parameters.Add(pc);
                th.ErrorCount5m = Convert.ToInt32(cmd.ExecuteScalar() ?? 0);
            }
            catch
            {
                th.ErrorCount5m = 0;
            }

            return th;
        }

        public static void ApplyAlertRules(HealthSnapshot snap)
        {
            // Per-table lag
            foreach (var kv in snap.Tables)
            {
                if (kv.Value.LagSeconds is long lag)
                {
                    if (lag > LagCriticalSeconds)
                        snap.Alerts.Add(new Alert
                        {
                            Severity = "CRITICAL",
                            Code = "LAG_CRITICAL",
                            Message = $"{kv.Key} cloud lag {lag}s > {LagCriticalSeconds}s"
                        });
                    else if (lag > LagWarningSeconds)
                        snap.Alerts.Add(new Alert
                        {
                            Severity = "WARNING",
                            Code = "LAG_WARNING",
                            Message = $"{kv.Key} cloud lag {lag}s > {LagWarningSeconds}s"
                        });
                }
                if (kv.Value.ErrorCount5m > ErrorRateWarningPer5m)
                    snap.Alerts.Add(new Alert
                    {
                        Severity = "WARNING",
                        Code = "ERROR_RATE",
                        Message = $"{kv.Key} {kv.Value.ErrorCount5m} errors in last 5m"
                    });
            }

            if (snap.OutboxDepth > OutboxCriticalDepth)
                snap.Alerts.Add(new Alert { Severity = "CRITICAL", Code = "OUTBOX_FULL", Message = $"outbox depth {snap.OutboxDepth} > {OutboxCriticalDepth}; cloud sync should pause" });
            else if (snap.OutboxDepth > OutboxWarningDepth)
                snap.Alerts.Add(new Alert { Severity = "WARNING", Code = "OUTBOX_DEEP", Message = $"outbox depth {snap.OutboxDepth} > {OutboxWarningDepth}" });

            if (snap.SupabaseDbSizeMb is long mb)
            {
                if (mb > SupabaseDbCriticalMb)
                    snap.Alerts.Add(new Alert { Severity = "CRITICAL", Code = "DB_NEAR_CEILING", Message = $"Supabase DB {mb}MB > {SupabaseDbCriticalMb}MB (free-tier ceiling 500MB)" });
                else if (mb > SupabaseDbWarningMb)
                    snap.Alerts.Add(new Alert { Severity = "WARNING", Code = "DB_GROWING", Message = $"Supabase DB {mb}MB > {SupabaseDbWarningMb}MB" });
            }

            // Roll up status
            string status = "healthy";
            foreach (var a in snap.Alerts)
            {
                if (a.Severity == "CRITICAL") { status = "critical"; break; }
                if (a.Severity == "WARNING") status = "degraded";
            }
            snap.Status = status;
        }
    }
}
