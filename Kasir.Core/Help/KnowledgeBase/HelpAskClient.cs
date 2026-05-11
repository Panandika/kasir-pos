using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Kasir.Models;

namespace Kasir.Help.KnowledgeBase
{
    public interface IHelpAskClient
    {
        Task<List<HelpFaqHit>> AskAsync(string query, string registerId, CancellationToken ct);
    }

    /// <summary>
    /// HTTPS client for /help-ask Edge Function. Returns vector-search hits;
    /// caller fuses with local FTS5 hits via HybridRetriever.
    /// </summary>
    public class HttpHelpAskClient : IHelpAskClient
    {
        private readonly HttpClient _http;
        private readonly string _endpoint;
        private readonly string _anonKey;
        private readonly Func<CancellationToken, Task<string>> _accessTokenProvider;

        // PM3 mitigation: Edge Function cold-start (~800ms) + OpenAI embed (~300ms)
        // routinely exceeds 1s. 5s gives headroom while UI shows a spinner.
        public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(5);

        public HttpHelpAskClient(
            HttpClient http,
            string endpoint,
            string anonKey,
            Func<CancellationToken, Task<string>> accessTokenProvider)
        {
            _http = http;
            _endpoint = endpoint;
            _anonKey = anonKey;
            _accessTokenProvider = accessTokenProvider;
        }

        public async Task<List<HelpFaqHit>> AskAsync(string query, string registerId, CancellationToken ct)
        {
            using (var cts = CancellationTokenSource.CreateLinkedTokenSource(ct))
            {
                cts.CancelAfter(Timeout);
                string body = JsonConvert.SerializeObject(new { query, register_id = registerId });
                using (var req = new HttpRequestMessage(HttpMethod.Post, _endpoint))
                {
                    req.Content = new StringContent(body, Encoding.UTF8, "application/json");
                    req.Headers.TryAddWithoutValidation("apikey", _anonKey);
                    string token = await _accessTokenProvider(cts.Token).ConfigureAwait(false);
                    if (!string.IsNullOrEmpty(token))
                    {
                        req.Headers.TryAddWithoutValidation("authorization", "Bearer " + token);
                    }

                    HttpResponseMessage res;
                    try
                    {
                        res = await _http.SendAsync(req, HttpCompletionOption.ResponseContentRead, cts.Token)
                                         .ConfigureAwait(false);
                    }
                    catch (Exception)
                    {
                        return new List<HelpFaqHit>(); // degrade silently — caller falls back to FTS5
                    }
                    using (res)
                    {
                        if (!res.IsSuccessStatusCode) return new List<HelpFaqHit>();
                        string raw = await res.Content.ReadAsStringAsync().ConfigureAwait(false);
                        return ParseChunks(raw);
                    }
                }
            }
        }

        private static List<HelpFaqHit> ParseChunks(string raw)
        {
            var list = new List<HelpFaqHit>();
            try
            {
                var obj = Newtonsoft.Json.Linq.JObject.Parse(raw);
                var chunks = obj["chunks"] as Newtonsoft.Json.Linq.JArray;
                if (chunks == null) return list;
                foreach (var c in chunks)
                {
                    list.Add(new HelpFaqHit
                    {
                        Title = (string)c["title"],
                        Content = (string)c["content"] ?? "",
                        DocPath = (string)c["doc_path"] ?? "",
                        Anchor = (string)c["anchor"],
                        Score = c["score"] != null ? (double)c["score"] : 0.0
                    });
                }
            }
            catch
            {
                // malformed response → empty list, degrade to FTS5
            }
            return list;
        }
    }
}
