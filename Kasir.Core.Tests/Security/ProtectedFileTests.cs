using System;
using System.IO;
using System.Text;
using FluentAssertions;
using Kasir.Security;
using NUnit.Framework;

// Platform-specific asserts are guarded at runtime with Assert.Ignore, which the
// platform analyzer does not understand.
#pragma warning disable CA1416

namespace Kasir.Tests.Security
{
    [TestFixture]
    public class ProtectedFileTests
    {
        private string _dir = "";

        [SetUp]
        public void SetUp()
        {
            _dir = Path.Combine(Path.GetTempPath(), "kasir-pf-" + Guid.NewGuid().ToString("N"));
        }

        [TearDown]
        public void TearDown()
        {
            try { Directory.Delete(_dir, true); } catch { }
        }

        [Test]
        public void Write_CreatesDirectory_AndRoundTrips_WithoutLeavingTempFile()
        {
            string path = Path.Combine(_dir, "sub", "secret.dat");

            ProtectedFile.Write(path, Encoding.UTF8.GetBytes("hello"), PlainSecretProtector.Instance);

            Encoding.UTF8.GetString(ProtectedFile.Read(path, PlainSecretProtector.Instance)).Should().Be("hello");
            Directory.GetFiles(Path.GetDirectoryName(path)!, "*.tmp").Should().BeEmpty();
        }

        [Test]
        public void Write_OnUnix_TightensExistingWiderFileTo0600()
        {
            if (OperatingSystem.IsWindows()) Assert.Ignore("Unix file mode only");
            Directory.CreateDirectory(_dir);
            string path = Path.Combine(_dir, "secret.dat");
            File.WriteAllText(path, "old");
            File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.GroupRead | UnixFileMode.OtherRead);

            ProtectedFile.Write(path, Encoding.UTF8.GetBytes("new"), PlainSecretProtector.Instance);

            File.GetUnixFileMode(path).Should().Be(UnixFileMode.UserRead | UnixFileMode.UserWrite);
            File.ReadAllText(path).Should().Be("new");
        }

        [Test]
        public void Write_OnWindows_Dpapi_RoundTrips_AndIsNotPlaintext()
        {
            if (!OperatingSystem.IsWindows()) Assert.Ignore("DPAPI is Windows only");
            string path = Path.Combine(_dir, "secret.dat");
            var protector = SecretProtectors.PlatformDefault;
            protector.Encrypts.Should().BeTrue();

            ProtectedFile.Write(path, Encoding.UTF8.GetBytes("pw-xyz"), protector);

            Encoding.UTF8.GetString(File.ReadAllBytes(path)).Should().NotContain("pw-xyz");
            Encoding.UTF8.GetString(ProtectedFile.Read(path, protector)).Should().Be("pw-xyz");
        }

        [Test]
        public void PlatformDefault_OnUnix_DoesNotEncrypt()
        {
            if (OperatingSystem.IsWindows()) Assert.Ignore("Unix only");
            SecretProtectors.PlatformDefault.Encrypts.Should().BeFalse();
        }
    }
}
