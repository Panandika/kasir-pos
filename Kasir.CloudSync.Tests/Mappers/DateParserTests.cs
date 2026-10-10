using System;
using FluentAssertions;
using Kasir.CloudSync.Mappers;
using NUnit.Framework;

namespace Kasir.CloudSync.Tests.Mappers
{
    // WP-02 / D26: POS timestamps are store wall-clock text (datetime('now','localtime'))
    // and the store runs on WITA (Asia/Makassar, UTC+08:00). They must reach Supabase
    // as the right instant, or the dashboard's exact-time opname rule (D11) is off.
    [TestFixture]
    public class DateParserTests
    {
        [TearDown]
        public void RestoreZone() => StoreTimeZone.Reset();

        [Test]
        public void NaiveLocalTime_IsParsedAsWita()
        {
            var t = DateParser.TryParseIso("2026-10-09 09:00:00", out var warn);

            warn.Should().BeFalse();
            t.Should().Be(new DateTimeOffset(2026, 10, 9, 1, 0, 0, TimeSpan.Zero),
                "09:00 WITA is 01:00 UTC");
            t.Value.Offset.Should().Be(TimeSpan.Zero, "the mirror always ships UTC");
        }

        [Test]
        public void NaiveIsoT_AndMilliseconds_AreParsedAsWita()
        {
            DateParser.TryParseIso("2026-10-09T15:00:00", out _)
                .Should().Be(new DateTimeOffset(2026, 10, 9, 7, 0, 0, TimeSpan.Zero));
            DateParser.TryParseIso("2026-10-09T15:00:00.250", out _)
                .Should().Be(new DateTimeOffset(2026, 10, 9, 7, 0, 0, 250, TimeSpan.Zero));
            DateParser.TryParseIso("20261009 15:00:00", out _)
                .Should().Be(new DateTimeOffset(2026, 10, 9, 7, 0, 0, TimeSpan.Zero));
        }

        [Test]
        public void ExplicitUtc_IsKept()
        {
            DateParser.TryParseIso("2026-10-09T02:00:00Z", out _)
                .Should().Be(new DateTimeOffset(2026, 10, 9, 2, 0, 0, TimeSpan.Zero));
            // Snapshot round trip shape (ReverseRowMapper emits yyyy-MM-ddTHH:mm:ss.fffK).
            DateParser.TryParseIso("2026-10-09T02:00:00.000Z", out _)
                .Should().Be(new DateTimeOffset(2026, 10, 9, 2, 0, 0, TimeSpan.Zero));
        }

        [Test]
        public void ExplicitOffset_IsKept()
        {
            DateParser.TryParseIso("2026-10-09T09:00:00+08:00", out _)
                .Should().Be(new DateTimeOffset(2026, 10, 9, 1, 0, 0, TimeSpan.Zero));
            DateParser.TryParseIso("2026-10-09T09:00:00+07:00", out _)
                .Should().Be(new DateTimeOffset(2026, 10, 9, 2, 0, 0, TimeSpan.Zero),
                    "a value that carries its own offset is not re-read as store time");
        }

        [Test]
        public void DateOnly_IsMidnightWita()
        {
            DateParser.TryParseIso("2026-10-09", out _)
                .Should().Be(new DateTimeOffset(2026, 10, 8, 16, 0, 0, TimeSpan.Zero));
            DateParser.TryParseIso("20261009", out _)
                .Should().Be(new DateTimeOffset(2026, 10, 8, 16, 0, 0, TimeSpan.Zero));
        }

        [Test]
        public void Result_DoesNotDependOnMachineTimeZone()
        {
            // Fixed +08:00, not TimeZoneInfo.Local: the hub may run with any OS zone.
            var a = DateParser.TryParseIso("2026-01-01 00:30:00", out _);
            a.Should().Be(new DateTimeOffset(2025, 12, 31, 16, 30, 0, TimeSpan.Zero));
        }

        [Test]
        public void AroundMidnight_StaysOnTheStoreDay()
        {
            // 00:30 WITA on 10 Oct is 16:30 UTC on 9 Oct (and only 23:30 in WIB).
            DateParser.TryParseIso("2026-10-10 00:30:00", out _)
                .Should().Be(new DateTimeOffset(2026, 10, 9, 16, 30, 0, TimeSpan.Zero));
            // 23:59:59 WITA on 9 Oct is 15:59:59 UTC.
            DateParser.TryParseIso("2026-10-09 23:59:59", out _)
                .Should().Be(new DateTimeOffset(2026, 10, 9, 15, 59, 59, TimeSpan.Zero));
        }

        [Test]
        public void ClosingTime_2100Wita_Is1300Utc()
        {
            DateParser.TryParseIso("2026-10-09 21:00:00", out _)
                .Should().Be(new DateTimeOffset(2026, 10, 9, 13, 0, 0, TimeSpan.Zero));
        }

        [Test]
        public void FollowsTheConfiguredStoreZone()
        {
            StoreTimeZone.Configure("Asia/Jakarta");
            DateParser.TryParseIso("2026-10-09 09:00:00", out _)
                .Should().Be(new DateTimeOffset(2026, 10, 9, 2, 0, 0, TimeSpan.Zero));
        }

        [TestCase("2026-10-09 09:00:00", false)]
        [TestCase("2026-10-09", false)]
        [TestCase("20261009", false)]
        [TestCase("2026-10-09T09:00:00Z", true)]
        [TestCase("2026-10-09T09:00:00+08:00", true)]
        [TestCase("2026-10-09T09:00:00-03:00", true)]
        public void HasExplicitOffset(string raw, bool expected)
        {
            DateParser.HasExplicitOffset(raw).Should().Be(expected);
        }

        [Test]
        public void Garbage_StillWarns()
        {
            DateParser.TryParseIso("09/10/2026", out var warn).Should().BeNull();
            warn.Should().BeTrue();
        }
    }
}
