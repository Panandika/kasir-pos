using System.Collections.Generic;
using System.Text;
using NUnit.Framework;
using FluentAssertions;
using Kasir.Models;
using Kasir.Services;

namespace Kasir.Tests.Services
{
    [TestFixture]
    public class ReceiptBuilderTests
    {
        private ReceiptBuilder _builder;

        [SetUp]
        public void SetUp()
        {
            _builder = new ReceiptBuilder();
        }

        [Test]
        public void BuildSaleReceipt_ContainsStoreName()
        {
            var sale = CreateTestSale();
            var items = CreateTestItems();

            byte[] receipt = _builder.BuildSaleReceipt(sale, items, "TOKO YONICO", "ADM");

            string text = Encoding.GetEncoding(437).GetString(receipt);
            text.Should().Contain("TOKO YONICO");
        }

        [Test]
        public void BuildSaleReceipt_ContainsJournalNo()
        {
            var sale = CreateTestSale();
            var items = CreateTestItems();

            byte[] receipt = _builder.BuildSaleReceipt(sale, items, "TOKO", "ADM");

            string text = Encoding.GetEncoding(437).GetString(receipt);
            text.Should().Contain("KLR-01-2604-0001");
        }

        [Test]
        public void BuildSaleReceipt_ContainsItemNames()
        {
            var sale = CreateTestSale();
            var items = CreateTestItems();

            byte[] receipt = _builder.BuildSaleReceipt(sale, items, "TOKO", "ADM");

            string text = Encoding.GetEncoding(437).GetString(receipt);
            text.Should().Contain("MINYAK GORENG");
            text.Should().Contain("SABUN CUCI");
        }

        [Test]
        public void BuildSaleReceipt_ContainsTotalAndChange()
        {
            var sale = CreateTestSale();
            var items = CreateTestItems();

            byte[] receipt = _builder.BuildSaleReceipt(sale, items, "TOKO", "ADM");

            string text = Encoding.GetEncoding(437).GetString(receipt);
            text.Should().Contain("TOTAL:");
            text.Should().Contain("TUNAI:");
            text.Should().Contain("KEMBALI:");
        }

        [Test]
        public void BuildSaleReceipt_ContainsThankYou()
        {
            var sale = CreateTestSale();
            var items = CreateTestItems();

            byte[] receipt = _builder.BuildSaleReceipt(sale, items, "TOKO", "ADM");

            string text = Encoding.GetEncoding(437).GetString(receipt);
            text.Should().Contain("Terima kasih!");
        }

        [Test]
        public void BuildSaleReceipt_ContainsLoyaltyInfo_WhenMemberPresent()
        {
            var sale = CreateTestSale();
            sale.MemberCode = "MBR001";
            sale.PointValue = 5;
            var items = CreateTestItems();

            byte[] receipt = _builder.BuildSaleReceipt(sale, items, "TOKO", "ADM");

            string text = Encoding.GetEncoding(437).GetString(receipt);
            text.Should().Contain("Stiker: 5");
            text.Should().Contain("MBR001");
        }

        [Test]
        public void BuildSaleReceipt_EndsWithCutCommand()
        {
            var sale = CreateTestSale();
            var items = CreateTestItems();

            byte[] receipt = _builder.BuildSaleReceipt(sale, items, "TOKO", "ADM");

            // Partial cut is 0x1D, 0x56, 0x01
            receipt[receipt.Length - 3].Should().Be(0x1D);
            receipt[receipt.Length - 2].Should().Be(0x56);
            receipt[receipt.Length - 1].Should().Be(0x01);
        }

        [Test]
        public void BuildSaleReceipt_ShowsQtyDetail_WhenQtyGreaterThan1()
        {
            var sale = CreateTestSale();
            var items = new List<SaleItem>
            {
                new SaleItem
                {
                    ProductCode = "P001",
                    ProductName = "MINYAK GORENG",
                    Quantity = 3,
                    UnitPrice = 3200000,
                    Value = 9600000
                }
            };

            byte[] receipt = _builder.BuildSaleReceipt(sale, items, "TOKO", "ADM");

            string text = Encoding.GetEncoding(437).GetString(receipt);
            text.Should().Contain("3 x");
        }

        private static Sale CreateTestSale()
        {
            return new Sale
            {
                JournalNo = "KLR-01-2604-0001",
                DocDate = "2026-04-04",
                Cashier = "ADM",
                TotalValue = 4750000,
                GrossAmount = 4750000,
                CashAmount = 5000000,
                ChangeAmount = 250000,
                TotalDisc = 0
            };
        }

        private static List<SaleItem> CreateTestItems()
        {
            return new List<SaleItem>
            {
                new SaleItem
                {
                    ProductCode = "P001",
                    ProductName = "MINYAK GORENG 2L",
                    Quantity = 1,
                    UnitPrice = 3200000,
                    Value = 3200000
                },
                new SaleItem
                {
                    ProductCode = "P002",
                    ProductName = "SABUN CUCI 800G",
                    Quantity = 1,
                    UnitPrice = 1550000,
                    Value = 1550000
                }
            };
        }
    }
}
