using NUnit.Framework;
using FluentAssertions;
using Kasir.Utils;

namespace Kasir.Tests.Utils
{
    [TestFixture]
    public class IndonesianMoneyFormatterTests
    {
        [TestCase(0L, "0")]
        [TestCase(1L, "1")]
        [TestCase(999L, "999")]
        [TestCase(1000L, "1.000")]
        [TestCase(1250000L, "1.250.000")]
        [TestCase(70000L, "70.000")]
        [TestCase(1000000000L, "1.000.000.000")]
        public void Format_ProducesIndonesianThousands(long whole, string expected)
        {
            IndonesianMoneyFormatter.Format(whole).Should().Be(expected);
        }

        [TestCase("1250000", "1.250.000")]
        [TestCase("70000", "70.000")]
        [TestCase("0", "0")]
        [TestCase("", "")]
        [TestCase(null, "")]
        [TestCase("1.250.000", "1.250.000")]
        [TestCase("1,250,000", "1.250.000")]
        [TestCase("abc7000xyz", "7.000")]
        [TestCase("0070000", "70.000")]
        public void FormatText_StripsNonDigitsAndAddsSeparators(string input, string expected)
        {
            IndonesianMoneyFormatter.FormatText(input).Should().Be(expected);
        }

        [TestCase("0", true)]
        [TestCase("70000", true)]
        [TestCase("", false)]
        [TestCase(null, false)]
        [TestCase("70.000", false)]
        [TestCase("70+", false)]
        [TestCase("abc", false)]
        [TestCase("7a0", false)]
        public void IsDigitsOnly_DetectsPureDigits(string input, bool expected)
        {
            IndonesianMoneyFormatter.IsDigitsOnly(input).Should().Be(expected);
        }

        [TestCase("70000", "70000")]
        [TestCase("1.250.000", "1250000")]
        [TestCase("abc70000xyz", "70000")]
        public void DigitsOnly_StripsEverythingExceptDigits(string input, string expected)
        {
            IndonesianMoneyFormatter.DigitsOnly(input).Should().Be(expected);
        }

        // Caret-preservation: count digits-from-right on input, then expect
        // the new caret to land such that the same number of digits sit to its
        // right in the formatted output.
        [Test]
        public void ReformatPreserveCaret_CaretAtEnd_StaysAtEnd()
        {
            var (formatted, caret) = IndonesianMoneyFormatter.ReformatPreserveCaret("70000", 5);
            formatted.Should().Be("70.000");
            caret.Should().Be(6);
        }

        [Test]
        public void ReformatPreserveCaret_TypingDigitInMiddle_PreservesDigitsToRight()
        {
            // "1.250.000" caret at index 1 (just after the "1") has 6 digits
            // to the right. After reformat, the caret should land such that
            // the same 6 digits remain to its right.
            var (formatted, caret) = IndonesianMoneyFormatter.ReformatPreserveCaret("1.250.000", 1);
            formatted.Should().Be("1.250.000");
            int digitsRight = 0;
            for (int i = caret; i < formatted.Length; i++)
                if (formatted[i] >= '0' && formatted[i] <= '9') digitsRight++;
            digitsRight.Should().Be(6);
        }

        [Test]
        public void ReformatPreserveCaret_AppendsThousandsSeparator_CaretShiftsRight()
        {
            // Going from "1000" caret=4 (end) to "1.000" — caret should still be at end.
            var (formatted, caret) = IndonesianMoneyFormatter.ReformatPreserveCaret("1000", 4);
            formatted.Should().Be("1.000");
            caret.Should().Be(5);
        }

        [Test]
        public void ReformatPreserveCaret_EmptyInput_ReturnsEmpty()
        {
            var (formatted, caret) = IndonesianMoneyFormatter.ReformatPreserveCaret("", 0);
            formatted.Should().Be("");
            caret.Should().Be(0);
        }
    }
}
