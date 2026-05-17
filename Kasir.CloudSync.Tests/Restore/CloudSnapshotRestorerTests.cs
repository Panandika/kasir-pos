using System;
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
    // Unit tests for CloudSnapshotRestorer.
    // Full end-to-end (network + Supabase) is integration test US-P5-1.
    //
    // PRD story: US-P3-1
    [TestFixture]
    public class CloudSnapshotRestorerTests
    {
        private static string TempDir()
        {
            var d = Path.Combine(Path.GetTempPath(), "csrtest-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(d);
            return d;
        }

        [Test]
        public async Task ComputeSha256Async_matches_known_hash()
        {
            var dir = TempDir();
            try
            {
                var path = Path.Combine(dir, "f");
                await File.WriteAllBytesAsync(path, new byte[] { 0x61, 0x62, 0x63 }); // "abc"
                var hash = await CloudSnapshotRestorer.ComputeSha256Async(path, CancellationToken.None);
                // SHA-256("abc") = ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad
                hash.Should().Be("ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad");
            }
            finally { Directory.Delete(dir, true); }
        }

        [Test]
        public async Task ComputeSha256Async_deterministic_for_same_file()
        {
            var dir = TempDir();
            try
            {
                var path = Path.Combine(dir, "f");
                await File.WriteAllBytesAsync(path, new byte[] { 1, 2, 3, 4, 5 });
                var a = await CloudSnapshotRestorer.ComputeSha256Async(path, CancellationToken.None);
                var b = await CloudSnapshotRestorer.ComputeSha256Async(path, CancellationToken.None);
                a.Should().Be(b);
                a.Length.Should().Be(64);
            }
            finally { Directory.Delete(dir, true); }
        }

        [Test]
        public void AtomicSwap_replaces_existing_file_and_keeps_bak()
        {
            var dir = TempDir();
            try
            {
                var target = Path.Combine(dir, "kasir.db");
                var tmp = Path.Combine(dir, "kasir.db.tmp");
                File.WriteAllText(target, "old");
                File.WriteAllText(tmp, "new");

                CloudSnapshotRestorer.AtomicSwap(tmp, target);

                File.ReadAllText(target).Should().Be("new");
                File.Exists(target + ".bak").Should().BeTrue();
                File.ReadAllText(target + ".bak").Should().Be("old");
                File.Exists(tmp).Should().BeFalse();
            }
            finally { Directory.Delete(dir, true); }
        }

        [Test]
        public void AtomicSwap_handles_no_existing_target()
        {
            var dir = TempDir();
            try
            {
                var target = Path.Combine(dir, "kasir.db");
                var tmp = Path.Combine(dir, "kasir.db.tmp");
                File.WriteAllText(tmp, "fresh");
                CloudSnapshotRestorer.AtomicSwap(tmp, target);
                File.ReadAllText(target).Should().Be("fresh");
                File.Exists(target + ".bak").Should().BeFalse();
            }
            finally { Directory.Delete(dir, true); }
        }

        [Test]
        public void AtomicSwap_overwrites_existing_bak()
        {
            var dir = TempDir();
            try
            {
                var target = Path.Combine(dir, "kasir.db");
                var tmp = Path.Combine(dir, "kasir.db.tmp");
                var bak = Path.Combine(dir, "kasir.db.bak");
                File.WriteAllText(target, "current");
                File.WriteAllText(tmp, "new");
                File.WriteAllText(bak, "previous");
                CloudSnapshotRestorer.AtomicSwap(tmp, target);
                File.ReadAllText(target).Should().Be("new");
                File.ReadAllText(bak).Should().Be("current");
            }
            finally { Directory.Delete(dir, true); }
        }

        [Test]
        public void CheckDiskSpace_passes_for_modest_size()
        {
            // 1 KB on any disk should be fine
            var dir = TempDir();
            try
            {
                CloudSnapshotRestorer.CheckDiskSpace(
                    Path.Combine(dir, "kasir.db"),
                    1024L);
            }
            finally { Directory.Delete(dir, true); }
        }

        [Test]
        public void Constructor_rejects_empty_supabaseUrl()
        {
            Action act = () => new CloudSnapshotRestorer(string.Empty);
            act.Should().Throw<ArgumentException>();
        }

        [Test]
        public async Task RunAsync_rejects_schema_version_too_new()
        {
            // Stub manifest response with schema_version > supported
            var handler = new StubHandler((req) =>
            {
                if (req.RequestUri.AbsolutePath.EndsWith("/snapshot-download"))
                {
                    return JsonResponse(HttpStatusCode.OK,
                        "{\"signed_url\":\"https://x.example\",\"sha256\":\"" +
                        new string('a', 64) +
                        "\",\"size_bytes\":100,\"schema_version\":999,\"built_at\":\"2026-01-01T00:00:00Z\",\"expires_at\":\"2026-01-01T00:15:00Z\"}");
                }
                return new HttpResponseMessage(HttpStatusCode.NotFound);
            });
            var http = new HttpClient(handler);
            var restorer = new CloudSnapshotRestorer("http://localhost", http);
            var target = Path.Combine(Path.GetTempPath(), "kasir-" + Guid.NewGuid().ToString("N") + ".db");
            await FluentActions.Invoking(() =>
                restorer.RunAsync("fake-jwt", target, null, CancellationToken.None))
                .Should().ThrowAsync<CloudSnapshotRestorer.RestoreException>()
                .Where(e => e.Stage == "manifest" && e.Message.Contains("schema_version=999"));
        }

        [Test]
        public async Task RunAsync_rejects_incomplete_manifest()
        {
            var handler = new StubHandler((req) =>
                JsonResponse(HttpStatusCode.OK, "{\"signed_url\":\"\",\"sha256\":\"\"}"));
            var http = new HttpClient(handler);
            var restorer = new CloudSnapshotRestorer("http://localhost", http);
            var target = Path.Combine(Path.GetTempPath(), "kasir-" + Guid.NewGuid().ToString("N") + ".db");
            await FluentActions.Invoking(() =>
                restorer.RunAsync("fake-jwt", target, null, CancellationToken.None))
                .Should().ThrowAsync<CloudSnapshotRestorer.RestoreException>()
                .Where(e => e.Stage == "manifest");
        }

        [Test]
        public async Task RunAsync_propagates_unauthorized()
        {
            var handler = new StubHandler((req) =>
                JsonResponse(HttpStatusCode.Unauthorized, "{\"error\":\"unauthorized\"}"));
            var http = new HttpClient(handler);
            var restorer = new CloudSnapshotRestorer("http://localhost", http);
            var target = Path.Combine(Path.GetTempPath(), "kasir-" + Guid.NewGuid().ToString("N") + ".db");
            await FluentActions.Invoking(() =>
                restorer.RunAsync("bad-jwt", target, null, CancellationToken.None))
                .Should().ThrowAsync<CloudSnapshotRestorer.RestoreException>()
                .Where(e => e.Stage == "manifest" && e.Message.Contains("401"));
        }

        // ─────────────────────────────────────────────────────────────

        private class StubHandler : HttpMessageHandler
        {
            private readonly Func<HttpRequestMessage, HttpResponseMessage> _r;
            public StubHandler(Func<HttpRequestMessage, HttpResponseMessage> r) { _r = r; }
            protected override Task<HttpResponseMessage> SendAsync(
                HttpRequestMessage req, CancellationToken ct)
            {
                return Task.FromResult(_r(req));
            }
        }

        private static HttpResponseMessage JsonResponse(HttpStatusCode code, string body)
        {
            return new HttpResponseMessage(code)
            {
                Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json"),
            };
        }
    }
}
