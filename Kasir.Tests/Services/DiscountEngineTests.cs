using System.Collections.Generic;
using NUnit.Framework;
using FluentAssertions;
using Kasir.Models;
using Kasir.Services;

namespace Kasir.Tests.Services
{
    [TestFixture]
    public class DiscountEngineTests
    {
        private DiscountEngine _engine;
        private Product _product;

        [SetUp]
        public void SetUp()
        {
            _engine = new DiscountEngine();
            _product = new Product
            {
                ProductCode = "P001",
                DeptCode = "10",
                DiscPct = 0,
                Price = 500000
            };
        }

        [Test]
        public void ResolveDiscount_NoDiscountConfigured_ReturnsNone()
        {
            var result = _engine.ResolveDiscount(
                _product, "2026-04-04",
                new List<Discount>(),
                0, 0, 0, 0);

            result.Source.Should().Be("none");
            result.DiscPct.Should().Be(0);
        }

        [Test]
        public void ResolveDiscount_ProductDiscPct_Applied()
        {
            _product.DiscPct = 500; // 5%

            var result = _engine.ResolveDiscount(
                _product, "2026-04-04",
                new List<Discount>(),
                0, 0, 0, 0);

            result.DiscPct.Should().Be(500);
            result.Source.Should().Be("product");
        }

        [Test]
        public void ResolveDiscount_DiscountsTableMatch_OverridesProductDisc()
        {
            _product.DiscPct = 500; // 5% on product

            var discounts = new List<Discount>
            {
                new Discount
                {
                    ProductCode = "P001",
                    DiscPct = 1000, // 10% from discounts table
                    DateStart = "2026-01-01",
                    DateEnd = "2026-12-31",
                    IsActive = 1
                }
            };

            var result = _engine.ResolveDiscount(
                _product, "2026-04-04",
                discounts,
                0, 0, 0, 0);

            result.DiscPct.Should().Be(1000);
            result.Source.Should().Be("discounts_table");
        }

        [Test]
        public void ResolveDiscount_DiscountsTableExpired_FallsBack()
        {
            _product.DiscPct = 500;

            var discounts = new List<Discount>
            {
                new Discount
                {
                    ProductCode = "P001",
                    DiscPct = 1000,
                    DateStart = "2025-01-01",
                    DateEnd = "2025-12-31", // expired
                    IsActive = 1
                }
            };

            var result = _engine.ResolveDiscount(
                _product, "2026-04-04",
                discounts,
                0, 0, 0, 0);

            result.DiscPct.Should().Be(500, "expired discount falls back to product disc");
            result.Source.Should().Be("product");
        }

        [Test]
        public void ResolveDiscount_PartnerDiscount_HighestPriority()
        {
            _product.DiscPct = 500; // 5% on product

            var discounts = new List<Discount>
            {
                new Discount
                {
                    ProductCode = "P001",
                    DiscPct = 1000,
                    DateStart = "2026-01-01",
                    DateEnd = "2026-12-31",
                    IsActive = 1
                }
            };

            // Partner discount: 15%
            var result = _engine.ResolveDiscount(
                _product, "2026-04-04",
                discounts,
                partnerDiscPct: 1500,
                accountDiscPct: 0,
                accountDiscDateStart: null,
                accountDiscDateEnd: null);

            result.DiscPct.Should().Be(1500);
            result.Source.Should().Be("partner");
        }

        [Test]
        public void ResolveDiscount_AccountConfigDisc_LowestPriority()
        {
            // No product disc, no discounts table, no partner
            var result = _engine.ResolveDiscount(
                _product, "2026-04-04",
                new List<Discount>(),
                partnerDiscPct: 0,
                accountDiscPct: 800, // 8% from account config
                accountDiscDateStart: "2026-01-01",
                accountDiscDateEnd: "2026-12-31");

            result.DiscPct.Should().Be(800);
            result.Source.Should().Be("account_config");
        }

        [Test]
        public void ResolveDiscount_AccountConfigExpired_NoDiscount()
        {
            var result = _engine.ResolveDiscount(
                _product, "2026-04-04",
                new List<Discount>(),
                partnerDiscPct: 0,
                accountDiscPct: 800,
                accountDiscDateStart: "2025-01-01",
                accountDiscDateEnd: "2025-12-31"); // expired

            result.Source.Should().Be("none");
        }

        [Test]
        public void ResolveDiscount_FirstMatchWins_PartnerOverridesAll()
        {
            _product.DiscPct = 500;

            var discounts = new List<Discount>
            {
                new Discount
                {
                    ProductCode = "P001",
                    DiscPct = 1000,
                    DateStart = "2026-01-01",
                    DateEnd = "2026-12-31",
                    IsActive = 1
                }
            };

            var result = _engine.ResolveDiscount(
                _product, "2026-04-04",
                discounts,
                partnerDiscPct: 1500,
                accountDiscPct: 800,
                accountDiscDateStart: "2026-01-01",
                accountDiscDateEnd: "2026-12-31");

            result.DiscPct.Should().Be(1500, "partner has highest priority");
            result.Source.Should().Be("partner");
        }

        [Test]
        public void ResolveDiscount_CascadingDiscount_Disc2PctApplied()
        {
            var discounts = new List<Discount>
            {
                new Discount
                {
                    ProductCode = "P001",
                    DiscPct = 1000,   // 10%
                    Disc2Pct = 500,   // 5% cascading
                    DateStart = "2026-01-01",
                    DateEnd = "2026-12-31",
                    IsActive = 1
                }
            };

            var result = _engine.ResolveDiscount(
                _product, "2026-04-04",
                discounts,
                0, 0, null, null);

            result.DiscPct.Should().Be(1000);
            result.Disc2Pct.Should().Be(500);
            result.Source.Should().Be("discounts_table");
        }

        // DiscountResult.CalculateDiscount tests
        [Test]
        public void CalculateDiscount_SinglePercent()
        {
            var result = new DiscountResult { DiscPct = 1000 }; // 10%
            result.CalculateDiscount(500000).Should().Be(50000); // Rp 500 discount
        }

        [Test]
        public void CalculateDiscount_CascadingPercent()
        {
            var result = new DiscountResult { DiscPct = 1000, Disc2Pct = 500 }; // 10% + 5%
            // 500000 * 10% = 50000, remainder = 450000 * 5% = 22500
            result.CalculateDiscount(500000).Should().Be(72500);
        }

        [Test]
        public void CalculateDiscount_FlatAmount()
        {
            var result = new DiscountResult { DiscAmount = 25000 };
            result.CalculateDiscount(500000).Should().Be(25000);
        }

        [Test]
        public void CalculateDiscount_Zero_NoDiscount()
        {
            DiscountResult.None.CalculateDiscount(500000).Should().Be(0);
        }
    }
}
