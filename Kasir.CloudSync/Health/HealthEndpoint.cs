using System;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;

namespace Kasir.CloudSync.Health
{
    // Tiny GET /health JSON server using System.Net.HttpListener (NOT Kestrel,
    // so we don't pull Microsoft.AspNetCore.App into the deployment). Bound
    // to localhost:5080 only — never exposed externally. For remote
    // visibility, RemoteHealthPublisher mirrors the same JSON to Supabase.
    public class HealthEndpoint : BackgroundService
    {
        private readonly HealthService _service;
        private readonly ILogger<HealthEndpoint> _logger;
        private readonly Func<long?> _supabaseSizeProvider;
        private readonly string _prefix;

        public HealthEndpoint(
            HealthService service,
            ILogger<HealthEndpoint> logger,
            Func<long?> supabaseSizeProvider = null,
            string prefix = "http://localhost:5080/")
        {
            _service = service;
            _logger = logger;
            _supabaseSizeProvider = supabaseSizeProvider ?? (() => null);
            _prefix = prefix;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            using var listener = new HttpListener();
            listener.Prefixes.Add(_prefix);
            try
            {
                listener.Start();
                _logger.LogInformation("Health endpoint listening on {Prefix}", _prefix);
            }
            catch (HttpListenerException ex)
            {
                _logger.LogError(ex,
                    "Failed to bind {Prefix}; on Windows you may need: netsh http add urlacl url={Prefix} user=$(whoami)",
                    _prefix, _prefix);
                return;
            }

            while (!stoppingToken.IsCancellationRequested)
            {
                HttpListenerContext ctx;
                try
                {
                    ctx = await listener.GetContextAsync().ConfigureAwait(false);
                }
                catch (HttpListenerException) { break; }
                catch (ObjectDisposedException) { break; }

                _ = Task.Run(() => HandleAsync(ctx), stoppingToken);
            }
            listener.Stop();
        }

        private void HandleAsync(HttpListenerContext ctx)
        {
            try
            {
                if (ctx.Request.Url?.AbsolutePath != "/health")
                {
                    ctx.Response.StatusCode = 404;
                    ctx.Response.OutputStream.Close();
                    return;
                }

                var snap = _service.Snapshot(_supabaseSizeProvider());
                var json = JsonConvert.SerializeObject(snap, Formatting.Indented);
                var bytes = Encoding.UTF8.GetBytes(json);

                ctx.Response.ContentType = "application/json; charset=utf-8";
                // 503 if any CRITICAL alerts so an external poller flags it
                ctx.Response.StatusCode = snap.Status == "critical" ? 503 : 200;
                ctx.Response.OutputStream.Write(bytes, 0, bytes.Length);
                ctx.Response.OutputStream.Close();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Health request handler threw");
                try { ctx.Response.StatusCode = 500; ctx.Response.OutputStream.Close(); }
                catch { }
            }
        }
    }
}
