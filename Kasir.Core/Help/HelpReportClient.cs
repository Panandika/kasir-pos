using System;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Kasir.Models;

namespace Kasir.Help
{
    /// <summary>
    /// Outcome of a single Edge Function POST attempt. Drives the drainer's
    /// next action (mark sent / mark failed / retry).
    /// </summary>
    public enum SendOutcome
    {
        /// 2xx or 409 (existing ticket_no, idempotent retry-after-partial-failure).
        Sent,
        /// 4xx other than 409 — payload rejected, do not retry.
        Failed,
        /// 5xx, network error, or timeout — retry with backoff.
        TransientError
    }

    public class SendResult
    {
        public SendOutcome Outcome { get; }
        /// Short status string suitable for help_tickets.last_error.
        /// NEVER contains the raw response body (security patch #8).
        public string ShortError { get; }

        public SendResult(SendOutcome outcome, string shortError)
        {
            Outcome = outcome;
            ShortError = shortError;
        }

        public static SendResult Ok() => new SendResult(SendOutcome.Sent, null);
        public static SendResult Bad(string err) => new SendResult(SendOutcome.Failed, err);
        public static SendResult Transient(string err) => new SendResult(SendOutcome.TransientError, err);
    }

    public interface IHelpReportClient
    {
        Task<SendResult> SendAsync(HelpTicket ticket, CancellationToken ct);
    }

    /// <summary>
    /// HTTPS client for the Bantuan /help-report Edge Function.
    /// Holds anon key + machine-auth access token. Never reaches Postgres directly.
    /// </summary>
    public class HttpHelpReportClient : IHelpReportClient
    {
        private readonly HttpClient _http;
        private readonly string _endpoint;
        private readonly string _anonKey;
        private readonly Func<CancellationToken, Task<string>> _accessTokenProvider;

        public HttpHelpReportClient(
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

        public async Task<SendResult> SendAsync(HelpTicket t, CancellationToken ct)
        {
            string body = JsonConvert.SerializeObject(new
            {
                ticket_no = t.TicketNo,
                register_id = t.RegisterId,
                cashier_id = t.CashierId,
                category = t.Category,
                body = t.Body,
                attachments = JsonConvert.DeserializeObject(t.AttachmentsJson),
                client_created_at = t.ClientCreatedAt
            });

            using (var req = new HttpRequestMessage(HttpMethod.Post, _endpoint))
            {
                req.Content = new StringContent(body, Encoding.UTF8, "application/json");
                req.Headers.TryAddWithoutValidation("apikey", _anonKey);
                string token = await _accessTokenProvider(ct).ConfigureAwait(false);
                if (!string.IsNullOrEmpty(token))
                {
                    req.Headers.TryAddWithoutValidation("authorization", "Bearer " + token);
                }

                HttpResponseMessage res;
                try
                {
                    res = await _http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct)
                                     .ConfigureAwait(false);
                }
                catch (TaskCanceledException)
                {
                    return SendResult.Transient("timeout");
                }
                catch (HttpRequestException)
                {
                    return SendResult.Transient("network");
                }

                int code = (int)res.StatusCode;
                using (res)
                {
                    if (code >= 200 && code < 300) return SendResult.Ok();
                    if (code == (int)HttpStatusCode.Conflict) return SendResult.Ok(); // idempotent
                    if (code == 401 || code == 403) return SendResult.Transient(code + " auth");
                    if (code == 429) return SendResult.Transient("429 rate_limit");
                    if (code >= 400 && code < 500) return SendResult.Bad(code + " invalid");
                    return SendResult.Transient(code + " server");
                }
            }
        }
    }
}
