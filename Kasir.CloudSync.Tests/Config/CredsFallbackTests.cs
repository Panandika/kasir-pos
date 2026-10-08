using System;
using System.IO;
using System.Text.Json;
using FluentAssertions;
using Kasir.Security;
using NUnit.Framework;

namespace Kasir.CloudSync.Tests.Config
{
    /// <summary>
    /// F01: the worker's lowest-priority config source must read the same
    /// encrypted store the POS setup screen writes (and migrate an old plaintext
    /// cloudsync.json), not the plaintext file directly.
    /// </summary>
    [TestFixture]
    [NonParallelizable]
    public class CredsFallbackTests
    {
        private string _dir = "";

        [SetUp]
        public void SetUp()
        {
            _dir = Path.Combine(Path.GetTempPath(), "kasir-cw-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_dir);
            CloudSyncCredentialStore.DirectoryOverride = _dir;
        }

        [TearDown]
        public void TearDown()
        {
            CloudSyncCredentialStore.DirectoryOverride = null;
            try { Directory.Delete(_dir, true); } catch { }
        }

        private static CloudSyncCreds Sample() => new CloudSyncCreds
        {
            Host = "pooler.example.com", Port = 6543, Database = "postgres",
            Username = "postgres.ref", Password = "pw-1",
        };

        [Test]
        public void ReadsCredsFromProtectedStore()
        {
            CloudSyncCredentialStore.TrySave(Sample()).Should().BeTrue();

            Program.TryBuildConnFromCredsJson().Should().Be(
                "Host=pooler.example.com;Port=6543;Database=postgres;Username=postgres.ref;Password=pw-1;SslMode=Require");
        }

        [Test]
        public void MigratesLegacyPlaintextFile()
        {
            File.WriteAllText(CloudSyncCredentialStore.LegacyPlaintextPath, JsonSerializer.Serialize(Sample()));

            Program.TryBuildConnFromCredsJson().Should().Contain("Password=pw-1");
            File.Exists(CloudSyncCredentialStore.LegacyPlaintextPath).Should().BeFalse();
            File.Exists(CloudSyncCredentialStore.FilePath).Should().BeTrue();
        }

        [Test]
        public void MissingOrIncomplete_ReturnsEmpty()
        {
            Program.TryBuildConnFromCredsJson().Should().BeEmpty();

            var c = Sample();
            c.Password = "";
            CloudSyncCredentialStore.TrySave(c).Should().BeTrue();
            Program.TryBuildConnFromCredsJson().Should().BeEmpty();
        }
    }
}
