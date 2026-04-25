using NUnit.Framework;
using FluentAssertions;

namespace Kasir.CloudSync.Tests
{
    // Proves the Kasir.CloudSync.Tests project builds, references the CloudSync
    // assembly, and the test host runs. Replaced with real mapper / sink /
    // outbox tests in US-A2.
    [TestFixture]
    public class SkeletonSmokeTests
    {
        [Test]
        public void CloudSyncConfig_Has_Sensible_Defaults()
        {
            var cfg = new CloudSyncConfig();
            cfg.PollIntervalSeconds.Should().Be(30);
            cfg.BatchSize.Should().Be(100);
            cfg.SupabaseConnectionString.Should().BeNull();
            cfg.R2Bucket.Should().BeNull();
            cfg.KasirDbPath.Should().BeNull();
        }

        [Test]
        public void CloudSyncConfig_Section_Name_Is_CloudSync()
        {
            CloudSyncConfig.SectionName.Should().Be("CloudSync");
        }
    }
}
