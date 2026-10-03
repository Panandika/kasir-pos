using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using NUnit.Framework;
using Kasir.Data.Repositories;
using Kasir.Services;
using Kasir.Tests.TestHelpers;
using Kasir.Utils;

namespace Kasir.Tests.Services
{
    // Self-update from GitHub Releases: check, asset choice, signature + manifest
    // verification, download/prepare. Uses a throwaway ECDSA key per test run — never
    // the real release key.
    [TestFixture]
    public class UpdateServiceTests
    {
        private SqliteConnection _db;
        private string _root;
        private ECDsa _key;
        private string _publicPem;

        [SetUp]
        public void SetUp()
        {
            _db = TestDb.Create();
            _root = Path.Combine(Path.GetTempPath(), "kasir-update-tests-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_root);
            _key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
            _publicPem = _key.ExportSubjectPublicKeyInfoPem();
        }

        [TearDown]
        public void TearDown()
        {
            _db.Dispose();
            _key.Dispose();
            try { Directory.Delete(_root, true); } catch { }
        }

        // ── helpers ──────────────────────────────────────────────────────────

        private string NewVersion()
        {
            var parts = AppVersion.Current.Split('.');
            return $"{int.Parse(parts[0])}.{int.Parse(parts[1])}.{int.Parse(parts[2].Split('-', '+')[0]) + 1}";
        }

        /// Writes a release folder: files + checksum.sha256 (+ signature unless unsigned).
        private string MakePackage(string dir, string version, bool sign = true, ECDsa signer = null)
        {
            Directory.CreateDirectory(dir);
            File.WriteAllText(Path.Combine(dir, "Kasir.Avalonia.exe"), "new exe");
            File.WriteAllText(Path.Combine(dir, "Kasir.Core.dll"), "new core");
            File.WriteAllText(Path.Combine(dir, "version.txt"), version);
            Directory.CreateDirectory(Path.Combine(dir, "Sql"));
            File.WriteAllText(Path.Combine(dir, "Sql", "x.sql"), "select 1;");
            WriteManifest(dir, sign, signer);
            return dir;
        }

        private void WriteManifest(string dir, bool sign = true, ECDsa signer = null)
        {
            var lines = new List<string>();
            foreach (var f in Directory.GetFiles(dir, "*", SearchOption.AllDirectories))
            {
                string rel = Path.GetRelativePath(dir, f).Replace('\\', '/');
                if (rel == "checksum.sha256" || rel == "checksum.sha256.sig") continue;
                lines.Add(UpdateService.ComputeFileSha256(f) + "  " + rel);
            }
            byte[] manifest = Encoding.UTF8.GetBytes(string.Join("\n", lines));
            File.WriteAllBytes(Path.Combine(dir, "checksum.sha256"), manifest);
            if (sign)
            {
                byte[] sig = (signer ?? _key).SignData(manifest, HashAlgorithmName.SHA256);
                File.WriteAllText(Path.Combine(dir, "checksum.sha256.sig"), Convert.ToBase64String(sig));
            }
        }

        private UpdateService NewSut(FakeReleaseSource source, string registerId = null)
        {
            if (registerId != null) new ConfigRepository(_db).Set("register_id", registerId);
            return new UpdateService(_db, new FileSystemImpl(), source, 5000)
            {
                PublicKeyPem = _publicPem,
                BaseDirectory = Path.Combine(_root, "install"),
            };
        }

        private static GitHubRelease Release(string version, params string[] assetNames)
        {
            var r = new GitHubRelease { TagName = "v" + version, Version = version, Body = "### Features\n* hal baru\n\n---\n\n## Cara Install (Windows)\nlangkah..." };
            foreach (var n in assetNames)
                r.Assets.Add(new GitHubReleaseAsset { Name = n, DownloadUrl = "https://example.invalid/" + n, Size = 1000 });
            return r;
        }

        // ── signature ────────────────────────────────────────────────────────

        [Test]
        public void VerifySignature_ValidSignature_True()
        {
            string dir = MakePackage(Path.Combine(_root, "pkg"), "9.9.9");
            Assert.IsTrue(NewSut(new FakeReleaseSource()).VerifySignature(dir));
        }

        [Test]
        public void VerifySignature_ChecksumTamperedAfterSigning_False()
        {
            string dir = MakePackage(Path.Combine(_root, "pkg"), "9.9.9");
            File.AppendAllText(Path.Combine(dir, "checksum.sha256"), "\ndeadbeef  evil.dll");
            Assert.IsFalse(NewSut(new FakeReleaseSource()).VerifySignature(dir));
        }

        [Test]
        public void VerifySignature_MissingSignature_False()
        {
            string dir = MakePackage(Path.Combine(_root, "pkg"), "9.9.9", sign: false);
            Assert.IsFalse(NewSut(new FakeReleaseSource()).VerifySignature(dir));
        }

        [Test]
        public void VerifySignature_SignedWithOtherKey_False()
        {
            using (var attacker = ECDsa.Create(ECCurve.NamedCurves.nistP256))
            {
                string dir = MakePackage(Path.Combine(_root, "pkg"), "9.9.9", signer: attacker);
                Assert.IsFalse(NewSut(new FakeReleaseSource()).VerifySignature(dir));
            }
        }

        [Test]
        public void VerifySignature_GarbageSignature_False()
        {
            string dir = MakePackage(Path.Combine(_root, "pkg"), "9.9.9");
            File.WriteAllText(Path.Combine(dir, "checksum.sha256.sig"), "not-base64!!");
            Assert.IsFalse(NewSut(new FakeReleaseSource()).VerifySignature(dir));
        }

        [Test]
        public void ReleaseWorkflowSigning_RoundTripsWithAppVerifier()
        {
            // Mirrors release.yml: secret = PKCS#8 PEM ("BEGIN PRIVATE KEY", as made by
            // `openssl pkcs8 -topk8 -nocrypt`), ECDsa.ImportFromPem, SignData(SHA256),
            // base64 → checksum.sha256.sig. The app must accept exactly that.
            string privatePem = _key.ExportPkcs8PrivateKeyPem();
            byte[] manifest = Encoding.UTF8.GetBytes("abc123  Kasir.Avalonia.exe\nfff  version.txt");
            string sigB64;
            using (var signer = ECDsa.Create())
            {
                signer.ImportFromPem(privatePem);
                sigB64 = Convert.ToBase64String(signer.SignData(manifest, HashAlgorithmName.SHA256));
            }
            Assert.IsTrue(UpdateSignature.Verify(manifest, sigB64 + "\n", _publicPem));
        }

        [Test]
        public void EmbeddedReleaseKey_IsAValidP256PublicKey()
        {
            using (var ec = ECDsa.Create())
            {
                ec.ImportFromPem(UpdateSignature.PublicKeyPem);
                Assert.AreEqual(256, ec.KeySize);
            }
            // A signature from an unrelated key must not verify against the release key.
            byte[] data = Encoding.UTF8.GetBytes("x");
            Assert.IsFalse(UpdateSignature.Verify(data, Convert.ToBase64String(_key.SignData(data, HashAlgorithmName.SHA256))));
        }

        // ── manifest ─────────────────────────────────────────────────────────

        [Test]
        public void VerifyChecksums_AllMatch_True()
        {
            string dir = MakePackage(Path.Combine(_root, "pkg"), "9.9.9");
            Assert.IsTrue(NewSut(new FakeReleaseSource()).VerifyChecksums(dir));
        }

        [Test]
        public void VerifyChecksums_FileTampered_False()
        {
            string dir = MakePackage(Path.Combine(_root, "pkg"), "9.9.9");
            File.WriteAllText(Path.Combine(dir, "Kasir.Core.dll"), "evil core");
            Assert.IsFalse(NewSut(new FakeReleaseSource()).VerifyChecksums(dir));
        }

        [Test]
        public void VerifyChecksums_UnlistedPlantedFile_False()
        {
            string dir = MakePackage(Path.Combine(_root, "pkg"), "9.9.9");
            File.WriteAllText(Path.Combine(dir, "Sql", "planted.dll"), "evil");
            Assert.IsFalse(NewSut(new FakeReleaseSource()).VerifyChecksums(dir));
        }

        [Test]
        public void VerifyChecksums_MissingManifest_False()
        {
            string dir = Path.Combine(_root, "pkg");
            Directory.CreateDirectory(dir);
            File.WriteAllText(Path.Combine(dir, "Kasir.Avalonia.exe"), "x");
            Assert.IsFalse(NewSut(new FakeReleaseSource()).VerifyChecksums(dir), "unsigned/unlisted packages are refused, not skipped");
        }

        // ── check ────────────────────────────────────────────────────────────

        [Test]
        public async Task Check_NewerRelease_AvailableWithRegisterAsset()
        {
            string v = NewVersion();
            var src = new FakeReleaseSource { Latest = Release(v, $"kasir-{v}-register-01.zip", $"kasir-{v}-register-02.zip") };
            var r = await NewSut(src, "02").CheckForUpdateAsync();

            Assert.IsTrue(r.Available);
            Assert.AreEqual(v, r.NewVersion);
            Assert.AreEqual($"kasir-{v}-register-02.zip", r.AssetName);
            Assert.That(r.ReleaseNotes, Does.Contain("hal baru"));
            Assert.That(r.ReleaseNotes, Does.Not.Contain("Cara Install"));
            Assert.IsNotNull(new ConfigRepository(_db).Get("last_update_check"));
        }

        [Test]
        public async Task Check_SameVersion_NotAvailable()
        {
            var src = new FakeReleaseSource { Latest = Release(AppVersion.Current, $"kasir-{AppVersion.Current}-register-01.zip") };
            var r = await NewSut(src).CheckForUpdateAsync();
            Assert.IsFalse(r.Available);
            Assert.IsNull(r.Error);
        }

        [Test]
        public async Task Check_OfflineOrRateLimited_ReturnsIndonesianError()
        {
            var src = new FakeReleaseSource { Throw = new UpdateSourceException(UpdateMessages.RateLimited) };
            var r = await NewSut(src).CheckForUpdateAsync();
            Assert.IsFalse(r.Available);
            Assert.AreEqual(UpdateMessages.RateLimited, r.Error);
        }

        [Test]
        public async Task Check_WithoutDatabase_StillWorks()
        {
            string v = NewVersion();
            var src = new FakeReleaseSource { Latest = Release(v, $"kasir-{v}-register-01.zip") };
            var sut = new UpdateService(null, new FileSystemImpl(), src, 5000) { PublicKeyPem = _publicPem };
            var r = await sut.CheckForUpdateAsync();
            Assert.IsTrue(r.Available, "first-run screens check updates before any DB exists");
            Assert.AreEqual($"kasir-{v}-register-01.zip", r.AssetName);
        }

        [Test]
        public async Task Check_NewerReleaseWithoutZip_ReportsError()
        {
            string v = NewVersion();
            var src = new FakeReleaseSource { Latest = Release(v, "notes.txt") };
            var r = await NewSut(src).CheckForUpdateAsync();
            Assert.IsFalse(r.Available);
            Assert.That(r.Error, Does.Contain(v));
        }

        [TestCase("KLR-03", "kasir-2.9.0-register-03.zip")]
        [TestCase("03", "kasir-2.9.0-register-03.zip")]
        [TestCase("99", "kasir-2.9.0-register-01.zip")]   // no zip for 99 → register-01
        [TestCase(null, "kasir-2.9.0-register-01.zip")]
        public void SelectAsset_PicksRegisterZipWithFallback(string registerId, string expected)
        {
            var rel = Release("2.9.0", "kasir-2.9.0-register-01.zip", "kasir-2.9.0-register-02.zip", "kasir-2.9.0-register-03.zip");
            Assert.AreEqual(expected, UpdateService.SelectAsset(rel, registerId).Name);
        }

        [Test]
        public void ParseGitHubRelease_ReadsTagBodyAssets()
        {
            const string json = "{\"tag_name\":\"v2.9.0\",\"html_url\":\"https://github.com/x\",\"body\":\"notes\",\"assets\":[{\"name\":\"kasir-2.9.0-register-01.zip\",\"size\":123,\"browser_download_url\":\"https://dl/1\"}]}";
            var r = GitHubReleaseClient.Parse(json);
            Assert.AreEqual("v2.9.0", r.TagName);
            Assert.AreEqual("2.9.0", r.Version);
            Assert.AreEqual("notes", r.Body);
            Assert.AreEqual(1, r.Assets.Count);
            Assert.AreEqual(123, r.Assets[0].Size);
            Assert.AreEqual("https://dl/1", r.Assets[0].DownloadUrl);
        }

        [TestCase(HttpStatusCode.NotFound, UpdateMessages.NoRelease)]
        [TestCase(HttpStatusCode.Forbidden, UpdateMessages.RateLimited)]
        public void GitHubClient_MapsHttpErrorsToIndonesian(HttpStatusCode status, string expected)
        {
            var client = new GitHubReleaseClient(new HttpClient(new StubHandler(status, "{}")));
            var ex = Assert.ThrowsAsync<UpdateSourceException>(() => client.GetLatestAsync(CancellationToken.None));
            Assert.AreEqual(expected, ex.Message);
        }

        [Test]
        public async Task GitHubClient_SendsUserAgent_AndParses()
        {
            var handler = new StubHandler(HttpStatusCode.OK, "{\"tag_name\":\"v3.0.0\",\"assets\":[]}");
            var r = await new GitHubReleaseClient(new HttpClient(handler)).GetLatestAsync(CancellationToken.None);
            Assert.AreEqual("3.0.0", r.Version);
            Assert.AreEqual(GitHubReleaseClient.LatestReleaseUrl, handler.LastRequest.RequestUri.ToString());
            Assert.IsTrue(handler.LastRequest.Headers.UserAgent.Count > 0, "GitHub API rejects requests without User-Agent");
        }

        // ── download + prepare ───────────────────────────────────────────────

        private async Task<(UpdateService sut, UpdatePrepareResult prep)> PrepareWith(Action<string> mutatePackage, string pkgVersion = null)
        {
            string v = NewVersion();
            string pkg = MakePackage(Path.Combine(_root, "built"), pkgVersion ?? v);
            mutatePackage?.Invoke(pkg);
            string zip = Path.Combine(_root, "release.zip");
            ZipFile.CreateFromDirectory(pkg, zip);

            var src = new FakeReleaseSource { Latest = Release(v, $"kasir-{v}-register-01.zip"), ZipPath = zip };
            var sut = NewSut(src);
            var check = await sut.CheckForUpdateAsync();
            Assert.IsTrue(check.Available);
            return (sut, await sut.DownloadAndPrepareAsync(check, null, CancellationToken.None));
        }

        [Test]
        public async Task Prepare_SignedPackage_Succeeds_AndStagesApp()
        {
            var (sut, prep) = await PrepareWith(null);
            Assert.IsTrue(prep.Success, prep.Error);
            Assert.IsTrue(File.Exists(Path.Combine(sut.GetStagedAppPath(), "Kasir.Avalonia.exe")));
            Assert.IsFalse(File.Exists(Path.Combine(sut.GetStagingPath(), "download.zip")));
        }

        [Test]
        public async Task Prepare_TamperedFile_Refused_AndStagingRemoved()
        {
            var (sut, prep) = await PrepareWith(pkg => File.WriteAllText(Path.Combine(pkg, "Kasir.Core.dll"), "evil"));
            Assert.IsFalse(prep.Success);
            Assert.AreEqual(UpdateMessages.ChecksumFailed, prep.Error);
            Assert.IsFalse(Directory.Exists(sut.GetStagingPath()));
        }

        [Test]
        public async Task Prepare_UnsignedPackage_Refused()
        {
            var (_, prep) = await PrepareWith(pkg => File.Delete(Path.Combine(pkg, "checksum.sha256.sig")));
            Assert.IsFalse(prep.Success);
            Assert.AreEqual(UpdateMessages.SignatureFailed, prep.Error);
        }

        [Test]
        public async Task Prepare_OlderSignedPackageUnderNewTag_Refused()
        {
            var (_, prep) = await PrepareWith(null, pkgVersion: "1.0.0");
            Assert.IsFalse(prep.Success);
            Assert.That(prep.Error, Does.Contain("1.0.0"));
        }

        [Test]
        public async Task Prepare_InsufficientDisk_Refused()
        {
            string v = NewVersion();
            var src = new FakeReleaseSource { Latest = Release(v, $"kasir-{v}-register-01.zip") };
            var fs = new LowDiskFs();
            var sut = new UpdateService(_db, fs, src, 5000) { PublicKeyPem = _publicPem, BaseDirectory = Path.Combine(_root, "install") };
            var check = await sut.CheckForUpdateAsync();
            var prep = await sut.DownloadAndPrepareAsync(check, null, CancellationToken.None);
            Assert.IsFalse(prep.Success);
            Assert.That(prep.Error, Does.Contain("Ruang disk"));
        }

        // ── the removed hub path stays removed ───────────────────────────────

        [Test]
        public void HubShareAndHmacUpdatePath_IsGone()
        {
            var t = typeof(UpdateService);
            Assert.IsNull(t.GetMethod("PublishToShare"));
            Assert.IsNull(t.GetMethod("VerifyChecksumHmac"));
            Assert.IsNull(t.GetMethod("GetUpdateSharePath"));
            Assert.IsNull(t.GetMethod("ComputeHmac"));
        }

        // ── fakes ────────────────────────────────────────────────────────────

        private sealed class FakeReleaseSource : IReleaseSource
        {
            public GitHubRelease Latest;
            public Exception Throw;
            public string ZipPath;

            public Task<GitHubRelease> GetLatestAsync(CancellationToken ct)
            {
                if (Throw != null) throw Throw;
                return Task.FromResult(Latest);
            }

            public Task DownloadAsync(string url, string destinationPath, IProgress<int> percent, CancellationToken ct)
            {
                File.Copy(ZipPath, destinationPath, true);
                percent?.Report(100);
                return Task.CompletedTask;
            }
        }

        private sealed class LowDiskFs : FileSystemImpl, IFileSystem
        {
            public new long GetAvailableDiskSpace(string path) => 10;
        }

        private sealed class StubHandler : HttpMessageHandler
        {
            private readonly HttpStatusCode _status;
            private readonly string _body;
            public HttpRequestMessage LastRequest;

            public StubHandler(HttpStatusCode status, string body) { _status = status; _body = body; }

            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
            {
                LastRequest = request;
                return Task.FromResult(new HttpResponseMessage(_status) { Content = new StringContent(_body) });
            }
        }
    }
}
