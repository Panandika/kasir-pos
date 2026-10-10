using System;
using FluentAssertions;
using Kasir.CloudSync.Mappers;
using NUnit.Framework;

namespace Kasir.CloudSync.Tests.Mappers
{
    // WP-02: POS timestamps are WIB wall-clock text (datetime('now','localtime')).
    // They must reach Supabase as the right instant, or the dashboard's exact-time
    // opname rule (D11) is off by 7 hours.
    [TestFixture]
    public class DateParserTests
    {
        [Test]
        public void NaiveLocalTime_IsParsedAsWib()
        {
            var t = DateParser.TryParseIso("2026-10-09 09:00:00", out var warn);

            warn.Should().BeFalse();
            t.Should().Be(new DateTimeOffset(2026, 10, 9, 2, 0, 0, TimeSpan.Zero),
                "09:00 WIB is 02:00 UTC");
            t.Value.Offset.Should().Be(TimeSpan.Zero, "the mirror always ships UTC");
        }

        [Test]
        public void NaiveIsoT_AndMilliseconds_AreParsedAsWib()
        {
            DateParser.TryParseIso("2026-10-09T15:00:00", out _)
                .Should().Be(new DateTimeOffset(2026, 10, 9, 8, 0, 0, TimeSpan.Zero));
            DateParser.TryParseIso("2026-10-09T15:00:00.250", out _)
                .Should().Be(new DateTimeOffset(2026, 10, 9, 8, 0, 0, 250, TimeSpan.Zero));
            DateParser.TryParseIso("20261009 15:00:00", out _)
                .Should().Be(new DateTimeOffset(2026, 10, 9, 8, 0, 0, TimeSpan.Zero));
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
            DateParser.TryParseIso("2026-10-09T09:00:00+07:00", out _)
                .Should().Be(new DateTimeOffset(2026, 10, 9, 2, 0, 0, TimeSpan.Zero));
        }

        [Test]
        public void DateOnly_IsMidnightWib()
        {
            DateParser.TryParseIso("2026-10-09", out _)
                .Should().Be(new DateTimeOffset(2026, 10, 8, 17, 0, 0, TimeSpan.Zero));
            DateParser.TryParseIso("20261009", out _)
                .Should().Be(new DateTimeOffset(2026, 10, 8, 17, 0, 0, TimeSpan.Zero));
        }

        [Test]
        public void Result_DoesNotDependOnMachineTimeZone()
        {
            // Fixed +07:00, not TimeZoneInfo.Local: the hub may run with any OS zone.
            var a = DateParser.TryParseIso("2026-01-01 00:30:00", out _);
            a.Should().Be(new DateTimeOffset(2025, 12, 31, 17, 30, 0, TimeSpan.Zero));
        }

        [TestCase("2026-10-09 09:00:00", false)]
        [TestCase("2026-10-09", false)]
        [TestCase("20261009", false)]
        [TestCase("2026-10-09T09:00:00Z", true)]
        [TestCase("2026-10-09T09:00:00+07:00", true)]
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
