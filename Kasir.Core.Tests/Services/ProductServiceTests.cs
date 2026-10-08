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
    // PR-K5 "Barang Masuk Cepat": a marketplace item gets an internal code from its
    // register's block in 9000-9899 (skipping codes in use), a product row with its cost, and a PURCHASE
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

        private string Create(string name = "X") =>
            _service.CreateQuickProduct(name, "LL", 1000, 2000, 1, 1).ProductCode;

        private void UseRegister(string registerId)
        {
            new ConfigRepository(_db).Set("register_id", registerId);
            _service = new ProductService(_db, new FakeClock(new DateTime(2026, 10, 8, 11, 0, 0)));
        }

        // H2: each register hands out codes from its own block, so two registers can never
        // pick the same code before the next LAN sync.
        [TestCase("01", "9000", "9001")]
        [TestCase("02", "9300", "9301")]
        [TestCase("03", "9600", "9601")]
        public void QuickCodes_ComeFromTheRegistersOwnBlock(string registerId, string first, string second)
        {
            UseRegister(registerId);
            Create("A").Should().Be(first);
            Create("B").Should().Be(second);
        }

        [TestCase("01", 9000, 9299)]
        [TestCase("02", 9300, 9599)]
        [TestCase("03", 9600, 9899)]
        public void QuickCodeBlock_PerRegister(string registerId, int first, int last)
        {
            ProductService.QuickCodeBlock(registerId).Should().Be((first, last));
        }

        [TestCase("04")]
        [TestCase("99")]
        [TestCase("")]
        public void QuickCodes_RegisterWithoutABlock_ThrowsClearly(string registerId)
        {
            // 9900-9999 is reserved; registers other than 01-03 get no block.
            UseRegister(registerId);
            Action act = () => Create();
            act.Should().Throw<InvalidOperationException>().WithMessage("*kasir*");
            Scalar("SELECT COUNT(*) FROM products WHERE product_code GLOB '9[0-9][0-9][0-9]'").Should().Be(0);
        }

        [Test]
        public void QuickCodes_SkipCodesAlreadyInProducts_IncludingInactive()
        {
            // 85 legacy products already use 9003+ codes.
            Existing("9000");
            Existing("9001", "I");
            Existing("9003");
            Create("A").Should().Be("9002");
            Create("B").Should().Be("9004");
        }

        [Test]
        public void QuickCodes_SkipExisting_InAnotherRegistersBlock()
        {
            UseRegister("02");
            Existing("9300");
            Existing("9000"); // register 01's block: irrelevant here
            Create().Should().Be("9301");
        }

        [Test]
        public void QuickCodes_AreMonotonic_ADeletedCodeIsNeverReused()
        {
            Create("A").Should().Be("9000");
            using (var cmd = _db.CreateCommand())
            {
                cmd.CommandText = "DELETE FROM stock_movements WHERE product_code = '9000'; DELETE FROM products WHERE product_code = '9000'";
                cmd.ExecuteNonQuery();
            }
            Create("B").Should().Be("9001", "historic sale_items/movements keep the old code");
        }

        [Test]
        public void QuickCodes_BlockExhausted_ThrowsWithTheBlockRange()
        {
            UseRegister("02");
            using (var cmd = _db.CreateCommand())
            {
                cmd.CommandText = "INSERT INTO counters (prefix, register_id, current_value) VALUES (@p, '02', 300)";
                cmd.Parameters.AddWithValue("@p", ProductService.QuickCodeCounterPrefix);
                cmd.ExecuteNonQuery();
            }
            Action act = () => Create();
            act.Should().Throw<InvalidOperationException>().WithMessage("*9300-9599*");
        }

        [Test]
        public void QuickCodes_BlockFullOfExistingCodes_Throws()
        {
            using (var cmd = _db.CreateCommand())
            {
                cmd.CommandText = @"WITH RECURSIVE n(x) AS (SELECT 9000 UNION ALL SELECT x + 1 FROM n WHERE x < 9299)
                                    INSERT INTO products (product_code, name) SELECT CAST(x AS TEXT), 'X' FROM n";
                cmd.ExecuteNonQuery();
            }
            Action act = () => Create();
            act.Should().Throw<InvalidOperationException>().WithMessage("*9000-9299*");
        }

        [Test]
        public void QuickCodes_FailedSave_DoesNotBurnACode()
        {
            Action bad = () => _service.CreateQuickProduct("X", "ZZ", 1000, 2000, 1, 1);
            bad.Should().Throw<ArgumentException>();
            Create().Should().Be("9000");
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
            rd.GetInt32(1).Should().Be(StockQty.ToLedger(3));
            rd.GetInt64(2).Should().Be(3000000);
            rd.GetInt64(3).Should().Be(9000000);
            rd.GetString(4).Should().Be(result.JournalNo);
            rd.GetString(5).Should().Be("2026-10-08");
            rd.GetString(6).Should().Be("T");
            rd.Read().Should().BeFalse("one movement");

            new InventoryService(_db).GetStockOnHand(result.ProductCode).Should().Be(StockQty.ToLedger(3));
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
