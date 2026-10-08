using System;
using Microsoft.Data.Sqlite;
using NUnit.Framework;
using FluentAssertions;
using Kasir.Data.Migrations;
using Kasir.Data.Repositories;
using Kasir.Models;
using Kasir.Services;
using Kasir.Tests.TestHelpers;
using Kasir.Tests.TestHelpers.Fakes;

namespace Kasir.Tests.Services
{
    // PR-K5 "Barang Masuk Cepat": a marketplace item gets an internal code from
    // 9000-9999 (skipping codes in use), a product row with its cost, and a PURCHASE
    // stock movement - in one go, from name/category/cost/price/qty.
    [TestFixture]
    public class ProductServiceTests
    {
        private SqliteConnection _db;
        private ProductService _service;
        private ProductRepository _products;

        [SetUp]
        public void SetUp()
        {
            _db = TestDb.Create();
            new ConfigRepository(_db).Set("register_id", "01");
            new Migration_013().Up(_db); // category rows (dept per category)
            _products = new ProductRepository(_db);
            _service = new ProductService(_db, new FakeClock(new DateTime(2026, 10, 8, 11, 0, 0)));
        }

        [TearDown]
        public void TearDown()
        {
            _db.Close();
            _db.Dispose();
        }

        private void Existing(string code, string status = "A")
        {
            _products.Insert(new Product
            {
                ProductCode = code, Name = "ADA " + code, Price = 100, Status = status,
                OpenPrice = "N", VatFlag = "N", LuxuryTaxFlag = "N", IsConsignment = "N"
            });
        }

        private long Scalar(string sql)
        {
            using var cmd = _db.CreateCommand();
            cmd.CommandText = sql;
            return Convert.ToInt64(cmd.ExecuteScalar());
        }

        [Test]
        public void NextFreeCode_EmptyRange_Is9000()
        {
            _service.NextFreeCode().Should().Be("9000");
        }

        [Test]
        public void NextFreeCode_SkipsCodesInUse_IncludingInactive()
        {
            Existing("9000");
            Existing("9001", "I");
            Existing("9003");
            _service.NextFreeCode().Should().Be("9002");
        }

        [Test]
        public void NextFreeCode_IgnoresCodesOutsideTheRange()
        {
            Existing("8999");
            Existing("90000");
            Existing("A9000");
            Existing("09000");
            _service.NextFreeCode().Should().Be("9000");
        }

        [Test]
        public void NextFreeCode_RangeFull_Throws()
        {
            using (var cmd = _db.CreateCommand())
            {
                cmd.CommandText = @"WITH RECURSIVE n(x) AS (SELECT 9000 UNION ALL SELECT x + 1 FROM n WHERE x < 9999)
                                    INSERT INTO products (product_code, name) SELECT CAST(x AS TEXT), 'X' FROM n";
                cmd.ExecuteNonQuery();
            }
            Action act = () => _service.NextFreeCode();
            act.Should().Throw<InvalidOperationException>().WithMessage("*9000-9999*");
        }

        [Test]
        public void CreateQuickProduct_CreatesProduct_WithCostAndCategoryDept()
        {
            Existing("9000");
            var result = _service.CreateQuickProduct("  lampu tidur led  ", "AL", 1500000, 2500000, 4, 7);

            result.ProductCode.Should().Be("9001");
            var p = _products.GetByCode("9001");
            p.Name.Should().Be("LAMPU TIDUR LED");
            p.DeptCode.Should().Be("42", "ALAT LISTRIK's department");
            p.Price.Should().Be(2500000);
            p.BuyingPrice.Should().Be(1500000);
            p.CostPrice.Should().Be(1500000, "the cost is captured on the product");
            p.Status.Should().Be("A");
            p.OpenPrice.Should().Be("N");
            p.Unit.Should().Be("PCS");
            p.ChangedBy.Should().Be(7);
        }

        [Test]
        public void CreateQuickProduct_WritesPurchaseMovement_AtCost()
        {
            var result = _service.CreateQuickProduct("Mainan Robot", "MY", 3000000, 4500000, 3, 1);

            result.JournalNo.Should().StartWith("BMC-01-");
            using var cmd = _db.CreateCommand();
            cmd.CommandText = "SELECT movement_type, qty_in, cost_price, val_in, journal_no, doc_date, location_code FROM stock_movements WHERE product_code = @c";
            cmd.Parameters.AddWithValue("@c", result.ProductCode);
            using var rd = cmd.ExecuteReader();
            rd.Read().Should().BeTrue();
            rd.GetString(0).Should().Be("PURCHASE");
            rd.GetInt32(1).Should().Be(3);
            rd.GetInt64(2).Should().Be(3000000);
            rd.GetInt64(3).Should().Be(9000000);
            rd.GetString(4).Should().Be(result.JournalNo);
            rd.GetString(5).Should().Be("2026-10-08");
            rd.GetString(6).Should().Be("T");
            rd.Read().Should().BeFalse("one movement");

            new InventoryService(_db).GetStockOnHand(result.ProductCode).Should().Be(3);
        }

        [Test]
        public void CreateQuickProduct_ThenSells_WithCostAsCogs()
        {
            var result = _service.CreateQuickProduct("Gunting", "AT", 800000, 1200000, 2, 1);
            var sales = new SalesService(_db, new FakeClock(new DateTime(2026, 10, 8, 12, 0, 0)));
            sales.SetCashier("ADM", 1);

            var item = sales.AddItem(result.ProductCode, 1);
            item.UnitPrice.Should().Be(1200000);
            var sale = sales.CompleteSale(1200000, 0, 0, "", "", "");
            Scalar($"SELECT cogs FROM sale_items WHERE journal_no = '{sale.JournalNo}'").Should().Be(800000);
        }

        [TestCase("", "AL", 1000, 2000, 1)]
        [TestCase("X", "ZZ", 1000, 2000, 1)]
        [TestCase("X", "AL", 0, 2000, 1)]
        [TestCase("X", "AL", 1000, 0, 1)]
        [TestCase("X", "AL", 1000, 2000, 0)]
        public void CreateQuickProduct_InvalidInput_ThrowsAndWritesNothing(string name, string cat, long cost, long price, int qty)
        {
            Action act = () => _service.CreateQuickProduct(name, cat, cost, price, qty, 1);
            act.Should().Throw<ArgumentException>();
            Scalar("SELECT COUNT(*) FROM products WHERE product_code GLOB '9[0-9][0-9][0-9]'").Should().Be(0);
            Scalar("SELECT COUNT(*) FROM stock_movements").Should().Be(0);
        }

        [Test]
        public void CreateQuickProduct_NameTooLong_Throws()
        {
            Action act = () => _service.CreateQuickProduct(new string('A', 76), "AL", 1000, 2000, 1, 1);
            act.Should().Throw<ArgumentException>();
        }

        [Test]
        public void CreateQuickProduct_AcceptsLowercaseCategory_AndConsecutiveCodes()
        {
            _service.CreateQuickProduct("A", "ll", 1000, 2000, 1, 1).ProductCode.Should().Be("9000");
            _service.CreateQuickProduct("B", "PL", 1000, 2000, 1, 1).ProductCode.Should().Be("9001");
            _products.GetByCode("9000").DeptCode.Should().Be("100");
        }
    }
}
