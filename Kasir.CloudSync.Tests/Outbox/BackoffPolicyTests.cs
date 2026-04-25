using System;
using NUnit.Framework;
using FluentAssertions;
using Kasir.CloudSync.Outbox;

namespace Kasir.CloudSync.Tests.Outbox
{
    [TestFixture]
    public class BackoffPolicyTests
    {
        [Test]
        public void Zero_Failures_Returns_Base_Interval()
        {
            BackoffPolicy.Delay(0).TotalSeconds.Should().Be(BackoffPolicy.DefaultBaseIntervalSeconds);
        }

        [Test]
        public void Doubles_Each_Failure_Until_Max()
        {
            BackoffPolicy.Delay(1).TotalSeconds.Should().Be(60);
            BackoffPolicy.Delay(2).TotalSeconds.Should().Be(120);
            BackoffPolicy.Delay(3).TotalSeconds.Should().Be(240);
            BackoffPolicy.Delay(4).TotalSeconds.Should().Be(480);
        }

        [Test]
        public void Caps_At_MaxBackoffSeconds()
        {
            BackoffPolicy.Delay(5).TotalSeconds.Should().Be(BackoffPolicy.MaxBackoffSeconds);
            BackoffPolicy.Delay(20).TotalSeconds.Should().Be(BackoffPolicy.MaxBackoffSeconds);
            BackoffPolicy.Delay(1000).TotalSeconds.Should().Be(BackoffPolicy.MaxBackoffSeconds);
        }

        [Test]
        public void Negative_Failures_Treated_As_Zero()
        {
            BackoffPolicy.Delay(-3).TotalSeconds.Should().Be(BackoffPolicy.DefaultBaseIntervalSeconds);
        }

        [Test]
        public void Custom_BaseInterval_Honored()
        {
            BackoffPolicy.Delay(0, baseIntervalSeconds: 10).TotalSeconds.Should().Be(10);
            BackoffPolicy.Delay(2, baseIntervalSeconds: 10).TotalSeconds.Should().Be(40);
        }
    }
}
