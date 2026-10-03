using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Kasir.Utils;

namespace Kasir.Services
{
    public sealed class GitHubRelease
    {
        public string TagName { get; set; }
        /// <summary>Tag without the leading "v" (e.g. "2.8.1").</summary>
        public string Version { get; set; }
        public string Body { get; set; }
        public string HtmlUrl { get; set; }
        public List<GitHubReleaseAsset> Assets { get; set; } = new List<GitHubReleaseAsset>();
    }

    public sealed class GitHubReleaseAsset
    {
        public string Name { get; set; }
        public string DownloadUrl { get; set; }
        public long Size { get; set; }
    }

    /// <summary>User-facing (Indonesian) failure while talking to GitHub.</summary>
    public sealed class UpdateSourceException : Exception
    {
        public UpdateSourceException(string message, Exception inner = null) : base(message, inner) { }
    }

    /// <summary>Where updates come from. Abstracted so tests can stub the network.</summary>
    public interface IReleaseSource
    {
        Task<GitHubRelease> GetLatestAsync(CancellationToken ct);

        Task DownloadAsync(string url, string destinationPath, IProgress<int> percent, CancellationToken ct);
    }

    /// <summary>
    /// Reads the latest published release of Panandika/kasir-pos from the public
    /// GitHub REST API and downloads release assets. No token is needed (public
    /// repo); unauthenticated calls are limited to 60/hour per IP, plenty for a
    /// daily check from a few registers.
    /// </summary>
    public sealed class GitHubReleaseClient : IReleaseSource
    {
        public const string Owner = "Panandika";
        public const string Repo = "kasir-pos";
        public static readonly string LatestReleaseUrl =
            "https://api.github.com/repos/" + Owner + "/" + Repo + "/releases/latest";

        private readonly HttpClient _http;

        public GitHubReleaseClient(HttpClient http = null)
        {
            _http = http ?? CreateDefaultClient();
        }

        private static HttpClient CreateDefaultClient()
        {
            // Release downloads redirect to objects.githubusercontent.com; HttpClient
            // follows HTTPS→HTTPS redirects by default. Long timeout for the ~80 MB zip;
            // the check itself is bounded by the caller's CancellationToken.
            var http = new HttpClient { Timeout = TimeSpan.FromMinutes(20) };
            http.DefaultRequestHeaders.UserAgent.ParseAdd("Kasir-POS/" + SafeVersion());
            return http;
        }

        private static string SafeVersion()
        {
            string v = AppVersion.Current ?? "0.0.0";
            int plus = v.IndexOf('+');
            return plus > 0 ? v.Substring(0, plus) : v;
        }

        public async Task<GitHubRelease> GetLatestAsync(CancellationToken ct)
        {
            using (var req = new HttpRequestMessage(HttpMethod.Get, LatestReleaseUrl))
            {
                req.Headers.Accept.ParseAdd("application/vnd.github+json");
                if (req.Headers.UserAgent.Count == 0 && _http.DefaultRequestHeaders.UserAgent.Count == 0)
                    req.Headers.UserAgent.ParseAdd("Kasir-POS/" + SafeVersion());

                HttpResponseMessage resp;
                try
                {
                    resp = await _http.SendAsync(req, ct).ConfigureAwait(false);
                }
                catch (HttpRequestException ex)
                {
                    throw new UpdateSourceException(UpdateMessages.Offline, ex);
                }
                catch (TaskCanceledException ex) when (!ct.IsCancellationRequested)
                {
                    throw new UpdateSourceException(UpdateMessages.Timeout, ex);
                }

                using (resp)
                {
                    if (resp.StatusCode == HttpStatusCode.NotFound)
                        throw new UpdateSourceException(UpdateMessages.NoRelease);
                    if (resp.StatusCode == HttpStatusCode.Forbidden || (int)resp.StatusCode == 429)
                        throw new UpdateSourceException(UpdateMessages.RateLimited);
                    if (!resp.IsSuccessStatusCode)
                        throw new UpdateSourceException(string.Format(UpdateMessages.ServerError, (int)resp.StatusCode));

                    string json = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
                    try
                    {
                        return Parse(json);
                    }
                    catch (JsonException ex)
                    {
                        throw new UpdateSourceException(UpdateMessages.BadResponse, ex);
                    }
                }
            }
        }

        /// <summary>Parses a GitHub "release" JSON object.</summary>
        public static GitHubRelease Parse(string json)
        {
            using (var doc = JsonDocument.Parse(json))
            {
                var root = doc.RootElement;
                string tag = GetString(root, "tag_name") ?? "";
                var release = new GitHubRelease
                {
                    TagName = tag,
                    Version = tag.StartsWith("v", StringComparison.OrdinalIgnoreCase) ? tag.Substring(1) : tag,
                    Body = GetString(root, "body") ?? "",
                    HtmlUrl = GetString(root, "html_url") ?? "",
                };
                if (root.TryGetProperty("assets", out var assets) && assets.ValueKind == JsonValueKind.Array)
                {
                    foreach (var a in assets.EnumerateArray())
                    {
                        release.Assets.Add(new GitHubReleaseAsset
                        {
                            Name = GetString(a, "name") ?? "",
                            DownloadUrl = GetString(a, "browser_download_url") ?? "",
                            Size = a.TryGetProperty("size", out var s) && s.ValueKind == JsonValueKind.Number ? s.GetInt64() : 0,
                        });
                    }
                }
                return release;
            }
        }

        private static string GetString(JsonElement e, string name)
        {
            return e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
        }

        public async Task DownloadAsync(string url, string destinationPath, IProgress<int> percent, CancellationToken ct)
        {
            HttpResponseMessage resp;
            try
            {
                resp = await _http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
            }
            catch (HttpRequestException ex)
            {
                throw new UpdateSourceException(UpdateMessages.DownloadFailed + " (" + ex.Message + ")", ex);
            }
            catch (TaskCanceledException ex) when (!ct.IsCancellationRequested)
            {
                throw new UpdateSourceException(UpdateMessages.Timeout, ex);
            }

            using (resp)
            {
                if (!resp.IsSuccessStatusCode)
                    throw new UpdateSourceException(UpdateMessages.DownloadFailed + " (HTTP " + (int)resp.StatusCode + ")");

                long? total = resp.Content.Headers.ContentLength;
                using (var src = await resp.Content.ReadAsStreamAsync(ct).ConfigureAwait(false))
                using (var dst = new FileStream(destinationPath, FileMode.Create, FileAccess.Write, FileShare.None, 81920, useAsync: true))
                {
                    var buffer = new byte[81920];
                    long done = 0;
                    int lastPct = -1;
                    int read;
                    try
                    {
                        while ((read = await src.ReadAsync(buffer, 0, buffer.Length, ct).ConfigureAwait(false)) > 0)
                        {
                            await dst.WriteAsync(buffer, 0, read, ct).ConfigureAwait(false);
                            done += read;
                            if (total.HasValue && total.Value > 0)
                            {
                                int pct = (int)(done * 100 / total.Value);
                                if (pct != lastPct) { lastPct = pct; percent?.Report(pct); }
                            }
                        }
                    }
                    catch (IOException ex)
                    {
                        throw new UpdateSourceException(UpdateMessages.DownloadFailed + " (" + ex.Message + ")", ex);
                    }
                    catch (HttpRequestException ex)
                    {
                        throw new UpdateSourceException(UpdateMessages.DownloadFailed + " (" + ex.Message + ")", ex);
                    }
                }
            }
        }
    }
}
