using System;
using System.IO;
using FluentAssertions;
using Kasir.Help.Auth;
using NUnit.Framework;

namespace Kasir.Tests.Help
{
    /// <summary>
    /// Bantuan machine login now arrives via cloud pairing (MachineCredentialStore)
    /// instead of MachineEmail/MachinePassword in the public release zip's help.json.
    /// </summary>
    [TestFixture]
    public class MachineCredentialsTests
    {
        private string _dir = "";

        [SetUp]
        public void SetUp()
        {
            _dir = Path.Combine(Path.GetTempPath(), "kasir-mc-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_dir);
            MachineCredentialStore.PathOverride = Path.Combine(_dir, "machine-credentials.json");
            HelpConfigLoader.HelpJsonPathOverride = Path.Combine(_dir, "help.json");
        }

        [TearDown]
        public void TearDown()
        {
            MachineCredentialStore.PathOverride = null;
            HelpConfigLoader.HelpJsonPathOverride = null;
            try { Directory.Delete(_dir, true); } catch { }
        }

        private void WriteHelpJson(string json) =>
            File.WriteAllText(HelpConfigLoader.HelpJsonPathOverride!, json);

        private const string PublicHelpJson =
            "{\"SupabaseUrl\":\"https://x.supabase.co\",\"AnonKey\":\"anon\",\"StoreId\":\"sinar-makmur\",\"RegisterId\":\"01\"}";

        [Test]
        public void Store_RoundTrips_Credentials()
        {
            var creds = new MachineCredentials("register-02@sinar-makmur.local", "pw-123", "sinar-makmur", "02");

            MachineCredentialStore.TrySave(creds).Should().BeTrue();

            MachineCredentialStore.TryLoad().Should().Be(creds);
        }

        [Test]
        public void Store_Missing_Or_Corrupt_ReturnsNull_NeverThrows()
        {
            MachineCredentialStore.TryLoad().Should().BeNull();
            File.WriteAllText(MachineCredentialStore.PathOverride!, "not json");
            MachineCredentialStore.TryLoad().Should().BeNull();
        }

        [Test]
        public void Store_RejectsEmptyPassword()
        {
            MachineCredentialStore.TrySave(new MachineCredentials("a@b", "", "s", "01")).Should().BeFalse();
            File.Exists(MachineCredentialStore.PathOverride!).Should().BeFalse();
        }

        [Test]
        public void Store_OnUnix_FileIsOwnerOnly()
        {
            if (OperatingSystem.IsWindows()) Assert.Ignore("Unix file mode only");
            MachineCredentialStore.TrySave(new MachineCredentials("a@b", "pw", "s", "01"));

            File.GetUnixFileMode(MachineCredentialStore.PathOverride!)
                .Should().Be(UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }

        [Test]
        public void PublicOnlyHelpJson_WithoutPairing_GivesNoConfig()
        {
            WriteHelpJson(PublicHelpJson);

            HelpConfigLoader.TryLoad().Should().BeNull("no machine login yet: register not paired");
            HelpConfigLoader.TryReadOptional("SupabaseUrl").Should().Be("https://x.supabase.co",
                "cloud import must still find the server address without a machine login");
        }

        [Test]
        public void PairedCredentials_AreUsed_WithPublicHelpJson()
        {
            WriteHelpJson(PublicHelpJson);
            MachineCredentialStore.TrySave(new MachineCredentials("register-03@sinar-makmur.local", "fresh-pw", "sinar-makmur", "03"));

            var cfg = HelpConfigLoader.TryLoad();

            cfg.Should().NotBeNull();
            cfg!.MachineEmail.Should().Be("register-03@sinar-makmur.local");
            cfg.MachinePassword.Should().Be("fresh-pw");
            cfg.RegisterId.Should().Be("03", "paired register wins over the zip's RegisterId");
            cfg.SupabaseUrl.Should().Be("https://x.supabase.co");
        }

        [Test]
        public void PairedCredentials_WinOver_LegacyHelpJsonCredentials()
        {
            WriteHelpJson("{\"SupabaseUrl\":\"https://x.supabase.co\",\"AnonKey\":\"anon\",\"MachineEmail\":\"old@x\",\"MachinePassword\":\"old-pw\",\"RegisterId\":\"01\"}");
            MachineCredentialStore.TrySave(new MachineCredentials("register-01@sinar-makmur.local", "new-pw", "sinar-makmur", "01"));

            HelpConfigLoader.TryLoad()!.MachinePassword.Should().Be("new-pw");
        }

        [Test]
        public void LegacyHelpJsonCredentials_StillWork_WhenNotPaired()
        {
            WriteHelpJson("{\"SupabaseUrl\":\"https://x.supabase.co\",\"AnonKey\":\"anon\",\"MachineEmail\":\"old@x\",\"MachinePassword\":\"old-pw\",\"RegisterId\":\"01\"}");

            var cfg = HelpConfigLoader.TryLoad();

            cfg.Should().NotBeNull("existing installs keep working until they re-pair");
            cfg!.MachineEmail.Should().Be("old@x");
        }
    }
}
