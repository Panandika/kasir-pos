using NUnit.Framework;
using FluentAssertions;
using Kasir.Models;
using Kasir.Services;

namespace Kasir.Tests.Services
{
    [TestFixture]
    public class PricingEngineTests
    {
        private PricingEngine _engine;
        private Product _product;

        [SetUp]
        public void SetUp()
        {
            _engine = new PricingEngine();
            _product = new Product
            {
                ProductCode = "TST001",
                Price = 500000,     // Rp 5,000.00
                Price1 = 450000,    // wholesale tier 1
                Price2 = 400000,    // qty break tier 2
                Price3 = 350000,    // qty break tier 3
                Price4 = 300000,    // special tier 4
                QtyBreak2 = 12,     // qty >= 12 → price2
                QtyBreak3 = 24,     // qty >= 24 → price3
                OpenPrice = "N"
            };
        }

        // Base price tests
        [TestCase(1, 500000, Description = "qty=1: base price")]
        [TestCase(5, 500000, Description = "qty=5: still base")]
        [TestCase(11, 500000, Description = "qty=11: below break2, base price")]
        public void GetUnitPrice_BelowBreak2_ReturnsBasePrice(int qty, int expected)
        {
            _engine.GetUnitPrice(_product, qty).Should().Be(expected);
        }

        // Qty break tier 2 tests
        [TestCase(12, 400000, Description = "qty=12: exactly at break2 → price2")]
        [TestCase(20, 400000, Description = "qty=20: between break2 and break3 → price2")]
        [TestCase(23, 400000, Description = "qty=23: just below break3 → price2")]
        public void GetUnitPrice_AtOrAboveBreak2_ReturnsPrice2(int qty, int expected)
        {
            _engine.GetUnitPrice(_product, qty).Should().Be(expected);
        }

        // Qty break tier 3 tests
        [TestCase(24, 350000, Description = "qty=24: exactly at break3 → price3")]
        [TestCase(50, 350000, Description = "qty=50: above break3 → price3")]
        [TestCase(100, 350000, Description = "qty=100: well above break3 → price3")]
        public void GetUnitPrice_AtOrAboveBreak3_ReturnsPrice3(int qty, int expected)
        {
            _engine.GetUnitPrice(_product, qty).Should().Be(expected);
        }

        // Disabled tier tests
        [Test]
        public void GetUnitPrice_Price2Zero_StaysAtBaseAboveBreak2()
        {
            _product.Price2 = 0;
            _engine.GetUnitPrice(_product, 15).Should().Be(500000);
        }

        [Test]
        public void GetUnitPrice_Break2Zero_NoTierTriggered()
        {
            _product.QtyBreak2 = 0;
            _engine.GetUnitPrice(_product, 15).Should().Be(500000);
        }

        // Open price override
        [Test]
        public void GetUnitPrice_OpenPriceY_AllowsOverride()
        {
            _product.OpenPrice = "Y";
            _engine.GetUnitPrice(_product, 1, overridePrice: 300000).Should().Be(300000);
        }

        [Test]
        public void GetUnitPrice_OpenPriceN_IgnoresOverride()
        {
            _product.OpenPrice = "N";
            _engine.GetUnitPrice(_product, 1, overridePrice: 300000).Should().Be(500000);
        }

        [Test]
        public void GetUnitPrice_OpenPriceY_ZeroOverride_ReturnsBase()
        {
            _product.OpenPrice = "Y";
            _engine.GetUnitPrice(_product, 1, overridePrice: 0).Should().Be(500000);
        }

        // Promotional price
        [Test]
        public void GetUnitPrice_PromoPrice_OverridesAllTiers()
        {
            _engine.GetUnitPrice(_product, 1, promoPrice: 250000).Should().Be(250000);
        }

        [Test]
        public void GetUnitPrice_PromoZero_IgnoresPromo()
        {
            _engine.GetUnitPrice(_product, 1, promoPrice: 0).Should().Be(500000);
        }

        // Priority: promo > open price > qty break > base
        [Test]
        public void GetUnitPrice_AllOverridesPresent_PromoWins()
        {
            _product.OpenPrice = "Y";
            long result = _engine.GetUnitPrice(_product, 24,
                overridePrice: 100000,
                promoPrice: 150000);

            result.Should().Be(150000, "promo has highest priority");
        }

        // Customer tier (price1/price4)
        [Test]
        public void GetUnitPrice_CustomerTier1_ReturnsPrice1()
        {
            _engine.GetUnitPrice(_product, 1, customerTier: 1).Should().Be(450000);
        }

        [Test]
        public void GetUnitPrice_CustomerTier4_ReturnsPrice4()
        {
            _engine.GetUnitPrice(_product, 1, customerTier: 4).Should().Be(300000);
        }

        [Test]
        public void GetUnitPrice_CustomerTier1_Price1Zero_FallsBackToBase()
        {
            _product.Price1 = 0;
            _engine.GetUnitPrice(_product, 1, customerTier: 1).Should().Be(500000);
        }

        // Edge cases
        [Test]
        public void GetUnitPrice_ZeroQty_ReturnsBasePrice()
        {
            _engine.GetUnitPrice(_product, 0).Should().Be(500000);
        }

        [Test]
        public void GetUnitPrice_NegativeQty_ReturnsBasePrice()
        {
            _engine.GetUnitPrice(_product, -1).Should().Be(500000);
        }
    }
}
