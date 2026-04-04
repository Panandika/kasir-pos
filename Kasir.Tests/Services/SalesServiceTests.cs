using System.Data.SQLite;
using NUnit.Framework;
using FluentAssertions;
using Kasir.Data.Repositories;
using Kasir.Models;
using Kasir.Services;
using Kasir.Tests.TestHelpers;
using Kasir.Tests.TestHelpers.Fakes;

namespace Kasir.Tests.Services
{
    [TestFixture]
    public class SalesServiceTests
    {
        private SQLiteConnection _db;
        private SalesService _service;
        private FakeClock _clock;

        [SetUp]
        public void SetUp()
        {
            _db = TestDb.Create();
            _clock = new FakeClock(new System.DateTime(2026, 4, 4, 14, 30, 0));
            _service = new SalesService(_db, _clock);
            _service.SetCashier("ADM", 1);

            SeedTestData();
        }

        [TearDown]
        public void TearDown()
        {
            _db.Close();
            _db.Dispose();
        }

        private void SeedTestData()
        {
            // Seed config
            var configRepo = new ConfigRepository(_db);
            configRepo.Set("register_id", "01");

            // Seed products
            var productRepo = new ProductRepository(_db);
            productRepo.Insert(new Product
            {
                ProductCode = "P001",
                Name = "MINYAK GORENG 2L",
                Price = 3200000,
                Price2 = 2800000,
                QtyBreak2 = 12,
                Status = "A",
                OpenPrice = "N",
                VatFlag = "N",
                LuxuryTaxFlag = "N",
                IsConsignment = "N"
            });

            productRepo.Insert(new Product
            {
                ProductCode = "P002",
                Name = "SABUN CUCI 800G",
                Price = 1550000,
                Status = "A",
                OpenPrice = "N",
                VatFlag = "N",
                LuxuryTaxFlag = "N",
                IsConsignment = "N"
            });

            productRepo.Insert(new Product
            {
                ProductCode = "P003",
                Name = "GULA PASIR 1KG",
                Price = 1800000,
                DiscPct = 500, // 5% product discount
                Status = "A",
                OpenPrice = "N",
                VatFlag = "N",
                LuxuryTaxFlag = "N",
                IsConsignment = "N"
            });
        }

        [Test]
        public void AddItem_ValidProduct_ReturnsItem()
        {
            var item = _service.AddItem("P001", 1);

            item.Should().NotBeNull();
            item.ProductCode.Should().Be("P001");
            item.ProductName.Should().Be("MINYAK GORENG 2L");
            item.UnitPrice.Should().Be(3200000);
            item.Quantity.Should().Be(1);
            item.Value.Should().Be(3200000);
        }

        [Test]
        public void AddItem_NonExistentProduct_ReturnsNull()
        {
            var item = _service.AddItem("XXXX", 1);
            item.Should().BeNull();
        }

        [Test]
        public void AddItem_MultipleItems_TracksAll()
        {
            _service.AddItem("P001", 1);
            _service.AddItem("P002", 2);

            _service.CurrentItems.Count.Should().Be(2);
        }

        [Test]
        public void AddItem_QtyBreakTier_UsesPrice2()
        {
            var item = _service.AddItem("P001", 12); // qty >= 12 → price2

            item.UnitPrice.Should().Be(2800000);
            item.Quantity.Should().Be(12);
        }

        [Test]
        public void AddItem_WithProductDiscount_AppliesDiscount()
        {
            var item = _service.AddItem("P003", 1); // 5% discount

            item.DiscPct.Should().Be(500);
            // 1800000 * 5% = 90000
            item.DiscValue.Should().Be(90000);
            item.Value.Should().Be(1710000); // 1800000 - 90000
        }

        [Test]
        public void GetTotals_SingleItem_CorrectTotals()
        {
            _service.AddItem("P001", 1);

            var totals = _service.GetTotals();

            totals.GrossAmount.Should().Be(3200000);
            totals.TotalDiscount.Should().Be(0);
            totals.NetAmount.Should().Be(3200000);
            totals.ItemCount.Should().Be(1);
            totals.LineCount.Should().Be(1);
        }

