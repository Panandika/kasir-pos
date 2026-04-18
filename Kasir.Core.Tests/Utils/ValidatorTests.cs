using NUnit.Framework;
using FluentAssertions;
using Kasir.Utils;

namespace Kasir.Tests.Utils
{
    [TestFixture]
    public class ValidatorTests
    {
        [TestCase("1234", true)]
        [TestCase("0007916248023", true)]
        [TestCase("ABCD1234", true)]
        [TestCase("12345678901234567890", true)]
        [TestCase("123", false)]
        [TestCase("", false)]
        [TestCase(null, false)]
        [TestCase("   ", false)]
        [TestCase("123456789012345678901", false)]
        [TestCase("ABC-123", false)]
        public void IsValidBarcode_ValidatesCorrectly(string code, bool expected)
        {
            Validators.IsValidBarcode(code).Should().Be(expected);
        }

        [TestCase("P001", true)]
        [TestCase("0010", true)]
        [TestCase("A", true)]
        [TestCase("", false)]
        [TestCase(null, false)]
        [TestCase("   ", false)]
        public void IsValidProductCode_ValidatesCorrectly(string code, bool expected)
        {
            Validators.IsValidProductCode(code).Should().Be(expected);
        }

        [TestCase(0L, true)]
        [TestCase(1500000L, true)]
        [TestCase(-1L, false)]
        [TestCase(-100L, false)]
        public void IsValidAmount_ValidatesCorrectly(long amount, bool expected)
        {
            Validators.IsValidAmount(amount).Should().Be(expected);
        }

        [TestCase("10", true)]
        [TestCase("100", true)]
        [TestCase("ABCDEF", true)]
        [TestCase("", false)]
        [TestCase(null, false)]
        [TestCase("ABCDEFG", false)]
        public void IsValidDeptCode_ValidatesCorrectly(string code, bool expected)
        {
            Validators.IsValidDeptCode(code).Should().Be(expected);
        }

        [TestCase("ADMIN", true)]
        [TestCase("A", true)]
        [TestCase("", false)]
        [TestCase(null, false)]
        [TestCase("   ", false)]
        public void IsValidUsername_ValidatesCorrectly(string username, bool expected)
        {
            Validators.IsValidUsername(username).Should().Be(expected);
        }
    }
}
