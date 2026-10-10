using System;
using FluentAssertions;
using NUnit.Framework;

namespace Kasir.CloudSync.Tests
{
    // D26: one configurable store zone, default WITA (Asia/Makassar, UTC+08:00).
    [TestFixture]
    public class StoreTimeZoneTests
    {
        [TearDown]
        public void RestoreZone() => StoreTimeZone.Reset();

        [Test]
        public void DefaultsToWita()
        {
            StoreTimeZone.Id.Should().Be("Asia/Makassar");
            StoreTimeZone.Offset.Should().Be(TimeSpan.FromHours(8));
            new CloudSyncConfig().StoreTimeZone.Should().Be("Asia/Makassar");
        }

        [TestCase("Asia/Makassar", 8)]
        [TestCase("asia/makassar", 8)]
        [TestCase("Asia/Jakarta", 7)]
        [TestCase("Asia/Pontianak", 7)]
        [TestCase("Asia/Jayapura", 9)]
        [TestCase("SE Asia Standard Time", 7)]
        [TestCase("Singapore Standard Time", 8)]
        public void ResolvesIndonesianZones(string id, int hours)
        {
            StoreTimeZone.TryResolve(id, out var offset).Should().BeTrue();
            offset.Should().Be(TimeSpan.FromHours(hours));
        }

        [TestCase(null)]
        [TestCase("")]
        [TestCase("Asia/Makasar")]
        [TestCase("Europe/Amsterdam")]
        public void RejectsBlankUnknownOrDstZones(string id)
        {
            StoreTimeZone.TryResolve(id, out _).Should().BeFalse();
        }

        [Test]
        public void Configure_BlankKeepsDefault_UnknownThrows()
        {
            StoreTimeZone.Configure("Asia/Jakarta");
            StoreTimeZone.Offset.Should().Be(TimeSpan.FromHours(7));
            StoreTimeZone.Configure("  ");
            StoreTimeZone.Id.Should().Be("Asia/Makassar");

            Action bad = () => StoreTimeZone.Configure("Asia/Makasar");
            bad.Should().Throw<ArgumentException>().WithMessage("*StoreTimeZone*Asia/Makasar*");
            StoreTimeZone.Id.Should().Be("Asia/Makassar", "a bad id changes nothing");
        }

        [Test]
        public void WallClock_RollsTheDayAtWitaMidnight()
        {
            StoreTimeZone.WallClock(new DateTimeOffset(2026, 10, 9, 15, 59, 0, TimeSpan.Zero))
                .Should().Be(new DateTime(2026, 10, 9, 23, 59, 0));
            StoreTimeZone.WallClock(new DateTimeOffset(2026, 10, 9, 16, 30, 0, TimeSpan.Zero))
                .Should().Be(new DateTime(2026, 10, 10, 0, 30, 0));
            StoreTimeZone.WallClock(new DateTimeOffset(2026, 10, 9, 16, 30, 0, TimeSpan.Zero)).Kind
                .Should().Be(DateTimeKind.Unspecified);
        }

        [Test]
        public void WallClock_ClosingTime()
        {
            // 21:00 WITA closing = 13:00 UTC = 20:00 WIB.
            StoreTimeZone.WallClock(new DateTimeOffset(2026, 10, 9, 21, 0, 0, TimeSpan.FromHours(8)))
                .Should().Be(new DateTime(2026, 10, 9, 21, 0, 0));
            StoreTimeZone.WallClock(new DateTimeOffset(2026, 10, 9, 13, 0, 0, TimeSpan.Zero))
                .Should().Be(new DateTime(2026, 10, 9, 21, 0, 0));
        }

        [Test]
        public void ValidateWorkerConfig_RejectsAnUnknownZone()
        {
            var path = System.IO.Path.GetTempFileName();
            try
            {
                var cfg = new CloudSyncConfig
                {
                    SupabaseConnectionString = "Host=x",
                    KasirDbPath = path,
                    StoreTimeZone = "Asia/Makasar",
                };
                Program.ValidateWorkerConfig(cfg).Should().Contain("StoreTimeZone");
                cfg.StoreTimeZone = "Asia/Makassar";
                Program.ValidateWorkerConfig(cfg).Should().BeNull();
            }
            finally
            {
                System.IO.File.Delete(path);
            }
        }
    }
}
