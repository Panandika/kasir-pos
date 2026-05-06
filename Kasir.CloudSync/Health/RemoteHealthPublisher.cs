using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Npgsql;
using NpgsqlTypes;

namespace Kasir.CloudSync.Health
{
    // Publishes the local HealthSnapshot to Supabase _sync_health table once
    // per minute so the owner can see status remotely without VPNing into
    // the gateway. Single-row UPSERT; payload is the same JSON the local
    // /health endpoint returns.
    public class RemoteHealthPublisher : BackgroundService
    {
        private readonly HealthService _service;
        private readonly string _connectionString;
        private readonly ILogger<RemoteHealthPublisher> _logger;
        private readonly TimeSpan _interval;

        public RemoteHealthPublisher(
            HealthService service,
            string connectionString,
            ILogger<RemoteHealthPublisher> logger,
            TimeSpan? interval = null)
        {
            _service = service;
            _connectionString = connectionString;
            _logger = logger;
            _interval = interval ?? TimeSpan.FromMinutes(1);
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            // First publish establishes the row; subsequent ones UPSERT.
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    var snap = _service.Snapshot();
                    await PublishAsync(snap, stoppingToken).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Remote health publish failed (will retry)");
                }

                try { await Task.Delay(_interval, stoppingToken).ConfigureAwait(false); }
                catch (OperationCanceledException) { break; }
            }
        }

        // Internal so tests can drive a single publish without a long-running
        // background task.
        internal async Task PublishAsync(HealthSnapshot snap, CancellationToken ct)
        {
            await using var conn = new NpgsqlConnection(_connectionString);
            await conn.OpenAsync(ct).ConfigureAwait(false);
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = BuildUpsertSql();

            var p1 = cmd.CreateParameter();
            p1.ParameterName = "@id";
            p1.NpgsqlDbType = NpgsqlDbType.Text;
            p1.Value = "current";
            cmd.Parameters.Add(p1);

            var p2 = cmd.CreateParameter();
            p2.ParameterName = "@payload";
            p2.NpgsqlDbType = NpgsqlDbType.Jsonb;
            p2.Value = JsonConvert.SerializeObject(snap);
            cmd.Parameters.Add(p2);

            var p3 = cmd.CreateParameter();
            p3.ParameterName = "@updated_at";
            p3.NpgsqlDbType = NpgsqlDbType.TimestampTz;
            p3.Value = DateTimeOffset.UtcNow;
            cmd.Parameters.Add(p3);

            await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }

        internal static string BuildUpsertSql()
        {
            return
                "INSERT INTO _sync_health (id, payload, updated_at) " +
                "VALUES (@id, @payload, @updated_at) " +
                "ON CONFLICT (id) DO UPDATE SET payload = EXCLUDED.payload, " +
                "                                 updated_at = EXCLUDED.updated_at;";
        }
    }
}
