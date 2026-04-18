using NUnit.Framework;
using Kasir.Utils;

namespace Kasir.Tests.Utils
{
    [TestFixture]
    public class AppVersionTests
    {
        [Test]
        public void IsNewerThan_NewerMajor_ReturnsTrue()
        {
            Assert.IsTrue(AppVersion.IsNewerThan("2027.01.1", "2026.04.1"));
        }

        [Test]
        public void IsNewerThan_NewerMinor_ReturnsTrue()
        {
            Assert.IsTrue(AppVersion.IsNewerThan("2026.05.1", "2026.04.1"));
        }

        [Test]
        public void IsNewerThan_NewerBuild_ReturnsTrue()
        {
            Assert.IsTrue(AppVersion.IsNewerThan("2026.04.2", "2026.04.1"));
        }

        [Test]
        public void IsNewerThan_SameVersion_ReturnsFalse()
        {
            Assert.IsFalse(AppVersion.IsNewerThan("2026.04.1", "2026.04.1"));
        }

        [Test]
        public void IsNewerThan_OlderVersion_ReturnsFalse()
        {
            Assert.IsFalse(AppVersion.IsNewerThan("2026.03.1", "2026.04.1"));
        }

        [Test]
        public void IsNewerThan_MalformedCandidate_ReturnsFalse()
        {
            Assert.IsFalse(AppVersion.IsNewerThan("abc", "2026.04.1"));
        }

        [Test]
        public void IsNewerThan_EmptyCandidate_ReturnsFalse()
        {
            Assert.IsFalse(AppVersion.IsNewerThan("", "2026.04.1"));
        }

        [Test]
        public void IsNewerThan_NullCandidate_ReturnsFalse()
        {
            Assert.IsFalse(AppVersion.IsNewerThan(null, "2026.04.1"));
        }

        [Test]
        public void IsNewerThan_TooFewParts_ReturnsFalse()
        {
            Assert.IsFalse(AppVersion.IsNewerThan("2026.04", "2026.04.1"));
        }

        [Test]
        public void IsNewerThan_NonNumericPart_ReturnsFalse()
        {
            Assert.IsFalse(AppVersion.IsNewerThan("2026.04.x", "2026.04.1"));
        }

        [Test]
        public void Current_ReturnsNonEmptyString()
        {
            string version = AppVersion.Current;
            Assert.IsNotNull(version);
            Assert.IsNotEmpty(version);
        }
    }
}
