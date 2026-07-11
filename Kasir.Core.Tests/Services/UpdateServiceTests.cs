using System;
using System.IO;
using Microsoft.Data.Sqlite;
using NUnit.Framework;
using FluentAssertions;
using Kasir.Services;
using Kasir.Tests.TestHelpers;
using Kasir.Tests.TestHelpers.Fakes;
using Kasir.Data.Repositories;
using Kasir.Utils;

namespace Kasir.Tests.Services
{
    [TestFixture]
    public class UpdateServiceTests
    {
        private SqliteConnection _db;
        private FakeFileSystem _fs;
        private UpdateService _sut;

        [SetUp]
        public void SetUp()
        {
            _db = TestDb.Create();
            _fs = new FakeFileSystem();

            // Set up config
            var config = new ConfigRepository(_db);
            config.Set("update_share", @"\\SERVER\updates\latest");
            config.Set("sync_hmac_key", "test-key-123");

            _sut = new UpdateService(_db, _fs, 5000);
        }

        [TearDown]
        public void TearDown()
        {
            _db.Dispose();
        }

        [Test]
        public void CheckForUpdate_ShareUnreachable_ReturnsError()
        {
            // No files in fake FS — share is "unreachable"
            var result = _sut.CheckForUpdate();

            Assert.IsFalse(result.Available);
            Assert.IsNotNull(result.Error);
            Assert.That(result.Error, Does.Contain("terhubung"));
        }

        [Test]
        public void CheckForUpdate_SameVersion_ReturnsNotAvailable()
        {
            _fs.AddDirectory(@"\\SERVER\updates\latest");
            _fs.AddFile(@"\\SERVER\updates\latest\version.txt", "2026.04.1");

            var result = _sut.CheckForUpdate();

            Assert.IsFalse(result.Available);
            Assert.IsNull(result.Error);
        }

        [Test]
        public void CheckForUpdate_NewerVersion_ReturnsAvailable()
        {
            _fs.AddDirectory(@"\\SERVER\updates\latest");
            _fs.AddFile(@"\\SERVER\updates\latest\version.txt", "2026.04.2");

            var result = _sut.CheckForUpdate();

            Assert.IsTrue(result.Available);
            Assert.AreEqual("2026.04.2", result.NewVersion);
        }

        [Test]
        public void CheckForUpdate_OlderVersion_ReturnsNotAvailable()
        {
            _fs.AddDirectory(@"\\SERVER\updates\latest");
            _fs.AddFile(@"\\SERVER\updates\latest\version.txt", "2025.01.1");

            var result = _sut.CheckForUpdate();

            Assert.IsFalse(result.Available);
        }

        [Test]
        public void GetPatchNotes_FileExists_ReturnsContent()
        {
            _fs.AddDirectory(@"\\SERVER\updates\latest");
            _fs.AddFile(@"\\SERVER\updates\latest\whatsnew.txt", "Bug fixes and improvements");

            string notes = _sut.GetPatchNotes();

            Assert.AreEqual("Bug fixes and improvements", notes);
        }

        [Test]
        public void GetPatchNotes_NoFile_ReturnsEmpty()
        {
            string notes = _sut.GetPatchNotes();

            Assert.AreEqual("", notes);
        }

        [Test]
        public void VerifyChecksumHmac_ValidHmac_ReturnsTrue()
        {
            string checksumContent = "abc123  Kasir.exe";
            string hmac = UpdateService.ComputeHmac(checksumContent, "test-key-123");

            _fs.AddDirectory(@"\\SERVER\updates\latest");
            _fs.AddFile(@"\\SERVER\updates\latest\checksum.sha256", checksumContent);
            _fs.AddFile(@"\\SERVER\updates\latest\checksum.sha256.hmac", hmac);

            bool valid = _sut.VerifyChecksumHmac(@"\\SERVER\updates\latest");

            Assert.IsTrue(valid);
        }

        [Test]
        public void VerifyChecksumHmac_InvalidHmac_ReturnsFalse()
        {
            _fs.AddDirectory(@"\\SERVER\updates\latest");
            _fs.AddFile(@"\\SERVER\updates\latest\checksum.sha256", "abc123  Kasir.exe");
            _fs.AddFile(@"\\SERVER\updates\latest\checksum.sha256.hmac", "wrong-hmac-value");

            bool valid = _sut.VerifyChecksumHmac(@"\\SERVER\updates\latest");

            Assert.IsFalse(valid);
        }

        // F23: VerifyChecksums must reject a file that is physically present but NOT in the
        // signed manifest, otherwise an attacker who writes to the update share could plant
        // a malicious DLL that skips verification and is deployed — remote code execution.
        // Uses a real temp dir because ComputeFileSha256 reads the real filesystem.
        [Test]
        public void VerifyChecksums_UnlistedPlantedFile_ReturnsFalse()
        {
            string dir = Path.Combine(Path.GetTempPath(), "kasir_upd_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            try
            {
                var sut = new UpdateService(_db, new FileSystemImpl(), 5000);

                string appFile = Path.Combine(dir, "Kasir.exe");
                File.WriteAllText(appFile, "legit-binary");
                string hash = UpdateService.ComputeFileSha256(appFile);
                File.WriteAllText(Path.Combine(dir, "checksum.sha256"), hash + "  Kasir.exe");

                sut.VerifyChecksums(dir).Should().BeTrue("only manifest-listed files are present");

                // Attacker plants an unlisted file.
                File.WriteAllText(Path.Combine(dir, "evil.dll"), "malicious");
                sut.VerifyChecksums(dir).Should().BeFalse("an unlisted file must fail verification");
            }
            finally
            {
                Directory.Delete(dir, true);
            }
        }

        [Test]
        public void ComputeHmac_Deterministic()
        {
            string hmac1 = UpdateService.ComputeHmac("test content", "key");
            string hmac2 = UpdateService.ComputeHmac("test content", "key");

            Assert.AreEqual(hmac1, hmac2);
        }

        [Test]
        public void ComputeHmac_DifferentKey_DifferentResult()
        {
            string hmac1 = UpdateService.ComputeHmac("test content", "key1");
            string hmac2 = UpdateService.ComputeHmac("test content", "key2");

            Assert.AreNotEqual(hmac1, hmac2);
        }
    }
}
