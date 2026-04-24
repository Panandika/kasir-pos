using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Kasir.CloudSync
{
    // Kasir.CloudSync entry point. Phase A scope: skeleton that starts an
    // IHost with a stub BackgroundService, logs "CloudSync worker started",
    // and exits cleanly on Ctrl+C. Real mappers, OutboxReader, and
    // PostgresSink arrive in US-A2.
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
            }

            using var host = Host.CreateDefaultBuilder(args)
                .ConfigureAppConfiguration((ctx, cfg) =>
                {
                    cfg.AddJsonFile("appsettings.json", optional: true, reloadOnChange: false);
                    cfg.AddEnvironmentVariables(prefix: "KASIR_CLOUDSYNC_");
                    cfg.AddCommandLine(args);
                })
                .ConfigureServices((ctx, services) =>
                {
                    services.Configure<CloudSyncConfig>(
                        ctx.Configuration.GetSection(CloudSyncConfig.SectionName));
                    services.AddHostedService<CloudSyncWorker>();
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

        internal static async Task<int> RunInitialLoadAsync(string[] args)
        {
        var cfgBuilder = new ConfigurationBuilder()
            .AddJsonFile("appsettings.json", optional: true, reloadOnChange: false)
            .AddEnvironmentVariables(prefix: "KASIR_CLOUDSYNC_")
            .AddCommandLine(args);
        var configuration = cfgBuilder.Build();
        var conn = configuration["CloudSync:SupabaseConnectionString"]
                   ?? Environment.GetEnvironmentVariable("KASIR_CLOUDSYNC_SUPABASE");
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

            var loader = new Loader.InitialLoader(sqlite, conn, log);
            var result = await loader.RunAsync(CancellationToken.None).ConfigureAwait(false);
            return result.Mismatches == 0 ? 0 : 1;
        }
    }

    // Stub worker. Phase A scope: prove the hosted-service lifecycle works on
    // both macOS (dev) and Windows (target). Replaced with the real outbox-
    // polling loop in US-A2.
    internal sealed class CloudSyncWorker : BackgroundService
    {
        private readonly ILogger<CloudSyncWorker> _logger;

        public CloudSyncWorker(ILogger<CloudSyncWorker> logger)
        {
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("CloudSync worker started (skeleton; Phase A US-A1)");
            try
            {
                // Idle heartbeat until cancellation. US-A2 replaces this with the
                // OutboxReader + PostgresSink dispatch loop.
                while (!stoppingToken.IsCancellationRequested)
                {
                    _logger.LogDebug("CloudSync tick (no-op in skeleton)");
                    await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException)
            {
                // Expected on shutdown
            }
            _logger.LogInformation("CloudSync worker stopped cleanly");
        }
    }
}
