using NUnit.Framework;
using FluentAssertions;
using Kasir.Utils;

namespace Kasir.Tests.Utils
{
    [TestFixture]
    public class FormattingTests
    {
        [TestCase(1500000, "Rp 15.000")]
        [TestCase(100, "Rp 1")]
        [TestCase(0, "Rp 0")]
        [TestCase(99999900, "Rp 999.999")]
        [TestCase(1000000000, "Rp 10.000.000")]
        public void FormatCurrency_ReturnsIndonesianFormat(long cents, string expected)
        {
            Formatting.FormatCurrency(cents).Should().Be(expected);
        }

        [TestCase(1500000, "15.000")]
        [TestCase(0, "0")]
        public void FormatCurrencyShort_NoRpPrefix(long cents, string expected)
        {
            Formatting.FormatCurrencyShort(cents).Should().Be(expected);
        }

        [TestCase("2026-04-04", "04-04-2026")]
        [TestCase("2026-12-31", "31-12-2026")]
        [TestCase("", "")]
        [TestCase(null, "")]
        public void FormatDate_ConvertsIsoToDdMmYyyy(string input, string expected)
        {
            Formatting.FormatDate(input).Should().Be(expected);
        }

        [Test]
        public void CurrentPeriod_ReturnsYyyyMm()
        {
            string period = Formatting.CurrentPeriod();
            period.Should().HaveLength(6);
            period.Should().MatchRegex("^[0-9]{6}$");
        }

        [Test]
        public void TodayIso_ReturnsYyyyMmDd()
        {
            string today = Formatting.TodayIso();
            today.Should().HaveLength(10);
            today.Should().MatchRegex("^[0-9]{4}-[0-9]{2}-[0-9]{2}$");
        }
    }
}
