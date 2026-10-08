using System;
using System.Threading;
using System.Threading.Tasks;
using Kasir.CloudSync.Outbox;
using Kasir.CloudSync.Pull;
using Kasir.CloudSync.Push;
using Kasir.CloudSync.Sinks;
using Kasir.Data.Repositories;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Kasir.CloudSync
{
    // Kasir.CloudSync entry point. The hosted worker pushes POS data to the
    // Supabase mirror every tick (WP-02): OutboxRouter for sync_queue tables in the
    // push scope (sales), then WatermarkPusher for stock_movements and shifts, then
    // the pull step (WP-04; a no-op until it lands).
    public static class Program
    {
        public static async Task<int> Main(string[] args)
        {
            // --initial-load runs the Phase C bulk loader and exits.
            // The hosted-service run loop is for steady-state operation.
            for (int i = 0; i < args.Length; i++)
            {
                if (args[i] == "--initial-load")
                {
                    return await RunInitialLoadAsync(args).ConfigureAwait(false);
                }
                if (args[i] == "--build-snapshot")
                {
                    return await RunBuildSnapshotAsync(args).ConfigureAwait(false);
                }
            }

            using var host = Host.CreateDefaultBuilder(args)
                .ConfigureAppConfiguration((ctx, cfg) =>
                {
                    cfg.AddJsonFile("appsettings.json", optional: true, reloadOnChange: false);
                    cfg.AddEnvironmentVariables(prefix: "KASIR_CLOUDSYNC_");
                    cfg.AddCommandLine(args);

                    // Fallback: read the in-app creds written by
                    // CloudSyncSetupView (LocalAppData/Kasir/cloudsync.dat, encrypted).
                    // Lowest priority — only fills CloudSync:SupabaseConnectionString
                    // if no other source provided one.
                    var credsConn = TryBuildConnFromCredsJson();
                    if (!string.IsNullOrWhiteSpace(credsConn))
                    {
                        var built = cfg.Build();
                        if (string.IsNullOrWhiteSpace(built["CloudSync:SupabaseConnectionString"]))
                        {
                            cfg.AddInMemoryCollection(new System.Collections.Generic.Dictionary<string, string>
                            {
                                ["CloudSync:SupabaseConnectionString"] = credsConn,
                            });
                        }
                    }
                })
                .ConfigureServices((ctx, services) =>
                {
                    services.Configure<CloudSyncConfig>(
                        ctx.Configuration.GetSection(CloudSyncConfig.SectionName));
                    AddWorkerServices(services);
                })
                .ConfigureLogging(logging =>
                {
                    logging.AddSimpleConsole(opts =>
                    {
                        opts.SingleLine = true;
                        opts.TimestampFormat = "HH:mm:ss ";
                    });
                })
                .Build();

            // Fail fast with a clear message instead of a DI exception at first tick.
            var cfgValue = host.Services.GetRequiredService<IOptions<CloudSyncConfig>>().Value;
            var configError = ValidateWorkerConfig(cfgValue);
            if (configError != null)
            {
                await Console.Error.WriteLineAsync("CloudSync worker not started: " + configError).ConfigureAwait(false);
                return 78; // EX_CONFIG
            }

            try
            {
                await host.RunAsync().ConfigureAwait(false);
                return 0;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"FATAL: {ex}");
                return 1;
            }
        }

        // DI graph for the steady-state worker (WP-02 task 0). Singletons: one SQLite
        // connection to the hub's kasir.db shared by every step (the worker ticks
        // sequentially), one Npgsql-backed sink.
        internal static void AddWorkerServices(IServiceCollection services)
        {
            services.AddSingleton(sp =>
                OpenKasirDb(sp.GetRequiredService<IOptions<CloudSyncConfig>>().Value.KasirDbPath));
            services.AddSingleton(sp => new SyncQueueRepository(sp.GetRequiredService<SqliteConnection>()));
            services.AddSingleton(sp =>
                new GenericSink(sp.GetRequiredService<IOptions<CloudSyncConfig>>().Value.SupabaseConnectionString));
            services.AddSingleton<IMirrorSink>(sp => sp.GetRequiredService<GenericSink>());
            services.AddSingleton(sp => new OutboxRouter(
                sp.GetRequiredService<SqliteConnection>(),
                sp.GetRequiredService<SyncQueueRepository>(),
                sp.GetRequiredService<IMirrorSink>(),
                sp.GetRequiredService<ILogger<OutboxRouter>>(),
                sp.GetRequiredService<IOptions<CloudSyncConfig>>().Value.OutboxTableList()));
            services.AddSingleton(sp => new WatermarkPusher(
                sp.GetRequiredService<SqliteConnection>(),
                sp.GetRequiredService<IMirrorSink>(),
                sp.GetRequiredService<ILogger<WatermarkPusher>>()));
            services.AddSingleton<IPullService, NoOpPullService>();
            services.AddHostedService<CloudSyncWorker>();
        }

        // null when the worker can run; otherwise what is missing.
        internal static string ValidateWorkerConfig(CloudSyncConfig cfg)
        {
            if (cfg == null) return "missing CloudSync configuration section";
            if (string.IsNullOrWhiteSpace(cfg.SupabaseConnectionString))
                return "CloudSync:SupabaseConnectionString is not set (appsettings.json, KASIR_CLOUDSYNC_ env, or the in-app cloud setup)";
            if (string.IsNullOrWhiteSpace(cfg.KasirDbPath))
                return "CloudSync:KasirDbPath is not set (path to the hub's kasir.db)";
            if (!System.IO.File.Exists(cfg.KasirDbPath))
                return "CloudSync:KasirDbPath does not exist: " + cfg.KasirDbPath;
            if (cfg.BatchSize <= 0 || cfg.PushBatchSize <= 0 || cfg.PushMaxBatchesPerTick <= 0)
                return "CloudSync:BatchSize, PushBatchSize and PushMaxBatchesPerTick must be > 0";
            return null;
        }

        // Read-write (the watermarks and cloud_synced flags live in kasir.db) but never
        // creates the file. The POS writes the same DB concurrently (WAL), so wait on a
        // lock instead of failing the tick.
        internal static SqliteConnection OpenKasirDb(string path)
        {
            var csb = new SqliteConnectionStringBuilder
            {
                DataSource = path,
                Mode = SqliteOpenMode.ReadWrite,
                DefaultTimeout = 30
            };
            var conn = new SqliteConnection(csb.ToString());
            conn.Open();
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = "PRAGMA busy_timeout = 5000;";
                cmd.ExecuteNonQuery();
            }
            return conn;
        }

        internal static async Task<int> RunInitialLoadAsync(string[] args)
        {
        var cfgBuilder = new ConfigurationBuilder()
            .AddJsonFile("appsettings.json", optional: true, reloadOnChange: false)
            .AddEnvironmentVariables(prefix: "KASIR_CLOUDSYNC_")
            .AddCommandLine(args);
        var configuration = cfgBuilder.Build();
        var conn = configuration["CloudSync:SupabaseConnectionString"]
                   ?? Environment.GetEnvironmentVariable("KASIR_CLOUDSYNC_SUPABASE");
        if (string.IsNullOrWhiteSpace(conn))
        {
            var fromCreds = TryBuildConnFromCredsJson();
            if (!string.IsNullOrWhiteSpace(fromCreds)) conn = fromCreds;
        }
        var dbPath = configuration["CloudSync:KasirDbPath"]
                   ?? Environment.GetEnvironmentVariable("KASIR_CLOUDSYNC_DBPATH");
        if (string.IsNullOrWhiteSpace(conn) || string.IsNullOrWhiteSpace(dbPath))
        {
            await Console.Error.WriteLineAsync(
                "--initial-load requires CloudSync:SupabaseConnectionString and CloudSync:KasirDbPath (or KASIR_CLOUDSYNC_SUPABASE / KASIR_CLOUDSYNC_DBPATH env vars)");
            return 64; // EX_USAGE
        }

        using var loggerFactory = LoggerFactory.Create(b =>
            b.AddSimpleConsole(o => { o.SingleLine = true; o.TimestampFormat = "HH:mm:ss "; }));
        var log = loggerFactory.CreateLogger<Loader.InitialLoader>();

        await using var sqlite = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={dbPath};Mode=ReadOnly");
        await sqlite.OpenAsync().ConfigureAwait(false);

            var loader = new Loader.InitialLoader(sqlite, conn, log)
            {
                SkipOrphans = HasFlag(args, "--skip-orphans"),
                SkipConstraints = HasFlag(args, "--skip-constraints")
            };
            var result = await loader.RunAsync(CancellationToken.None).ConfigureAwait(false);
            return result.Mismatches == 0 ? 0 : 1;
        }

        // --build-snapshot: build a register snapshot from the cloud mirror and (with
        // --upload / --process-pending) publish it to Storage + snapshot_metadata.
        // Used by .github/workflows/snapshot-fallback.yml when no in-store hub exists.
        internal static async Task<int> RunBuildSnapshotAsync(string[] args)
        {
            var opts = Snapshot.SnapshotPublisher.ParseArgs(args, Environment.GetEnvironmentVariable, out var error);
            if (opts == null)
            {
                await Console.Error.WriteLineAsync("--build-snapshot: " + error).ConfigureAwait(false);
                await Console.Error.WriteLineAsync(
                    "usage: --build-snapshot --connection-string <pg> [--output <path>] " +
                    "[--upload --supabase-url <url> --service-role-key <key>] " +
                    "[--process-pending | --request-id <uuid>] [--trigger manual|auto_stale|gha_fallback] " +
                    "[--max-upload-mb <n> (default 50, 0 = no limit)] " +
                    "[--no-compress] [--brotli-quality <0-11> (default 9)]")
                    .ConfigureAwait(false);
                return 64; // EX_USAGE
            }

            void Log(string m) => Console.WriteLine($"{DateTime.UtcNow:HH:mm:ss} [snapshot] {m}");
            try
            {
                return await Snapshot.SnapshotPublisher.RunAsync(opts, Log, CancellationToken.None).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                await Console.Error.WriteLineAsync($"{DateTime.UtcNow:HH:mm:ss} [snapshot] FATAL: {ex}").ConfigureAwait(false);
                return 1;
            }
        }

        private static bool HasFlag(string[] args, string flag)
        {
            foreach (var a in args)
                if (string.Equals(a, flag, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        // Reads the in-app creds written by CloudSyncSetupView through
        // Kasir.Security.CloudSyncCredentialStore (LocalAppData/Kasir/cloudsync.dat,
        // DPAPI CurrentUser on Windows; ~/Library/Application Support/Kasir/cloudsync.dat
        // 0600 on macOS; an old plaintext cloudsync.json is migrated on first read)
        // and builds a Postgres connection string. Returns "" if the creds are
        // absent, unreadable (e.g. worker runs as a different Windows user than the
        // POS — DPAPI CurrentUser), or have empty fields. Used as the lowest-priority
        // config source so the in-app setup screen can configure the worker without
        // touching appsettings.json.
        internal static string TryBuildConnFromCredsJson()
        {
            try
            {
                var c = Kasir.Security.CloudSyncCredentialStore.TryLoad();
                if (c == null) return "";
                var host = c.Host ?? "";
                var db = c.Database ?? "";
                var user = c.Username ?? "";
                var pwd = c.Password ?? "";
                var port = c.Port;
                if (string.IsNullOrWhiteSpace(host) || string.IsNullOrWhiteSpace(db)
                    || string.IsNullOrWhiteSpace(user) || string.IsNullOrWhiteSpace(pwd))
                    return "";
                return $"Host={host};Port={port};Database={db};Username={user};Password={pwd};SslMode=Require";
            }
            catch
            {
                return "";
            }
        }
    }

    // Steady-state worker. Each tick runs the push and pull steps in order; a
    // failing step is logged and does not stop the later ones (a Supabase hiccup on
    // sales must not hold back stock_movements). Any failure counts toward the
    // BackoffPolicy so an outage does not hammer the connection budget.
    internal sealed class CloudSyncWorker : BackgroundService
    {
        private readonly ILogger<CloudSyncWorker> _logger;
        private readonly CloudSyncConfig _cfg;
        private readonly OutboxRouter _outboxRouter;
        private readonly WatermarkPusher _watermarkPusher;
        private readonly IPullService _pullService;
        private int _consecutiveFailures;

        public CloudSyncWorker(
            ILogger<CloudSyncWorker> logger,
            IOptions<CloudSyncConfig> options,
            OutboxRouter outboxRouter,
            WatermarkPusher watermarkPusher,
            IPullService pullService)
        {
            _logger = logger;
            _cfg = options?.Value ?? new CloudSyncConfig();
            _outboxRouter = outboxRouter;
            _watermarkPusher = watermarkPusher;
            _pullService = pullService;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("CloudSync worker started (outbox tables: {Tables})",
                _outboxRouter.AllowedTables == null ? "*" : string.Join(",", _outboxRouter.AllowedTables));
            try
            {
                while (!stoppingToken.IsCancellationRequested)
                {
                    bool tickOk = await TickAsync(stoppingToken).ConfigureAwait(false);
                    if (tickOk)
                        _consecutiveFailures = 0;
                    else
                        _consecutiveFailures++;

                    var delay = BackoffPolicy.Delay(_consecutiveFailures,
                        _cfg.PollIntervalSeconds > 0 ? _cfg.PollIntervalSeconds : BackoffPolicy.DefaultBaseIntervalSeconds);
                    if (_consecutiveFailures > 0)
                        _logger.LogWarning(
                            "Tick failed ({N} consecutive); backing off {Delay}",
                            _consecutiveFailures, delay);
                    await Task.Delay(delay, stoppingToken).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException)
            {
                // Expected on shutdown
            }
            _logger.LogInformation("CloudSync worker stopped cleanly");
        }

        // One pass: outbox -> stock_movements -> shifts -> pull. True when every step
        // succeeded.
        internal async Task<bool> TickAsync(CancellationToken ct)
        {
            bool ok = true;

            try
            {
                await _outboxRouter.TickAsync(_cfg.BatchSize, ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
            catch (Exception ex)
            {
                ok = false;
                _logger.LogError(ex, "Outbox push step failed");
            }

            ok &= await PushAsync(WatermarkPusher.StockMovementsTable, WatermarkPusher.StockMovementsWatermarkKey, ct)
                .ConfigureAwait(false);
            ok &= await PushAsync(WatermarkPusher.ShiftsTable, WatermarkPusher.ShiftsWatermarkKey, ct)
                .ConfigureAwait(false);

            try
            {
                await _pullService.TickAsync(ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
            catch (Exception ex)
            {
                ok = false;
                _logger.LogError(ex, "Pull step failed");
            }

            return ok;
        }

        private async Task<bool> PushAsync(string table, string key, CancellationToken ct)
        {
            try
            {
                var r = await _watermarkPusher.DrainAsync(table, key, _cfg.PushBatchSize, _cfg.PushMaxBatchesPerTick, ct)
                    .ConfigureAwait(false);
                return !r.Failed;
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
            catch (Exception ex)
            {
                // e.g. SQLite busy beyond the timeout; the watermark is unchanged.
                _logger.LogError(ex, "{Table} push step failed", table);
                return false;
            }
        }
    }
}
