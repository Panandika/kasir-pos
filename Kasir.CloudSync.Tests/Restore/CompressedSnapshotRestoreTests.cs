using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Kasir.CloudSync.Restore;
using Kasir.CloudSync.Snapshot;
using Kasir.Data;
using Microsoft.Data.Sqlite;
using NUnit.Framework;

namespace Kasir.CloudSync.Tests.Restore
{
    // Snapshots are Brotli-compressed (~189 MB raw -> ~37 MB) to fit the Supabase
    // free-plan 50 MB Storage object limit. sha256/size_bytes in the manifest describe
    // the downloaded (compressed) bytes; the restorer verifies them, then decompresses.
    [TestFixture]
    public class CompressedSnapshotRestoreTests
    {
        private string _dir;

        [SetUp]
        public void SetUp()
        {
            _dir = Path.Combine(Path.GetTempPath(), "br-restore-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_dir);
        }

        [TearDown]
        public void TearDown()
        {
            SqliteConnection.ClearAllPools();
            if (Directory.Exists(_dir)) Directory.Delete(_dir, true);
        }

        // ── SnapshotCompression ──────────────────────────────────────

        [Test]
        public async Task CompressFile_RoundTrips_AndReportsShaOfCompressedBytes()
        {
            var src = Path.Combine(_dir, "raw.db");
            var data = new byte[3 * 1024 * 1024 + 123];
            var rnd = new Random(7);
            for (int i = 0; i < data.Length; i++) data[i] = (byte)(i % 97 == 0 ? rnd.Next(256) : i % 13);
            await File.WriteAllBytesAsync(src, data);

            var c = SnapshotCompression.CompressFile(src, src + ".br");

            c.SizeBytes.Should().Be(new FileInfo(src + ".br").Length);
            c.SizeBytes.Should().BeLessThan(data.Length / 4, "repetitive SQLite-like pages compress well");
            c.Sha256.Should().Be(await CloudSnapshotRestorer.ComputeSha256Async(src + ".br", CancellationToken.None));

            var back = Path.Combine(_dir, "back.db");
            long n = await SnapshotCompression.DecompressFileAsync(src + ".br", back, CancellationToken.None);
            n.Should().Be(data.Length);
            (await File.ReadAllBytesAsync(back)).Should().Equal(data);
        }

        [Test]
        public async Task CompressFile_EmptyFile_RoundTrips()
        {
            var src = Path.Combine(_dir, "empty.db");
            await File.WriteAllBytesAsync(src, Array.Empty<byte>());
            SnapshotCompression.CompressFile(src, src + ".br");
            var back = Path.Combine(_dir, "empty-back.db");
            (await SnapshotCompression.DecompressFileAsync(src + ".br", back, CancellationToken.None)).Should().Be(0);
        }

        [TestCase("snapshots/snapshot-abc.db.br", "br")]
        [TestCase("snapshot-abc.DB.BR", "br")]
        [TestCase("snapshots/snapshot-abc.db", null)]
        [TestCase("snapshots/snapshot-abc.br", null)]
        [TestCase(null, null)]
        public void EncodingFromPath(string path, string expected)
        {
            SnapshotCompression.EncodingFromPath(path).Should().Be(expected);
        }

        [Test]
        public void RequiredDiskBytes_AccountsForDecompression()
        {
            CloudSnapshotRestorer.RequiredDiskBytes(1000, compressed: false).Should().Be(1100);
            CloudSnapshotRestorer.RequiredDiskBytes(1000, compressed: true).Should().Be(8000);
        }

        // ── RunAsync end to end (stubbed HTTP, real files) ───────────

        [Test]
        public async Task RunAsync_Brotli_VerifiesCompressedSha_Decompresses_AndInstalls()
        {
            var raw = CreateSnapshotDb();
            var br = SnapshotCompression.CompressFile(raw, raw + ".br");
            var target = Path.Combine(_dir, "kasir.db");

            var restorer = Restorer(br.Sha256, br.SizeBytes, "br", await File.ReadAllBytesAsync(br.Path));
            string lastStage = null;
            bool sawDecompress = false;
            var progress = new SyncProgress(p => { lastStage = p.Stage; sawDecompress |= p.Stage == "decompressing"; });

            var summary = await restorer.RunAsync("jwt", target, progress, CancellationToken.None, "KLR-01");

            sawDecompress.Should().BeTrue();
            lastStage.Should().Be("done");
            summary.RegisterId.Should().Be("01");
            IntegrityOk(target).Should().BeTrue();
            // Seeded login + register number must be in the installed file itself, not
            // stranded in a "-wal" side file left behind under the temp name.
            Scalar(target, "select count(*) from users where username = 'SM' and is_active = 1").Should().Be("1");
            Scalar(target, "select value from config where key = 'register_id'").Should().Be("01");
            Directory.GetFiles(_dir, "kasir.db.tmp*").Should().BeEmpty("temp files (.tmp and .tmp.br) are cleaned up");
        }

        [Test]
        public async Task RunAsync_Plain_StillWorks_ForOlderServers()
        {
            var raw = CreateSnapshotDb();
            var sha = await CloudSnapshotRestorer.ComputeSha256Async(raw, CancellationToken.None);
            var target = Path.Combine(_dir, "kasir.db");

            var restorer = Restorer(sha, new FileInfo(raw).Length, encoding: null, await File.ReadAllBytesAsync(raw));
            await restorer.RunAsync("jwt", target, null, CancellationToken.None, "KLR-02");

            IntegrityOk(target).Should().BeTrue();
            Scalar(target, "select count(*) from users where username = 'SM' and is_active = 1").Should().Be("1");
            Scalar(target, "select value from config where key = 'register_id'").Should().Be("02");
            Directory.GetFiles(_dir, "kasir.db.tmp*").Should().BeEmpty();
        }

        [Test]
        public async Task RunAsync_Brotli_ShaMismatch_FailsVerifying_BeforeDecompress()
        {
            var raw = CreateSnapshotDb();
            var br = SnapshotCompression.CompressFile(raw, raw + ".br");
            var target = Path.Combine(_dir, "kasir.db");

            var restorer = Restorer(new string('0', 64), br.SizeBytes, "br", await File.ReadAllBytesAsync(br.Path));

            await FluentActions.Invoking(() => restorer.RunAsync("jwt", target, null, CancellationToken.None))
                .Should().ThrowAsync<CloudSnapshotRestorer.RestoreException>()
                .Where(e => e.Stage == "verifying" && e.Message.Contains("SHA-256"));
            File.Exists(target).Should().BeFalse();
        }

        [Test]
        public async Task RunAsync_CorruptBrotli_WithMatchingSha_FailsDecompressing()
        {
            var bogus = Path.Combine(_dir, "bogus.br");
            await File.WriteAllBytesAsync(bogus, new byte[] { 0xFF, 0xFE, 0x00, 0x12, 0x34, 0x56, 0x78, 0x9A });
            var sha = await CloudSnapshotRestorer.ComputeSha256Async(bogus, CancellationToken.None);
            var target = Path.Combine(_dir, "kasir.db");

            var restorer = Restorer(sha, 8, "br", await File.ReadAllBytesAsync(bogus));

            await FluentActions.Invoking(() => restorer.RunAsync("jwt", target, null, CancellationToken.None))
                .Should().ThrowAsync<CloudSnapshotRestorer.RestoreException>()
                .Where(e => e.Stage == "decompressing");
            File.Exists(target).Should().BeFalse();
            Directory.GetFiles(_dir, "kasir.db.tmp*").Should().BeEmpty();
        }

        [Test]
        public async Task RunAsync_UnknownEncoding_FailsManifest()
        {
            var target = Path.Combine(_dir, "kasir.db");
            var restorer = Restorer(new string('a', 64), 10, "zstd", new byte[10]);

            await FluentActions.Invoking(() => restorer.RunAsync("jwt", target, null, CancellationToken.None))
                .Should().ThrowAsync<CloudSnapshotRestorer.RestoreException>()
                .Where(e => e.Stage == "manifest" && e.Message.StartsWith("Unsupported snapshot encoding"));
        }

        // ── Error explanations ───────────────────────────────────────

        [Test]
        public void ForRestore_Decompressing_ExplainsCorruptPackage()
        {
            var e = CloudImportErrors.ForRestore("decompressing", null, "", "Snapshot data is corrupt (brotli): x");
            e.Title.Should().Contain("tidak bisa dibuka");
            e.Action.Should().Contain("Bangun snapshot");
        }

        [Test]
        public void ForRestore_DecompressingDiskFull_ExplainsDiskSpace()
        {
            var e = CloudImportErrors.ForRestore("decompressing", null, "", "Insufficient disk space while decompressing: x");
            e.Title.Should().Be("Ruang disk tidak cukup.");
        }

        [Test]
        public void ForRestore_UnsupportedEncoding_AsksForUpdate()
        {
            var e = CloudImportErrors.ForRestore("manifest", null, "", "Unsupported snapshot encoding \"zstd\". Update POS.");
            e.Action.Should().Contain("Perbarui aplikasi");
        }

        // ── helpers ──────────────────────────────────────────────────

        // Schema.sql only — the shape SnapshotBuilder produces before copying rows.
        private string CreateSnapshotDb()
        {
            var path = Path.Combine(_dir, "snapshot-" + Guid.NewGuid().ToString("N") + ".db");
            string schema;
            using (var stream = typeof(DbConnection).Assembly.GetManifestResourceStream("Kasir.Data.Schema.sql"))
            using (var reader = new StreamReader(stream))
                schema = reader.ReadToEnd();
            using (var conn = new SqliteConnection($"Data Source={path};Pooling=False"))
            {
                conn.Open();
                using var cmd = conn.CreateCommand();
                cmd.CommandText = schema;
                cmd.ExecuteNonQuery();
            }
            return path;
        }

        // Copies the file alone (as a register would see it after the swap, without any
        // stray -wal/-shm siblings) and queries the copy.
        private string Scalar(string path, string sql)
        {
            SqliteConnection.ClearAllPools();
            var copy = Path.Combine(_dir, "inspect-" + Guid.NewGuid().ToString("N") + ".db");
            File.Copy(path, copy);
            using var conn = new SqliteConnection($"Data Source={copy};Pooling=False");
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = sql;
            return cmd.ExecuteScalar()?.ToString();
        }

        private static bool IntegrityOk(string path)
        {
            SqliteConnection.ClearAllPools();
            using var conn = new SqliteConnection($"Data Source={path};Pooling=False");
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "PRAGMA integrity_check;";
            var result = cmd.ExecuteScalar()?.ToString();
            TestContext.Out.WriteLine("integrity_check: " + result);
            return result == "ok";
        }

        private static CloudSnapshotRestorer Restorer(string sha, long size, string encoding, byte[] body)
        {
            string enc = encoding == null ? "" : ",\"encoding\":\"" + encoding + "\"";
            var handler = new StubHandler(req =>
            {
                if (req.RequestUri.AbsolutePath.EndsWith("/snapshot-download"))
                {
                    return new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new StringContent(
                            "{\"signed_url\":\"https://storage.example/snap?token=t\",\"sha256\":\"" + sha +
                            "\",\"size_bytes\":" + size + ",\"schema_version\":1,\"built_at\":\"2026-10-03T00:00:00Z\"," +
                            "\"expires_at\":\"2026-10-03T00:15:00Z\"" + enc + "}",
                            System.Text.Encoding.UTF8, "application/json"),
                    };
                }
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(body) };
            });
            return new CloudSnapshotRestorer("http://localhost", new HttpClient(handler));
        }

        private sealed class StubHandler : HttpMessageHandler
        {
            private readonly Func<HttpRequestMessage, HttpResponseMessage> _r;
            public StubHandler(Func<HttpRequestMessage, HttpResponseMessage> r) { _r = r; }
            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage req, CancellationToken ct) =>
                Task.FromResult(_r(req));
        }

        // Progress<T> posts to a sync context; this one reports inline.
        private sealed class SyncProgress : IProgress<CloudSnapshotRestorer.RestoreProgress>
        {
            private readonly Action<CloudSnapshotRestorer.RestoreProgress> _a;
            public SyncProgress(Action<CloudSnapshotRestorer.RestoreProgress> a) { _a = a; }
            public void Report(CloudSnapshotRestorer.RestoreProgress value) => _a(value);
        }
    }
}
