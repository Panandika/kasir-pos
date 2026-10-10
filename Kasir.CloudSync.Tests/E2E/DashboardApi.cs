using System;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Kasir.CloudSync.Tests.E2E
{
    // Calls the dashboard's Supabase RPCs the way the browser does: PostgREST
    // (/rest/v1/rpc/<fn>) with a user JWT, so grants, SECURITY DEFINER role checks
    // (assert_staff / assert_owner_mfa) and RLS all run for real.
    //
    // LOCAL STACK ONLY. The JWT is signed with the local stack's JWT secret
    // (`supabase status -o env` JWT_SECRET; the CLI default when unset). GoTrue signs
    // its own tokens with the same secret, so PostgREST cannot tell the difference.
    // Refuses any URL that is not 127.0.0.1 / localhost.
    public sealed class DashboardApi : IDisposable
    {
        // Supabase CLI default for a local stack (public, documented value).
        public const string LocalDefaultJwtSecret = "super-secret-jwt-token-with-at-least-32-characters-long";

        private readonly HttpClient _http;
        private readonly string _baseUrl;
        private readonly string _secret;
        private readonly string _anonKey;

        public DashboardApi(string baseUrl, string jwtSecret)
        {
            var uri = new Uri(baseUrl);
            if (uri.Host != "127.0.0.1" && uri.Host != "localhost")
                throw new InvalidOperationException("DashboardApi refuses non-local host " + uri.Host);
            _baseUrl = baseUrl.TrimEnd('/');
            _secret = string.IsNullOrWhiteSpace(jwtSecret) ? LocalDefaultJwtSecret : jwtSecret;
            _anonKey = Mint(new JObject { ["role"] = "anon", ["iss"] = "supabase-demo" });
            _http = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        }

        public sealed class User
        {
            public Guid Id;
            public string Email;
            public string Aal = "aal1";
        }

        public static User Owner(Guid id, string email) => new User { Id = id, Email = email, Aal = "aal2" };
        public static User Manager(Guid id, string email) => new User { Id = id, Email = email };

        // POST /rest/v1/rpc/{fn}. Returns the JSON body (scalar, object or array).
        public async Task<JToken> Rpc(User as_, string fn, object args)
        {
            var req = new HttpRequestMessage(HttpMethod.Post, _baseUrl + "/rest/v1/rpc/" + fn);
            req.Headers.TryAddWithoutValidation("apikey", _anonKey);
            req.Headers.TryAddWithoutValidation("Authorization", "Bearer " + UserToken(as_));
            req.Content = new StringContent(JsonConvert.SerializeObject(args ?? new { }), Encoding.UTF8, "application/json");
            var res = await _http.SendAsync(req).ConfigureAwait(false);
            string body = await res.Content.ReadAsStringAsync().ConfigureAwait(false);
            if (!res.IsSuccessStatusCode)
                throw new DashboardRpcException(fn, (int)res.StatusCode, body);
            return string.IsNullOrWhiteSpace(body) ? JValue.CreateNull() : JToken.Parse(body);
        }

        public string UserToken(User u)
        {
            return Mint(new JObject
            {
                ["sub"] = u.Id.ToString(),
                ["email"] = u.Email,
                ["role"] = "authenticated",
                ["aud"] = "authenticated",
                ["aal"] = u.Aal,
                ["amr"] = new JArray(new JObject { ["method"] = u.Aal == "aal2" ? "totp" : "otp", ["timestamp"] = Now() }),
                ["session_id"] = Guid.NewGuid().ToString()
            });
        }

        private string Mint(JObject claims)
        {
            long now = Now();
            claims["iat"] = now;
            claims["exp"] = now + 3600;
            string header = B64(Encoding.UTF8.GetBytes("{\"alg\":\"HS256\",\"typ\":\"JWT\"}"));
            string payload = B64(Encoding.UTF8.GetBytes(claims.ToString(Formatting.None)));
            using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(_secret));
            string sig = B64(hmac.ComputeHash(Encoding.ASCII.GetBytes(header + "." + payload)));
            return header + "." + payload + "." + sig;
        }

        private static long Now() => DateTimeOffset.UtcNow.ToUnixTimeSeconds();

        private static string B64(byte[] bytes) =>
            Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

        public void Dispose() => _http.Dispose();
    }

    public sealed class DashboardRpcException : Exception
    {
        public int Status { get; }
        public string Body { get; }

        public DashboardRpcException(string fn, int status, string body)
            : base("rpc " + fn + " -> HTTP " + status + ": " + body)
        {
            Status = status;
            Body = body;
        }
    }
}
