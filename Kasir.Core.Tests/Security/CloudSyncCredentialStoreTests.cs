using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Kasir.Security;
using NUnit.Framework;

// Platform-specific asserts are guarded at runtime with Assert.Ignore, which the
// platform analyzer does not understand.
#pragma warning disable CA1416

namespace Kasir.Tests.Security
{
    /// <summary>
    /// F01: cloud sync Postgres credentials used to be written as plaintext JSON
    /// (%LOCALAPPDATA%\Kasir\cloudsync.json). They now go through ProtectedFile
    /// (DPAPI CurrentUser on Windows) with a transparent one-time migration.
    /// A fake protector stands in for DPAPI so this runs on macOS/Linux CI too.
    /// </summary>
    [TestFixture]
    [NonParallelizable]
    public class CloudSyncCredentialStoreTests
    {
        private string _dir = "";

        /// <summary>Reversible "encryption" with a header; rejects foreign bytes like DPAPI does.</summary>
        private sealed class FakeProtector : ISecretProtector
        {
            private static readonly byte[] Magic = Encoding.ASCII.GetBytes("FAKEDPAPI");
            public bool Encrypts => true;
            public byte[] Protect(byte[] plain) => Magic.Concat(plain.Select(b => (byte)(b ^ 0x5A))).ToArray();
            public byte[] Unprotect(byte[] stored)
            {
                if (stored.Length < Magic.Length || !stored.Take(Magic.Length).SequenceEqual(Magic))
                    throw new CryptographicException("The data is invalid.");
                return stored.Skip(Magic.Length).Select(b => (byte)(b ^ 0x5A)).ToArray();
            }
        }

        private sealed class FailingProtector : ISecretProtector
        {
            public bool Encrypts => true;
            public byte[] Protect(byte[] plain) => throw new CryptographicException("DPAPI unavailable");
            public byte[] Unprotect(byte[] stored) => throw new CryptographicException("DPAPI unavailable");
        }

        private static CloudSyncCreds Sample(string pwd = "s3cret-Pa55") => new CloudSyncCreds
        {
            Host = "aws-1-ap-southeast-1.pooler.supabase.com",
            Port = 6543,
            Database = "postgres",
            Username = "postgres.abcdefgh",
            Password = pwd,
        };

        private static void ShouldMatch(CloudSyncCreds actual, CloudSyncCreds expected)
        {
            actual.Should().NotBeNull();
            actual.Should().BeEquivalentTo(expected);
        }

        /// <summary>Exactly what the pre-F01 CloudSyncCredsService.Save wrote.</summary>
        private static void WriteLegacyPlaintext(CloudSyncCreds creds) =>
            File.WriteAllText(CloudSyncCredentialStore.LegacyPlaintextPath,
                JsonSerializer.Serialize(creds, new JsonSerializerOptions { WriteIndented = true }));

