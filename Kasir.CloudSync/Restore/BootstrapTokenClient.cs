using System;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

#if WINDOWS || NET10_0_OR_GREATER
// Microsoft.Win32 registry access is only available on Windows. We gate
// the registry path with RuntimeInformation.IsOSPlatform at call time.
#endif

namespace Kasir.CloudSync.Restore
{
    // HTTP client for /functions/v1/register-pair.
    //
    // Exchanges a 6-digit pair code for a one-shot bootstrap JWT. The JWT is
    // scoped snapshot:read for 15 min; consumed by CloudSnapshotRestorer to
    // pull the snapshot.
    //
    // Device fingerprint is derived from:
    //   - Windows MachineGuid (HKLM\SOFTWARE\Microsoft\Cryptography\MachineGuid)
    //   - First non-loopback MAC address
    // SHA-256 of the concatenation. Stable across reboots; not used for auth
    // (audit/rate-limit only).
    //
    // PRD story: US-P3-2
    public class BootstrapTokenClient
    {
        public class PairResult
        {
            public string Jwt;
            public string RegisterId;
            public long? SnapshotAgeSeconds;
            public bool SnapshotAvailable;
        }

        public class PairException : Exception
        {
            public HttpStatusCode StatusCode { get; }
            // Our function's "error" value, or the Supabase gateway's "code"
            // (e.g. UNAUTHORIZED_NO_AUTH_HEADER). "http_<status>" when the body has neither.
            public string ErrorCode { get; }
            public string ServerMessage { get; }
            public PairException(HttpStatusCode status, string errorCode, string message)
                : this(status, errorCode, message, null, null)
            { }
            public PairException(HttpStatusCode status, string errorCode, string message,
                string serverMessage, Exception inner)
                : base(message, inner)
            {
                StatusCode = status;
                ErrorCode = errorCode;
                ServerMessage = serverMessage ?? "";
            }
        }

        // One HTTP call (or network failure) during pairing, for diagnostics logging.
        // Never contains the pair code; Body is only set for non-2xx responses.
        public class PairAttempt
        {
            public int Attempt;
            public string Endpoint;
            public int? HttpStatus;
            public string Body;
            public Exception Exception;
            public string DeviceFingerprint;
        }

        public Action<PairAttempt> OnAttempt { get; set; }

        public string Endpoint => _endpoint.ToString();
        public string DeviceFingerprint => _fingerprint;

        private readonly HttpClient _http;
        private readonly Uri _endpoint;
        private readonly string _fingerprint;

        // Convenience constructor: build HttpClient with sensible defaults.
        public BootstrapTokenClient(string supabaseUrl)
            : this(supabaseUrl, NewDefaultHttpClient(), ComputeDeviceFingerprint())
        { }

        // Test-friendly constructor.
        public BootstrapTokenClient(string supabaseUrl, HttpClient http, string fingerprint)
        {
            if (string.IsNullOrWhiteSpace(supabaseUrl))
                throw new ArgumentException("supabaseUrl required", nameof(supabaseUrl));
            if (string.IsNullOrWhiteSpace(fingerprint))
                throw new ArgumentException("fingerprint required", nameof(fingerprint));

            _http = http ?? throw new ArgumentNullException(nameof(http));
            _endpoint = new Uri(new Uri(supabaseUrl.TrimEnd('/') + "/"), "functions/v1/register-pair");
            _fingerprint = fingerprint;
        }

        private static HttpClient NewDefaultHttpClient()
        {
            return new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        }

