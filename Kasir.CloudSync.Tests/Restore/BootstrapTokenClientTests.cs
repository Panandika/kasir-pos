using System;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Kasir.CloudSync.Restore;
using NUnit.Framework;

namespace Kasir.CloudSync.Tests.Restore
{
    // Unit tests for BootstrapTokenClient — pair-code HTTP exchange.
    // Mocks the wire via a custom HttpMessageHandler.
    //
    // PRD story: US-P3-2
    [TestFixture]
    public class BootstrapTokenClientTests
    {
        private class StubHandler : HttpMessageHandler
        {
            private readonly Func<HttpRequestMessage, HttpResponseMessage> _respond;
            public int CallCount;
            public StubHandler(Func<HttpRequestMessage, HttpResponseMessage> respond)
            {
                _respond = respond;
            }
            protected override Task<HttpResponseMessage> SendAsync(
                HttpRequestMessage req,
                CancellationToken ct)
            {
                CallCount++;
                return Task.FromResult(_respond(req));
            }
        }

        private static HttpResponseMessage Json(HttpStatusCode code, string body)
        {
            return new HttpResponseMessage(code)
            {
                Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json"),
            };
        }

        [Test]
        public async Task Pair_happy_path_returns_jwt_and_register_id()
        {
            var handler = new StubHandler(_ => Json(
                HttpStatusCode.OK,
                "{\"jwt\":\"eyJ...\",\"register_id\":\"KLR-03\",\"snapshot_age_seconds\":3600,\"snapshot_available\":true}"));
            var http = new HttpClient(handler);
            var client = new BootstrapTokenClient("http://localhost", http, "fingerprint-" + new string('x', 30));

            var result = await client.PairAsync("482917", CancellationToken.None);

            result.Jwt.Should().Be("eyJ...");
            result.RegisterId.Should().Be("KLR-03");
            result.SnapshotAgeSeconds.Should().Be(3600);
            result.SnapshotAvailable.Should().BeTrue();
            handler.CallCount.Should().Be(1);
        }

        [Test]
        public async Task Pair_4xx_throws_PairException_without_retry()
        {
            var handler = new StubHandler(_ => Json(
                HttpStatusCode.Unauthorized,
                "{\"error\":\"code_not_found\"}"));
            var http = new HttpClient(handler);
            var client = new BootstrapTokenClient("http://localhost", http, "fingerprint-" + new string('x', 30));

            await FluentActions.Invoking(() => client.PairAsync("123456", CancellationToken.None))
                .Should().ThrowAsync<BootstrapTokenClient.PairException>()
                .Where(ex => ex.StatusCode == HttpStatusCode.Unauthorized && ex.ErrorCode == "code_not_found");
            handler.CallCount.Should().Be(1);
        }

        [Test]
        public async Task Pair_5xx_retries_three_times_then_throws()
        {
            var handler = new StubHandler(_ => Json(
                HttpStatusCode.InternalServerError,
                "{\"error\":\"server_error\"}"));
            var http = new HttpClient(handler);
            var client = new BootstrapTokenClient("http://localhost", http, "fingerprint-" + new string('x', 30));

            await FluentActions.Invoking(() => client.PairAsync("123456", CancellationToken.None))
                .Should().ThrowAsync<BootstrapTokenClient.PairException>();
            handler.CallCount.Should().Be(3);
        }

        [Test]
        public async Task Pair_5xx_then_200_succeeds_after_retry()
        {
            int n = 0;
            var handler = new StubHandler(_ =>
            {
                n++;
                if (n == 1) return Json(HttpStatusCode.BadGateway, "{\"error\":\"upstream\"}");
                return Json(
                    HttpStatusCode.OK,
                    "{\"jwt\":\"t\",\"register_id\":\"KLR-01\",\"snapshot_age_seconds\":0,\"snapshot_available\":true}");
            });
            var http = new HttpClient(handler);
            var client = new BootstrapTokenClient("http://localhost", http, "fingerprint-" + new string('x', 30));

            var result = await client.PairAsync("123456", CancellationToken.None);
            result.Jwt.Should().Be("t");
            handler.CallCount.Should().Be(2);
        }

        [Test]
        public void Constructor_rejects_empty_supabaseUrl()
        {
            Action act = () => new BootstrapTokenClient(string.Empty, new HttpClient(), "fp" + new string('x', 30));
            act.Should().Throw<ArgumentException>();
        }

        [Test]
        public void Constructor_rejects_empty_fingerprint()
        {
            Action act = () => new BootstrapTokenClient("http://localhost", new HttpClient(), string.Empty);
            act.Should().Throw<ArgumentException>();
        }

        [Test]
        public void PairAsync_rejects_empty_code()
        {
            var http = new HttpClient(new StubHandler(_ => Json(HttpStatusCode.OK, "{}")));
            var client = new BootstrapTokenClient("http://localhost", http, "fp" + new string('x', 30));
            FluentActions.Invoking(() => client.PairAsync(string.Empty, CancellationToken.None))
                .Should().ThrowAsync<ArgumentException>();
        }

        [Test]
        public void ComputeDeviceFingerprint_returns_64char_hex()
        {
            Environment.SetEnvironmentVariable("KASIR_DEVICE_OVERRIDE", "test-override");
            try
            {
                var fp = BootstrapTokenClient.ComputeDeviceFingerprint();
                fp.Length.Should().Be(64);
                fp.Should().MatchRegex("^[0-9a-f]{64}$");
            }
            finally
            {
                Environment.SetEnvironmentVariable("KASIR_DEVICE_OVERRIDE", null);
            }
        }

        [Test]
        public void ComputeDeviceFingerprint_is_deterministic_with_override()
        {
            Environment.SetEnvironmentVariable("KASIR_DEVICE_OVERRIDE", "fixed-value");
            try
            {
                var a = BootstrapTokenClient.ComputeDeviceFingerprint();
                var b = BootstrapTokenClient.ComputeDeviceFingerprint();
                a.Should().Be(b);
            }
            finally
            {
                Environment.SetEnvironmentVariable("KASIR_DEVICE_OVERRIDE", null);
            }
        }
    }
}