        [SetUp]
        public void SetUp()
        {
            _dir = Path.Combine(Path.GetTempPath(), "kasir-cs-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_dir);
            CloudSyncCredentialStore.DirectoryOverride = _dir;
            CloudSyncCredentialStore.ProtectorOverride = new FakeProtector();
        }

        [TearDown]
        public void TearDown()
        {
            CloudSyncCredentialStore.DirectoryOverride = null;
            CloudSyncCredentialStore.ProtectorOverride = null;
            try { Directory.Delete(_dir, true); } catch { }
        }

        [Test]
        public void Save_WritesEncryptedFile_PasswordNeverOnDiskInPlaintext()
        {
            CloudSyncCredentialStore.TrySave(Sample()).Should().BeTrue();

            File.Exists(CloudSyncCredentialStore.FilePath).Should().BeTrue("creds go to the protected file");
            File.Exists(CloudSyncCredentialStore.LegacyPlaintextPath).Should().BeFalse("no plaintext JSON is written any more");
            var allBytes = Directory.GetFiles(_dir).SelectMany(File.ReadAllBytes).ToArray();
            Encoding.UTF8.GetString(allBytes).Should().NotContain("s3cret-Pa55");
        }

        [Test]
        public void Save_Then_Load_RoundTrips()
        {
            CloudSyncCredentialStore.TrySave(Sample()).Should().BeTrue();

            ShouldMatch(CloudSyncCredentialStore.TryLoad(), Sample());
        }

        [Test]
        public void Save_OverwritesPreviousCreds()
        {
            CloudSyncCredentialStore.TrySave(Sample("old")).Should().BeTrue();
            CloudSyncCredentialStore.TrySave(Sample("new")).Should().BeTrue();

            CloudSyncCredentialStore.TryLoad().Password.Should().Be("new");
            Directory.GetFiles(_dir, "*.tmp").Should().BeEmpty();
        }

        [Test]
        public void Save_DeletesStaleLegacyPlaintext()
        {
            WriteLegacyPlaintext(Sample("old"));

            CloudSyncCredentialStore.TrySave(Sample("new")).Should().BeTrue();

            File.Exists(CloudSyncCredentialStore.LegacyPlaintextPath).Should().BeFalse();
            CloudSyncCredentialStore.TryLoad().Password.Should().Be("new");
        }

        [Test]
        public void Save_WhenEncryptionFails_ReturnsFalse_AndWritesNoPlaintext()
        {
            CloudSyncCredentialStore.ProtectorOverride = new FailingProtector();

            CloudSyncCredentialStore.TrySave(Sample()).Should().BeFalse();

            Directory.GetFiles(_dir).Should().BeEmpty();
        }

        [Test]
        public void Load_Missing_ReturnsNull()
        {
            CloudSyncCredentialStore.TryLoad().Should().BeNull();
        }

        [Test]
        public void Load_MigratesLegacyPlaintext_ToEncrypted_AndDeletesPlaintext()
        {
            WriteLegacyPlaintext(Sample());

            var loaded = CloudSyncCredentialStore.TryLoad();

            ShouldMatch(loaded, Sample());
            File.Exists(CloudSyncCredentialStore.LegacyPlaintextPath).Should().BeFalse("plaintext is removed after a verified migration");
            File.Exists(CloudSyncCredentialStore.FilePath).Should().BeTrue();
            var decrypted = ProtectedFile.Read(CloudSyncCredentialStore.FilePath, new FakeProtector());
            JsonSerializer.Deserialize<CloudSyncCreds>(decrypted).Should().BeEquivalentTo(Sample());
            Encoding.UTF8.GetString(File.ReadAllBytes(CloudSyncCredentialStore.FilePath)).Should().NotContain("s3cret-Pa55");

            // Second load reads the encrypted file only.
            ShouldMatch(CloudSyncCredentialStore.TryLoad(), Sample());
        }

        [Test]
        public void Load_MigrationEncryptionFails_KeepsPlaintext_AndStillReturnsCreds()
        {
            WriteLegacyPlaintext(Sample());
            CloudSyncCredentialStore.ProtectorOverride = new FailingProtector();

            var loaded = CloudSyncCredentialStore.TryLoad();

            ShouldMatch(loaded, Sample());
            File.Exists(CloudSyncCredentialStore.LegacyPlaintextPath).Should().BeTrue("never lose creds when encryption fails");
            File.Exists(CloudSyncCredentialStore.FilePath).Should().BeFalse();
        }

        [Test]
        public void Load_CorruptEncryptedFile_ReturnsNull_NeverThrows_AndKeepsFile()
        {
            File.WriteAllBytes(CloudSyncCredentialStore.FilePath, new byte[] { 1, 2, 3, 4 });

            CloudSyncCredentialStore.TryLoad().Should().BeNull();
            File.Exists(CloudSyncCredentialStore.FilePath).Should().BeTrue("an unreadable file is left for recovery, not silently deleted");
        }

        [Test]
        public void Load_EncryptedButNotJson_ReturnsNull()
        {
            ProtectedFile.Write(CloudSyncCredentialStore.FilePath, Encoding.UTF8.GetBytes("not json"), new FakeProtector());

            CloudSyncCredentialStore.TryLoad().Should().BeNull();
        }

        [Test]
        public void Load_CorruptLegacyPlaintext_ReturnsNull_AndKeepsIt()
        {
            File.WriteAllText(CloudSyncCredentialStore.LegacyPlaintextPath, "{ not json");

            CloudSyncCredentialStore.TryLoad().Should().BeNull();
            File.Exists(CloudSyncCredentialStore.LegacyPlaintextPath).Should().BeTrue();
        }

        [Test]
        public void Load_UndecryptableEncrypted_FallsBackToLegacyPlaintext()
        {
            File.WriteAllBytes(CloudSyncCredentialStore.FilePath, new byte[] { 9, 9, 9 });
            WriteLegacyPlaintext(Sample());

            ShouldMatch(CloudSyncCredentialStore.TryLoad(), Sample());
            File.Exists(CloudSyncCredentialStore.LegacyPlaintextPath).Should().BeFalse();
            ShouldMatch(CloudSyncCredentialStore.TryLoad(), Sample());
        }

        [Test]
        public void Load_BothPresent_EncryptedNewer_UsesEncrypted_AndRemovesPlaintext()
        {
            WriteLegacyPlaintext(Sample("stale"));
            File.SetLastWriteTimeUtc(CloudSyncCredentialStore.LegacyPlaintextPath, DateTime.UtcNow.AddHours(-2));
            ProtectedFile.Write(CloudSyncCredentialStore.FilePath,
                JsonSerializer.SerializeToUtf8Bytes(Sample("current")), new FakeProtector());

            CloudSyncCredentialStore.TryLoad().Password.Should().Be("current");
            File.Exists(CloudSyncCredentialStore.LegacyPlaintextPath).Should().BeFalse();
        }

        [Test]
        public void Load_BothPresent_PlaintextNewer_ReMigratesPlaintext()
        {
            // e.g. the register was rolled back to an older build, creds re-entered
            // there (plaintext), then upgraded again: the plaintext is the latest truth.
            ProtectedFile.Write(CloudSyncCredentialStore.FilePath,
                JsonSerializer.SerializeToUtf8Bytes(Sample("old")), new FakeProtector());
            File.SetLastWriteTimeUtc(CloudSyncCredentialStore.FilePath, DateTime.UtcNow.AddHours(-2));
            WriteLegacyPlaintext(Sample("re-entered"));

            CloudSyncCredentialStore.TryLoad().Password.Should().Be("re-entered");
            File.Exists(CloudSyncCredentialStore.LegacyPlaintextPath).Should().BeFalse();
            CloudSyncCredentialStore.TryLoad().Password.Should().Be("re-entered");
        }

        [Test]
        public void Delete_RemovesEncryptedAndLegacy()
        {
            CloudSyncCredentialStore.TrySave(Sample()).Should().BeTrue();
            WriteLegacyPlaintext(Sample());

            CloudSyncCredentialStore.TryDelete();

            CloudSyncCredentialStore.TryLoad().Should().BeNull();
            Directory.GetFiles(_dir).Should().BeEmpty();
        }

        [Test]
        public void PlatformDefault_OnUnix_IsPlain_AndFileIsOwnerOnly()
        {
            if (OperatingSystem.IsWindows()) Assert.Ignore("Unix file mode only");
            CloudSyncCredentialStore.ProtectorOverride = null;

            CloudSyncCredentialStore.IsEncrypted.Should().BeFalse();
            CloudSyncCredentialStore.TrySave(Sample()).Should().BeTrue();

            File.GetUnixFileMode(CloudSyncCredentialStore.FilePath)
                .Should().Be(UnixFileMode.UserRead | UnixFileMode.UserWrite);
            ShouldMatch(CloudSyncCredentialStore.TryLoad(), Sample());
        }

        [Test]
        public void PlatformDefault_OnUnix_MigratedFileIsOwnerOnly()
        {
            if (OperatingSystem.IsWindows()) Assert.Ignore("Unix file mode only");
            CloudSyncCredentialStore.ProtectorOverride = null;
            WriteLegacyPlaintext(Sample());
            File.SetUnixFileMode(CloudSyncCredentialStore.LegacyPlaintextPath,
                UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.GroupRead | UnixFileMode.OtherRead);

            ShouldMatch(CloudSyncCredentialStore.TryLoad(), Sample());

            File.Exists(CloudSyncCredentialStore.LegacyPlaintextPath).Should().BeFalse();
            File.GetUnixFileMode(CloudSyncCredentialStore.FilePath)
                .Should().Be(UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }

        [Test]
        public void PlatformDefault_OnWindows_IsDpapi_RoundTrips_AndIsNotPlaintext()
        {
            if (!OperatingSystem.IsWindows()) Assert.Ignore("DPAPI is Windows only");
            CloudSyncCredentialStore.ProtectorOverride = null;

            CloudSyncCredentialStore.IsEncrypted.Should().BeTrue();
            CloudSyncCredentialStore.TrySave(Sample()).Should().BeTrue();

            Encoding.UTF8.GetString(File.ReadAllBytes(CloudSyncCredentialStore.FilePath)).Should().NotContain("s3cret-Pa55");
            // Same call the README PowerShell one-liner makes.
            byte[] plain = ProtectedData.Unprotect(File.ReadAllBytes(CloudSyncCredentialStore.FilePath), null, DataProtectionScope.CurrentUser);
            JsonSerializer.Deserialize<CloudSyncCreds>(plain).Should().BeEquivalentTo(Sample());
            ShouldMatch(CloudSyncCredentialStore.TryLoad(), Sample());
        }
    }
}
