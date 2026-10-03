using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Kasir.CloudSync.Restore;
using NUnit.Framework;

namespace Kasir.CloudSync.Tests.Restore
{
    // Error parsing, operator-facing explanations and the attempt log for the
    // cloud-import (pairing + snapshot download) flow.
    [TestFixture]
    public class CloudImportDiagnosticsTests
    {
        private class StubHandler : HttpMessageHandler
        {
            private readonly Func<HttpRequestMessage, HttpResponseMessage> _respond;
            public StubHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) { _respond = respond; }
            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage req, CancellationToken ct)
                => Task.FromResult(_respond(req));
        }

        private static HttpResponseMessage Json(HttpStatusCode code, string body) =>
            new HttpResponseMessage(code)
            {
                Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json"),
            };

        private const string Fingerprint = "fingerprint-xxxxxxxxxxxxxxxxxxxxxxxxxxxxxx";

        // ---------- ServerError.Parse ----------

        [Test]
        public void Parse_function_error_body()
        {
            var e = ServerError.Parse("{\"error\":\"rate_limited\",\"detail\":\"1 / device / 24h\"}");
            e.Code.Should().Be("rate_limited");
            e.Message.Should().Be("1 / device / 24h");
        }

        [Test]
        public void Parse_supabase_gateway_body()
        {
            var e = ServerError.Parse("{\"code\":\"UNAUTHORIZED_NO_AUTH_HEADER\",\"message\":\"Missing authorization header\"}");
            e.Code.Should().Be("UNAUTHORIZED_NO_AUTH_HEADER");
            e.Message.Should().Be("Missing authorization header");
        }

        [TestCase("")]
        [TestCase(null)]
        public void Parse_empty_body(string body)
        {
            var e = ServerError.Parse(body);
            e.Code.Should().BeEmpty();
            e.Message.Should().BeEmpty();
        }

        [Test]
        public void Parse_non_json_keeps_excerpt()
        {
            var e = ServerError.Parse("<html>502 Bad Gateway</html>");
            e.Code.Should().BeEmpty();
            e.Message.Should().Contain("502 Bad Gateway");
        }

        // ---------- PairException carries gateway code instead of "unknown" ----------

        [Test]
        public async Task Pair_gateway_401_reports_gateway_code_not_unknown()
        {
            var http = new HttpClient(new StubHandler(_ => Json(HttpStatusCode.Unauthorized,
                "{\"code\":\"UNAUTHORIZED_NO_AUTH_HEADER\",\"message\":\"Missing authorization header\"}")));
            var client = new BootstrapTokenClient("http://localhost", http, Fingerprint);

            var ex = await FluentActions.Invoking(() => client.PairAsync("123456", CancellationToken.None))
                .Should().ThrowAsync<BootstrapTokenClient.PairException>();

            ex.Which.ErrorCode.Should().Be("UNAUTHORIZED_NO_AUTH_HEADER");
            ex.Which.ServerMessage.Should().Be("Missing authorization header");
        }

        [Test]
        public async Task Pair_empty_4xx_body_reports_http_status()
        {
            var http = new HttpClient(new StubHandler(_ => Json(HttpStatusCode.NotFound, "")));
            var client = new BootstrapTokenClient("http://localhost", http, Fingerprint);

            var ex = await FluentActions.Invoking(() => client.PairAsync("123456", CancellationToken.None))
                .Should().ThrowAsync<BootstrapTokenClient.PairException>();

            ex.Which.ErrorCode.Should().Be("http_404");
        }

        [Test]
        public async Task Pair_reports_each_attempt_to_hook_without_pair_code()
        {
            var attempts = new List<BootstrapTokenClient.PairAttempt>();
            var http = new HttpClient(new StubHandler(_ => Json(HttpStatusCode.Unauthorized, "{\"error\":\"code_expired\"}")));
            var client = new BootstrapTokenClient("http://localhost", http, Fingerprint) { OnAttempt = attempts.Add };

            await FluentActions.Invoking(() => client.PairAsync("482917", CancellationToken.None))
                .Should().ThrowAsync<BootstrapTokenClient.PairException>();

            attempts.Should().ContainSingle();
            attempts[0].HttpStatus.Should().Be(401);
            attempts[0].Body.Should().Contain("code_expired");
            attempts[0].Endpoint.Should().EndWith("functions/v1/register-pair");
            attempts[0].DeviceFingerprint.Should().Be(Fingerprint);
            (attempts[0].Body + attempts[0].Endpoint).Should().NotContain("482917");
        }

        // ---------- Explanations ----------

        [Test]
        public void Gateway_401_is_explained_as_server_configuration_not_wrong_code()
        {
            var e = CloudImportErrors.ForPair(401, "UNAUTHORIZED_NO_AUTH_HEADER", "Missing authorization header");
            e.Title.Should().Contain("belum dikonfigurasi");
            e.Cause.Should().Contain("register-pair");
            e.Technical.Should().Contain("HTTP 401").And.Contain("UNAUTHORIZED_NO_AUTH_HEADER");
        }

        [TestCase("code_not_found", "tidak dikenal")]
        [TestCase("code_expired", "kedaluwarsa")]
        [TestCase("code_already_used", "sudah pernah dipakai")]
        [TestCase("code_format_invalid", "Format kode")]
        [TestCase("rate_limited", "Terlalu banyak")]
        [TestCase("bypass_disabled", "bypass")]
        [TestCase("retries_exhausted", "Tidak bisa menghubungi")]
        public void Pair_codes_have_specific_explanations(string code, string expectedTitle)
        {
            var e = CloudImportErrors.ForPair(code == "code_format_invalid" ? 400 : 401, code, "");
            e.Title.Should().Contain(expectedTitle);
            e.Action.Should().NotBeNullOrEmpty();
            e.Technical.Should().Contain(code);
        }

        [Test]
        public void Pair_404_is_function_not_deployed()
        {
            CloudImportErrors.ForPair(404, "http_404", "").Cause.Should().Contain("belum di-deploy");
        }

        [Test]
        public void Unknown_pair_failure_still_includes_status_and_server_text()
        {
            var e = CloudImportErrors.ForPair(418, "teapot", "short and stout");
            e.Title.Should().Contain("tidak dikenali");
            e.Cause.Should().Be("short and stout");
            e.Technical.Should().Contain("HTTP 418").And.Contain("teapot");
        }

        [TestCase("no_snapshot_available", "Belum ada snapshot")]
        [TestCase("schema_version_unsupported", "Versi data")]
        [TestCase("unauthorized", "ditolak")]
        [TestCase("signing_failed", "gagal menyiapkan")]
        public void Restore_codes_have_specific_explanations(string code, string expectedTitle)
        {
            CloudImportErrors.ForRestore("manifest", 404, code, "").Title.Should().Contain(expectedTitle);
        }

        [Test]
        public void Restore_integrity_failure_is_explained()
        {
            var e = CloudImportErrors.ForRestore("verifying", null, "", "SHA-256 mismatch: expected a, got b");
            e.Title.Should().Contain("rusak");
            e.Technical.Should().Contain("SHA-256");
        }

        [Test]
        public void Timeout_is_explained()
        {
            CloudImportErrors.ForUnexpected(new TaskCanceledException(), isTimeout: true).Title.Should().Contain("timeout");
        }

        [Test]
        public void Explanation_text_has_all_sections()
        {
            var text = CloudImportErrors.ForPair(401, "code_expired", "").ToString();
            text.Should().Contain("Penyebab:").And.Contain("Yang harus dilakukan:").And.Contain("Detail teknis:");
        }

        // ---------- Restore exception from HTTP ----------

        [Test]
        public void RestoreException_from_http_parses_server_code()
        {
            var ex = CloudSnapshotRestorer.RestoreException.FromHttp("manifest", 404, "{\"error\":\"no_snapshot_available\"}");
            ex.HttpStatus.Should().Be(404);
            ex.ServerCode.Should().Be("no_snapshot_available");
            ex.Stage.Should().Be("manifest");
        }

        // ---------- Log ----------

        [Test]
        public void Log_appends_lines_and_redacts_secrets()
        {
            string dir = Path.Combine(Path.GetTempPath(), "kasir-ci-log-" + Guid.NewGuid().ToString("N"));
            string path = Path.Combine(dir, "logs", "cloud-import.log");
            try
            {
                var log = new CloudImportLog(path);
                log.Write("pair#1", "https://x.supabase.co/functions/v1/register-pair", 401,
                    "{\"code\":\"UNAUTHORIZED_NO_AUTH_HEADER\"}", null, Fingerprint);
                log.Write("downloading", "https://x.supabase.co/storage/v1/object/sign/snapshots/a.db?token=SECRET123", 200,
                    "{\"jwt\":\"eyJhbGciOiJIUzI1NiJ9.eyJzdWIiOiJ4In0.c2lnbmF0dXJl\"}", new HttpRequestException("boom"), Fingerprint);

                var lines = File.ReadAllLines(path);
                lines.Should().HaveCount(2);
                lines[0].Should().Contain("step=pair#1").And.Contain("http=401").And.Contain("UNAUTHORIZED_NO_AUTH_HEADER").And.Contain(Fingerprint);
                lines[1].Should().Contain("exception=HttpRequestException: boom");
                string all = File.ReadAllText(path);
                all.Should().NotContain("SECRET123").And.NotContain("eyJhbGciOiJIUzI1NiJ9");
                all.Should().Contain("[jwt]");
            }
            finally
            {
                if (Directory.Exists(dir)) Directory.Delete(dir, true);
            }
        }

        [Test]
        public void Log_truncates_large_bodies()
        {
            string dir = Path.Combine(Path.GetTempPath(), "kasir-ci-log-" + Guid.NewGuid().ToString("N"));
            string path = Path.Combine(dir, "cloud-import.log");
            try
            {
                new CloudImportLog(path).Write("pair#1", null, 500, new string('x', 5000), null, Fingerprint);
                var line = File.ReadAllText(path);
                line.Should().Contain("[truncated]");
                line.Length.Should().BeLessThan(2600);
            }
            finally
            {
                if (Directory.Exists(dir)) Directory.Delete(dir, true);
            }
        }
    }
}
