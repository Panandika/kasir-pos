#nullable enable
using System;
using System.IO;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Kasir.Help.Auth
{
    /// <summary>
    /// Singleton Supabase machine-auth provider for Bantuan registers.
    ///
    /// Contract:
    ///   - Constructor NEVER throws (missing config / corrupt auth.dat / DPAPI failure → log-and-continue).
    ///   - <see cref="GetAccessTokenAsync"/> returns "" on failure, NEVER throws.
    ///   - <see cref="SemaphoreSlim"/> serialises refresh-token rotation to prevent
    ///     concurrent refresh races between HelpSyncService and HttpHelpAskClient.
    ///   - On Windows, refresh tokens persist via DPAPI (CurrentUser) at
    ///     %APPDATA%\Kasir\auth.dat. On non-Windows, tokens live in memory only.
    ///
    /// Auth-outage budget: First TickAsync may fire before initial signInWithPassword
    /// completes. Edge Function returns 401, ticket stays queued. With
    /// MaxAttemptsBeforeFail=8 × 15s tick = ~2 min auth-bootstrap window before a
    /// ticket is permanently failed. Tickets are not lost, just delayed.
    /// </summary>
    public sealed class SupabaseMachineAuth
    {
        private static readonly Lazy<SupabaseMachineAuth> _lazy =
            new Lazy<SupabaseMachineAuth>(() => new SupabaseMachineAuth(), LazyThreadSafetyMode.ExecutionAndPublication);
        public static SupabaseMachineAuth Current => _lazy.Value;

        private readonly HelpConfig? _config;
        private readonly bool _disabled;
        private readonly SemaphoreSlim _refreshLock = new SemaphoreSlim(1, 1);
        private readonly HttpClient _http = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };

        private string _accessToken = "";
        private string _refreshToken = "";
        private DateTimeOffset _accessTokenExpiry = DateTimeOffset.MinValue;
        private bool _authFailed;
        private DateTimeOffset _nextRetryAt = DateTimeOffset.MinValue;

        private const string AuthDatFileName = "auth.dat";

        public bool IsConfigured => !_disabled && _config != null;

        private SupabaseMachineAuth()
        {
            try
            {
                _config = HelpConfigLoader.TryLoad();
                if (_config == null)
                {
                    _disabled = true;
                    return;
                }
                // Try to load persisted refresh token (Windows only). On non-Windows
                // or read failure, we silently skip — first call will sign in fresh.
                _refreshToken = TryLoadRefreshToken();
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[SupabaseMachineAuth] init failed: {ex.GetType().Name}: {ex.Message}");
                _disabled = true;
            }
        }

        /// <summary>
        /// Returns a current access token, or "" on any failure. NEVER throws.
        /// Concurrent callers serialise on a SemaphoreSlim during refresh.
        /// </summary>
        public async Task<string> GetAccessTokenAsync(CancellationToken ct)
        {
            if (_disabled || _config == null) return "";

            // Honor 15s backoff after recent auth failure.
            if (_authFailed && DateTimeOffset.UtcNow < _nextRetryAt) return "";

            // Fast path: token still valid (with 60s safety margin).
            if (!string.IsNullOrEmpty(_accessToken) && _accessTokenExpiry > DateTimeOffset.UtcNow.AddSeconds(60))
            {
                return _accessToken;
            }

            await _refreshLock.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                // Re-check after acquiring lock — another caller may have refreshed.
                if (!string.IsNullOrEmpty(_accessToken) && _accessTokenExpiry > DateTimeOffset.UtcNow.AddSeconds(60))
                {
                    return _accessToken;
                }

                // Try refresh first if we have a refresh token; fall back to password sign-in.
                if (!string.IsNullOrEmpty(_refreshToken))
                {
                    if (await TryRefreshAsync(ct).ConfigureAwait(false))
                    {
                        return _accessToken;
                    }
                }

                if (await TrySignInAsync(ct).ConfigureAwait(false))
                {
                    return _accessToken;
                }

                _authFailed = true;
                _nextRetryAt = DateTimeOffset.UtcNow.AddSeconds(15);
                return "";
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[SupabaseMachineAuth] GetAccessTokenAsync error: {ex.GetType().Name}: {ex.Message}");
                _authFailed = true;
                _nextRetryAt = DateTimeOffset.UtcNow.AddSeconds(15);
                return "";
            }
            finally
            {
                _refreshLock.Release();
            }
        }

        private async Task<bool> TrySignInAsync(CancellationToken ct)
        {
            try
            {
                var cfg = _config!;
                string url = $"{cfg.SupabaseUrl.TrimEnd('/')}/auth/v1/token?grant_type=password";
                string body = JsonSerializer.Serialize(new { email = cfg.MachineEmail, password = cfg.MachinePassword });
                using var req = new HttpRequestMessage(HttpMethod.Post, url);
                req.Headers.TryAddWithoutValidation("apikey", cfg.AnonKey);
                req.Content = new StringContent(body, Encoding.UTF8, "application/json");
                using var res = await _http.SendAsync(req, ct).ConfigureAwait(false);
                if (!res.IsSuccessStatusCode) return false;
                string raw = await res.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
                return ParseAndStoreTokens(raw);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[SupabaseMachineAuth] sign-in failed: {ex.GetType().Name}: {ex.Message}");
                return false;
            }
        }

        private async Task<bool> TryRefreshAsync(CancellationToken ct)
        {
            try
            {
                var cfg = _config!;
                string url = $"{cfg.SupabaseUrl.TrimEnd('/')}/auth/v1/token?grant_type=refresh_token";
                string body = JsonSerializer.Serialize(new { refresh_token = _refreshToken });
                using var req = new HttpRequestMessage(HttpMethod.Post, url);
                req.Headers.TryAddWithoutValidation("apikey", cfg.AnonKey);
                req.Content = new StringContent(body, Encoding.UTF8, "application/json");
                using var res = await _http.SendAsync(req, ct).ConfigureAwait(false);
                if (!res.IsSuccessStatusCode)
                {
                    // Refresh token may be revoked/expired; drop it so next call re-signs in.
                    _refreshToken = "";
                    return false;
                }
                string raw = await res.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
                return ParseAndStoreTokens(raw);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[SupabaseMachineAuth] refresh failed: {ex.GetType().Name}: {ex.Message}");
                return false;
            }
        }

        private bool ParseAndStoreTokens(string json)
        {
            try
            {
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;
                string access = root.TryGetProperty("access_token", out var a) ? (a.GetString() ?? "") : "";
                string refresh = root.TryGetProperty("refresh_token", out var r) ? (r.GetString() ?? "") : "";
                int expiresIn = root.TryGetProperty("expires_in", out var e) && e.ValueKind == JsonValueKind.Number
                    ? e.GetInt32() : 3600;

                if (string.IsNullOrEmpty(access)) return false;

                _accessToken = access;
                _accessTokenExpiry = DateTimeOffset.UtcNow.AddSeconds(expiresIn);
                if (!string.IsNullOrEmpty(refresh))
                {
                    _refreshToken = refresh;
                    TrySaveRefreshToken(refresh);
                }
                _authFailed = false;
                return true;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[SupabaseMachineAuth] token parse failed: {ex.GetType().Name}: {ex.Message}");
                return false;
            }
        }

        private static string AuthDatPath()
        {
            string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            return Path.Combine(appData, "Kasir", AuthDatFileName);
        }

        private static string TryLoadRefreshToken()
        {
            try
            {
                if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) return "";
                string path = AuthDatPath();
                if (!File.Exists(path)) return "";
                byte[] enc = File.ReadAllBytes(path);
#pragma warning disable CA1416 // platform check above
                byte[] plain = ProtectedData.Unprotect(enc, null, DataProtectionScope.CurrentUser);
#pragma warning restore CA1416
                return Encoding.UTF8.GetString(plain);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[SupabaseMachineAuth] auth.dat read failed: {ex.GetType().Name}: {ex.Message}");
                return "";
            }
        }

        private static void TrySaveRefreshToken(string refreshToken)
        {
            try
            {
                if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) return;
                string path = AuthDatPath();
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                byte[] plain = Encoding.UTF8.GetBytes(refreshToken);
#pragma warning disable CA1416
                byte[] enc = ProtectedData.Protect(plain, null, DataProtectionScope.CurrentUser);
#pragma warning restore CA1416
                File.WriteAllBytes(path, enc);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[SupabaseMachineAuth] auth.dat write failed: {ex.GetType().Name}: {ex.Message}");
            }
        }
    }
}