        [Test]
        public void GetTotals_MultipleItems_SumsCorrectly()
        {
            _service.AddItem("P001", 2); // 2 × 3200000 = 6400000
            _service.AddItem("P002", 1); // 1 × 1550000 = 1550000

            var totals = _service.GetTotals();

            totals.GrossAmount.Should().Be(6400000 + 1550000);
            totals.NetAmount.Should().Be(7950000);
            totals.LineCount.Should().Be(2);
        }

        [Test]
        public void RemoveItem_RemovesFromList()
        {
            _service.AddItem("P001", 1);
            _service.AddItem("P002", 1);

            _service.RemoveItem(0);

            _service.CurrentItems.Count.Should().Be(1);
            _service.CurrentItems[0].ProductCode.Should().Be("P002");
        }

        [Test]
        public void UpdateItemQty_RecalculatesValue()
        {
            _service.AddItem("P001", 1);

            _service.UpdateItemQty(0, 3);

            _service.CurrentItems[0].Quantity.Should().Be(3);
            _service.CurrentItems[0].Value.Should().Be(3200000L * 3);
        }

        [Test]
        public void CompleteSale_PersistsAndReturnsSale()
        {
            _service.AddItem("P001", 1);
            _service.AddItem("P002", 1);

            var sale = _service.CompleteSale(
                cashAmount: 5000000,
                cardAmount: 0,
                voucherAmount: 0,
                cardCode: "",
                cardType: "",
                memberCode: "");

            sale.Should().NotBeNull();
            sale.JournalNo.Should().NotBeNullOrEmpty();
            sale.TotalValue.Should().Be(4750000); // 3200000 + 1550000
            sale.CashAmount.Should().Be(5000000);
            sale.ChangeAmount.Should().Be(250000); // 5000000 - 4750000
            sale.DocDate.Should().Be("2026-04-04");
        }

        [Test]
        public void CompleteSale_InsufficientPayment_Throws()
        {
            _service.AddItem("P001", 1); // 3200000

            System.Action act = () => _service.CompleteSale(
                cashAmount: 1000000, // not enough
                cardAmount: 0,
                voucherAmount: 0,
                cardCode: "",
                cardType: "",
                memberCode: "");

            act.Should().Throw<System.InvalidOperationException>();
        }

        [Test]
        public void CompleteSale_SplitPayment_Works()
        {
            _service.AddItem("P001", 1); // 3200000

            var sale = _service.CompleteSale(
                cashAmount: 2000000,
                cardAmount: 1200000,
                voucherAmount: 0,
                cardCode: "VISA",
                cardType: "C",
                memberCode: "");

            sale.CashAmount.Should().Be(2000000);
            sale.NonCash.Should().Be(1200000);
            sale.ChangeAmount.Should().Be(0);
        }

        [Test]
        public void CompleteSale_WithMember_CalculatesLoyaltyPoints()
        {
            _service.AddItem("P001", 1); // 3200000 = Rp 32,000 → 3 stickers

            var sale = _service.CompleteSale(
                cashAmount: 3200000,
                cardAmount: 0,
                voucherAmount: 0,
                cardCode: "",
                cardType: "",
                memberCode: "MBR001");

            sale.PointValue.Should().Be(3); // 3200000 / 1000000 = 3
            sale.MemberCode.Should().Be("MBR001");
        }

        [Test]
        public void VoidSale_SetsControl3()
        {
            _service.AddItem("P001", 1);
            var sale = _service.CompleteSale(3200000, 0, 0, "", "", "");

            _service.VoidSale(sale.JournalNo);

            var voided = new SaleRepository(_db).GetByJournalNo(sale.JournalNo);
            voided.Control.Should().Be(3);
        }

        [Test]
        public void ClearCurrentSale_EmptiesItemList()
        {
            _service.AddItem("P001", 1);
            _service.AddItem("P002", 1);

            _service.ClearCurrentSale();

            _service.CurrentItems.Count.Should().Be(0);
        }

        [Test]
        public void CompleteSale_GeneratesUniqueJournalNos()
        {
            _service.AddItem("P001", 1);
            var sale1 = _service.CompleteSale(3200000, 0, 0, "", "", "");
            _service.ClearCurrentSale();

            _service.AddItem("P002", 1);
            var sale2 = _service.CompleteSale(1550000, 0, 0, "", "", "");

            sale1.JournalNo.Should().NotBe(sale2.JournalNo);
        }
    }
}