        public async Task<PairResult> PairAsync(string code, CancellationToken ct)
        {
            if (string.IsNullOrEmpty(code))
                throw new ArgumentException("code required", nameof(code));

            // Retry transient (5xx, network) up to 3 times with backoff.
            int attempt = 0;
            Exception lastErr = null;
            HttpResponseMessage lastResponse = null;
            string lastBody = null;
            while (attempt < 3)
            {
                attempt++;
                try
                {
                    var payload = new { code, device_fingerprint = _fingerprint };
                    using var req = new HttpRequestMessage(HttpMethod.Post, _endpoint);
                    req.Content = JsonContent.Create(payload);
                    var resp = await _http.SendAsync(req, ct).ConfigureAwait(false);
                    if ((int)resp.StatusCode >= 200 && (int)resp.StatusCode < 300)
                    {
                        Report(attempt, (int)resp.StatusCode, null, null);
                        var result = await resp.Content
                            .ReadFromJsonAsync<PairResponseDto>(cancellationToken: ct)
                            .ConfigureAwait(false);
                        return new PairResult
                        {
                            Jwt = result.jwt,
                            RegisterId = result.register_id,
                            SnapshotAgeSeconds = result.snapshot_age_seconds,
                            SnapshotAvailable = result.snapshot_available,
                        };
                    }
                    var body = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
                    Report(attempt, (int)resp.StatusCode, body, null);
                    if ((int)resp.StatusCode >= 500)
                    {
                        lastResponse = resp;
                        lastBody = body;
                        await DelayBackoffAsync(attempt, ct).ConfigureAwait(false);
                        continue;
                    }
                    // 4xx: don't retry. Throw with the parsed server error.
                    var err = ServerError.Parse(body);
                    throw new PairException(resp.StatusCode, ErrorCodeOrStatus(err, resp.StatusCode), body,
                        err.Message, null);
                }
                catch (HttpRequestException ex)
                {
                    Report(attempt, null, null, ex);
                    lastErr = ex;
                    await DelayBackoffAsync(attempt, ct).ConfigureAwait(false);
                }
                catch (TaskCanceledException ex)
                {
                    Report(attempt, null, null, ex);
                    throw; // honor cancellation / timeout
                }
            }
            var lastErrBody = ServerError.Parse(lastBody);
            throw new PairException(
                lastResponse?.StatusCode ?? HttpStatusCode.ServiceUnavailable,
                "retries_exhausted",
                "register-pair failed after retries: " + (lastErr?.Message ?? "5xx " + lastBody),
                lastErr?.Message ?? lastErrBody.Message,
                lastErr);
        }

        private void Report(int attempt, int? status, string body, Exception ex)
        {
            try
            {
                OnAttempt?.Invoke(new PairAttempt
                {
                    Attempt = attempt,
                    Endpoint = Endpoint,
                    HttpStatus = status,
                    Body = body,
                    Exception = ex,
                    DeviceFingerprint = _fingerprint,
                });
            }
            catch
            {
                // Diagnostics hook must never break pairing.
            }
        }

        internal static string ErrorCodeOrStatus(ServerError err, HttpStatusCode status)
        {
            return string.IsNullOrEmpty(err.Code) ? "http_" + (int)status : err.Code;
        }

        private static Task DelayBackoffAsync(int attempt, CancellationToken ct)
        {
            // 250ms, 1s, 4s
            int ms = (int)Math.Pow(4, attempt - 1) * 250;
            return Task.Delay(ms, ct);
        }

        // ─────────────────────────────────────────────────────────────
        // Device fingerprint computation
        // ─────────────────────────────────────────────────────────────

        public static string ComputeDeviceFingerprint()
        {
            string machineGuid = ReadMachineGuidOrFallback();
            string mac = FirstNonLoopbackMac();
            var raw = machineGuid + "|" + mac;
            using var sha = SHA256.Create();
            var hash = sha.ComputeHash(Encoding.UTF8.GetBytes(raw));
            var sb = new StringBuilder(hash.Length * 2);
            foreach (var b in hash) sb.Append(b.ToString("x2"));
            return sb.ToString();
        }

        private static string ReadMachineGuidOrFallback()
        {
            // Override hatch for tests / non-Windows
            var env = Environment.GetEnvironmentVariable("KASIR_DEVICE_OVERRIDE");
            if (!string.IsNullOrEmpty(env)) return env;

            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                try
                {
                    using var key = Microsoft.Win32.Registry.LocalMachine
                        .OpenSubKey(@"SOFTWARE\Microsoft\Cryptography");
                    var v = key?.GetValue("MachineGuid") as string;
                    if (!string.IsNullOrEmpty(v)) return v;
                }
                catch
                {
                    // Fall through to fallback
                }
            }
            return "no-machine-guid-" + Environment.MachineName;
        }

        private static string FirstNonLoopbackMac()
        {
            try
            {
                foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
                {
                    if (nic.OperationalStatus != OperationalStatus.Up) continue;
                    if (nic.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;
                    var addr = nic.GetPhysicalAddress().ToString();
                    if (!string.IsNullOrEmpty(addr)) return addr;
                }
            }
            catch
            {
                // Fall through
            }
            return "no-mac";
        }

        private class PairResponseDto
        {
            public string jwt { get; set; }
            public string register_id { get; set; }
            public long? snapshot_age_seconds { get; set; }
            public bool snapshot_available { get; set; }
        }
    }
}
