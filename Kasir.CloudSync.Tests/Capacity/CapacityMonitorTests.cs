using NUnit.Framework;
using FluentAssertions;
using Kasir.CloudSync.Capacity;

namespace Kasir.CloudSync.Tests.Capacity
{
    [TestFixture]
    public class CapacityMonitorTests
    {
        [Test]
        public void ProjectedDaysUntilCeiling_Zero_Growth_Returns_MaxValue()
        {
            CapacityMonitor.ProjectedDaysUntilCeiling(currentMb: 100, mbPerWeek: 0)
                .Should().Be(int.MaxValue);
        }

        [Test]
        public void ProjectedDaysUntilCeiling_Beyond_Ceiling_Returns_Zero()
        {
            CapacityMonitor.ProjectedDaysUntilCeiling(currentMb: 600, mbPerWeek: 5)
                .Should().Be(0);
        }

        [Test]
        public void ProjectedDaysUntilCeiling_Realistic_Growth()
        {
            // 100 MB used, 1 MB/week growth -> 400 MB headroom -> 400 weeks * 7 = 2800 days
            CapacityMonitor.ProjectedDaysUntilCeiling(currentMb: 100, mbPerWeek: 1)
                .Should().BeGreaterThan(2700)
                .And.BeLessThan(2900);
        }

        [Test]
        public void ProjectedDaysUntilCeiling_Triggers_Warning_When_Under_90()
        {
            // 480 MB used, 5 MB/week -> 20 MB headroom -> 4 weeks -> 28 days
            var days = CapacityMonitor.ProjectedDaysUntilCeiling(currentMb: 480, mbPerWeek: 5);
            days.Should().BeLessThan(CapacityMonitor.ProjectedDaysWarning);
        }

        [Test]
        public void BytesToMb_Round_Trip()
        {
            CapacityMonitor.BytesToMb(1024L * 1024L).Should().Be(1L);
            CapacityMonitor.BytesToMb(500L * 1024L * 1024L).Should().Be(500L);
        }
    }
}
