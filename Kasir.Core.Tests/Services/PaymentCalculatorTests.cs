using System;
using NUnit.Framework;
using FluentAssertions;
using Kasir.Services;

namespace Kasir.Tests.Services
{
    [TestFixture]
    public class PaymentCalculatorTests
    {
        private PaymentCalculator _calc;

        [SetUp]
        public void SetUp()
        {
            _calc = new PaymentCalculator();
        }

        // Cash payment tests
        [TestCase(100000L, 100000L, 0L, Description = "Exact payment, no change")]
        [TestCase(150000L, 100000L, 50000L, Description = "Overpayment, Rp 500 change")]
        [TestCase(200000L, 187500L, 12500L, Description = "Rp 125 change")]
        [TestCase(1000000L, 65500L, 934500L, Description = "Large change")]
        public void CalculateChange_CashPayment(long tendered, long totalDue, long expectedChange)
        {
            _calc.CalculateChange(tendered, totalDue).Should().Be(expectedChange);
        }

        [Test]
        public void CalculateChange_InsufficientPayment_Throws()
        {
            Action act = () => _calc.CalculateChange(50000, 100000);
            act.Should().Throw<InvalidOperationException>();
        }

        [Test]
        public void CalculateChange_ZeroTotal_ZeroChange()
        {
            _calc.CalculateChange(0, 0).Should().Be(0);
        }

        // Card fee tests
        [TestCase(100000L, 250, 2500L, Description = "2.5% fee on Rp 1,000")]
        [TestCase(500000L, 300, 15000L, Description = "3% fee on Rp 5,000")]
        [TestCase(100000L, 0, 0L, Description = "0% fee")]
        public void CalculateCardFee_ReturnsCorrectFee(long amount, int feePctX100, long expectedFee)
        {
            _calc.CalculateCardFee(amount, feePctX100).Should().Be(expectedFee);
        }

        // Split payment tests
        [Test]
        public void ValidatePayment_CashOnly_Exact()
        {
            var result = _calc.ValidatePayment(100000, 100000, 0, 0);
            result.IsValid.Should().BeTrue();
            result.Change.Should().Be(0);
        }

        [Test]
        public void ValidatePayment_CashOnly_WithChange()
        {
            var result = _calc.ValidatePayment(100000, 150000, 0, 0);
            result.IsValid.Should().BeTrue();
            result.Change.Should().Be(50000);
        }

        [Test]
        public void ValidatePayment_CashOnly_Insufficient()
        {
            var result = _calc.ValidatePayment(100000, 50000, 0, 0);
            result.IsValid.Should().BeFalse();
        }

        [Test]
        public void ValidatePayment_SplitCashAndCard()
        {
            var result = _calc.ValidatePayment(100000, 50000, 50000, 0);
            result.IsValid.Should().BeTrue();
            result.Change.Should().Be(0);
        }

        [Test]
        public void ValidatePayment_VoucherCoversAll()
        {
            var result = _calc.ValidatePayment(100000, 0, 0, 100000);
            result.IsValid.Should().BeTrue();
            result.Change.Should().Be(0);
        }

        [Test]
        public void ValidatePayment_VoucherPartialPlusCash()
        {
            var result = _calc.ValidatePayment(100000, 60000, 0, 50000);
            result.IsValid.Should().BeTrue();
            result.Change.Should().Be(10000);
        }

        [Test]
        public void ValidatePayment_AllThreeTypes()
        {
            // Rp 1000 total: Rp 300 voucher + Rp 400 card + Rp 400 cash = Rp 1100 → Rp 100 change
            var result = _calc.ValidatePayment(100000, 40000, 40000, 30000);
            result.IsValid.Should().BeTrue();
            result.Change.Should().Be(10000);
        }

        // Loyalty points tests
        [TestCase(1000000L, 1, Description = "Rp 10,000 = 1 sticker")]
        [TestCase(5000000L, 5, Description = "Rp 50,000 = 5 stickers")]
        [TestCase(9999900L, 9, Description = "Rp 99,999 = 9 stickers (floor)")]
        [TestCase(500000L, 0, Description = "Rp 5,000 = 0 stickers (below threshold)")]
        [TestCase(0L, 0, Description = "Rp 0 = 0 stickers")]
        public void CalculateLoyaltyPoints_Correct(long totalCents, int expectedPoints)
        {
            _calc.CalculateLoyaltyPoints(totalCents).Should().Be(expectedPoints);
        }
    }
}
