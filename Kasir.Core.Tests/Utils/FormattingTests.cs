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

        [TestCase("100.000", true, 100000)]
        [TestCase("Rp 50.000", true, 50000)]
        [TestCase("1.000.000", true, 1000000)]
        [TestCase("0", true, 0)]
        [TestCase("abc", false, 0)]
        [TestCase("", false, 0)]
        [TestCase(null, false, 0)]
        public void TryParseRupiah_ParsesIndonesianFormat(string? input, bool expectedOk, long expectedVal)
        {
            bool ok = Formatting.TryParseRupiah(input, out long val);
            ok.Should().Be(expectedOk);
            val.Should().Be(expectedVal);
        }

        // Indonesian money input → cents: "." thousands, "," decimal (max 2 digits).
        [TestCase("11.208,67", true, 1120867)]
        [TestCase("1958,5", true, 195850)]
        [TestCase("1958,05", true, 195805)]
        [TestCase("11208", true, 1120800)]
        [TestCase("11.208", true, 1120800)]
        [TestCase("Rp 1.250.000", true, 125000000)]
        [TestCase("1.250.000,00", true, 125000000)]
        [TestCase("50,000", true, 5000000)]        // comma-as-thousands habit, exactly 3 digits
        [TestCase("1,250,000", true, 125000000)]
        [TestCase("0", true, 0)]
        [TestCase("0,5", true, 50)]
        [TestCase("11.5", false, 0)]               // ambiguous: not a thousands group
        [TestCase("1.20.000", false, 0)]
        [TestCase("1,2345", false, 0)]             // more than 2 decimals
        [TestCase("1.000,000", false, 0)]
        [TestCase("12,34,56", false, 0)]
        [TestCase(",50", false, 0)]
        [TestCase("11.208,", true, 1120800)]       // trailing comma while typing = no sen
        [TestCase("Rp. 50.000", true, 5000000)]
        [TestCase("rp50.000", true, 5000000)]
        [TestCase("50.000,-", true, 5000000)]      // ",-" suffix as written on nota
        [TestCase(" 1.000 ", true, 100000)]
        [TestCase("1,5", true, 150)]
        [TestCase("100,00", true, 10000)]
        [TestCase("1.000.000,5", true, 100000050)]
        [TestCase("21.474.837", true, 2147483700L)]  // > int.MaxValue cents: the old (int)(price*100m) PO cast overflowed here (F30)
        [TestCase("1.500.000.000", true, 150000000000L)]
        [TestCase("-5.000", true, -500000)]
        [TestCase("1rp000", false, 0)]             // "Rp" only as a prefix
        [TestCase("11.208,67,", false, 0)]
        [TestCase("99999999999999999999", false, 0)]
        [TestCase("abc", false, 0)]
        [TestCase("", false, 0)]
        [TestCase(null, false, 0)]
        public void TryParseRupiahCents_ParsesIndonesianDecimals(string? input, bool expectedOk, long expectedCents)
        {
            bool ok = Formatting.TryParseRupiahCents(input, out long cents);
            ok.Should().Be(expectedOk);
            cents.Should().Be(expectedCents);
        }

        [TestCase("11.208,67", false)]   // whole-rupiah fields reject sen instead of reading 100x
        [TestCase("11.208,00", true)]
        public void TryParseRupiah_RejectsSen(string input, bool expectedOk)
        {
            Formatting.TryParseRupiah(input, out _).Should().Be(expectedOk);
        }

        [TestCase(1120867, "11.208,67")]
        [TestCase(1120800, "11.208")]
        [TestCase(195850, "1.958,50")]
        [TestCase(0, "0")]
        public void FormatRupiahCentsInput_RoundTrips(long cents, string expected)
        {
            Formatting.FormatRupiahCentsInput(cents).Should().Be(expected);
            Formatting.TryParseRupiahCents(expected, out long back).Should().BeTrue();
            back.Should().Be(cents);
        }

        [TestCase(100000, "100.000")]
        [TestCase(0, "0")]
        [TestCase(1000000, "1.000.000")]
        public void FormatRupiahInput_RoundTrips(long rupiah, string expected)
        {
            Formatting.FormatRupiahInput(rupiah).Should().Be(expected);
        }
    }
}
