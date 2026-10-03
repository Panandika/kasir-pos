using System;
using System.IO;
using System.Net.Http;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Threading.Tasks;
using FluentAssertions;
using Kasir.Services;
using NUnit.Framework;

namespace Kasir.Tests.Services
{
    // Owner testing items #10 / #11 (2026-10-03).
    [TestFixture]
    public class UpdateTextTests
    {
        // ---- #10: say what really failed ----

        [Test]
        public void SslHandshakeFailure_IsNotReportedAsTimeout()
        {
            var ex = new HttpRequestException("The SSL connection could not be established, see inner exception.",
                new IOException("Received an unexpected EOF or 0 bytes from the transport stream."));
            GitHubReleaseClient.ClassifyNetworkError(ex).Should().Be(UpdateMessages.SslFailed);
        }

        [Test]
        public void AuthenticationException_IsSslFailure()
        {
            var ex = new HttpRequestException("x", new AuthenticationException("remote certificate invalid"));
            GitHubReleaseClient.ClassifyNetworkError(ex).Should().Be(UpdateMessages.SslFailed);
        }

        [Test]
        public void DnsFailure_IsOffline()
        {
            var ex = new HttpRequestException("No such host is known.", new SocketException((int)SocketError.HostNotFound));
            GitHubReleaseClient.ClassifyNetworkError(ex).Should().Be(UpdateMessages.Offline);
        }

        [Test]
        public void ConnectStall_IsConnectionProblem_NotSlowServer()
        {
            // What .NET throws when TCP/TLS set-up exceeds SocketsHttpHandler.ConnectTimeout
            // (reproduced live: SSL handshake that never completes).
            var ex = new TaskCanceledException("The request was canceled",
                new TimeoutException("A connection could not be established within the configured ConnectTimeout."));
            GitHubReleaseClient.ClassifyNetworkError(ex).Should().Be(UpdateMessages.ConnectStalled);
        }

        private sealed class FlakyHandler : HttpMessageHandler
        {
            public int Calls;
            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, System.Threading.CancellationToken ct)
            {
                Calls++;
                if (Calls == 1)
                    throw new HttpRequestException("The SSL connection could not be established, see inner exception.",
                        new IOException("Received an unexpected EOF or 0 bytes from the transport stream."));
                return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK)
                {
                    Content = new StringContent("{\"tag_name\":\"v9.9.9\",\"body\":\"x\",\"assets\":[]}")
                });
            }
        }

        [Test]
        public async Task FirstConnectionFails_SecondSucceeds_CheckStillWorks()
        {
            var handler = new FlakyHandler();
            var release = await new GitHubReleaseClient(new HttpClient(handler)).GetLatestAsync(System.Threading.CancellationToken.None);

            release.Version.Should().Be("9.9.9");
            handler.Calls.Should().Be(2);
        }

        [Test]
        public void Messages_AreDistinct_AndActionable()
        {
            UpdateMessages.SslFailed.Should().Contain("jam").And.NotContain("waktu habis");
            UpdateMessages.Timeout.Should().NotBe(UpdateMessages.Offline);
        }

        // ---- #11: readable "what's new" ----

        private const string ReleasePleaseBody =
            "## [2.9.0](https://github.com/Panandika/kasir-pos/compare/v2.8.0...v2.9.0) (2026-10-03)\n\n\n" +
            "### Features\n\n" +
            "* **cloudsync:** --build-snapshot CLI and hub-less snapshot publishing ([e5355b9](https://github.com/x/commit/e5355b9))\n" +
            "* **cloudsync:** --build-snapshot CLI and hub-less snapshot publishing ([0da8ca0](https://github.com/x/commit/0da8ca0))\n" +
            "* **update:** self-update from GitHub ([a6cef9a](https://github.com/x/commit/a6cef9a))\n\n" +
            "### Bug Fixes\n\n" +
            "* **ui:** Enter/Esc answer confirmation and input dialogs ([50ff225](https://github.com/x/commit/50ff225))\n\n" +
            "---\n\n## Cara Install (Windows)\n\n1. Download …\n";

        [Test]
        public void Changelog_IsCleaned_ForCashiers()
        {
            string notes = UpdateService.ExtractReleaseNotes(ReleasePleaseBody);

            notes.Should().StartWith("Versi 2.9.0 (2026-10-03)");
            notes.Should().Contain("Fitur baru:").And.Contain("Perbaikan:");
            notes.Should().Contain("• Enter/Esc answer confirmation and input dialogs");
            notes.Should().NotContain("](").And.NotContain("**").And.NotContain("Cara Install").And.NotContain("e5355b9");
            notes.Split("--build-snapshot CLI").Length.Should().Be(2, "duplicate entries are shown once");
        }

        [Test]
        public void CuratedIndonesianNotes_TakePriority()
        {
            string body = "<!-- kasir-notes -->\nYang baru:\n• Daftar register dari cloud\n<!-- /kasir-notes -->\n\n" + ReleasePleaseBody;

            UpdateService.ExtractReleaseNotes(body).Should().Be("Yang baru:\n• Daftar register dari cloud");
        }

        [Test]
        public void EmptyBody_GivesEmptyNotes()
        {
            UpdateService.ExtractReleaseNotes(null).Should().Be("");
        }
    }
}
