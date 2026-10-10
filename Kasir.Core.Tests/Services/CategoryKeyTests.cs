using System;
using System.Linq;
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
    // PR-K4: six category quick keys for unlabelled goods, alongside plain code "1". Open
    // price, non-stock, COGS 0 = unknown cost like code "1" (owner decision 2026-10-09),
    // with their own departments so sales-by-category works.
    [TestFixture]
    public class CategoryKeyTests
    {
        private SqliteConnection _db;
        private SalesService _service;

        [SetUp]
        public void SetUp()
        {
            _db = TestDb.Create();
            new ConfigRepository(_db).Set("register_id", "01");
            new Migration_013().Up(_db);
            _service = new SalesService(_db, new FakeClock(new DateTime(2026, 4, 4, 14, 30, 0)));
            _service.SetCashier("ADM", 1);
        }

        [TearDown]
        public void TearDown()
        {
            _db.Close();
            _db.Dispose();
        }

        private long Scalar(string sql)
        {
            using var cmd = _db.CreateCommand();
            cmd.CommandText = sql;
            return Convert.ToInt64(cmd.ExecuteScalar());
        }

        [Test]
        public void Migration_SeedsSixOpenPriceCategoryProducts_WithDepartments()
        {
            var repo = new ProductRepository(_db);
            var expected = new (string Code, string Name, string Dept)[]
            {
                ("AL", "ALAT LISTRIK", "42"), ("AT", "ALAT TULIS", "10"), ("PR", "PERABOT", "100"),
                ("PL", "PLASTIK", "22"), ("MY", "MAINAN", "44"), ("LL", "LAIN-LAIN", "100"),
            };
            foreach (var e in expected)
            {
                var p = repo.GetByCode(e.Code);
                p.Should().NotBeNull(e.Code);
                p.Name.Should().Be(e.Name);
                p.DeptCode.Should().Be(e.Dept);
                p.OpenPrice.Should().Be("Y");
                p.Status.Should().Be("A");
                p.CostPrice.Should().Be(0, "category keys carry no cost");
                p.MarginPct.Should().Be(0, "no margin estimate is seeded");
            }
            SalesService.CategoryKeys.Select(k => k.Code).Should().Equal("AL", "AT", "PR", "PL", "MY", "LL");
            SalesService.CategoryKeys.Select(k => k.Code).Should().BeEquivalentTo(SalesService.CategoryKeyCodes);
        }

        [Test]
        public void Migration_IsIdempotent_AndKeepsAnExistingRow()
        {
            Exec("UPDATE products SET name = 'MAINAN ANAK' WHERE product_code = 'MY'");
            new Migration_013().Up(_db);
            Scalar("SELECT COUNT(*) FROM products WHERE product_code IN ('AL','AT','PR','PL','MY','LL')").Should().Be(6);
            Text("SELECT name FROM products WHERE product_code = 'MY'").Should().Be("MAINAN ANAK");
        }

        private void Exec(string sql)
        {
            using var cmd = _db.CreateCommand();
            cmd.CommandText = sql;
            cmd.ExecuteNonQuery();
        }

        [Test]
        public void AddMiscItem_WithCategory_HasZeroCogs_UnknownCost()
        {
            var item = _service.AddMiscItem(2, 1000000, "AL"); // 2 x Rp 10.000

            item.ProductCode.Should().Be("AL");
            item.ProductName.Should().Be("ALAT LISTRIK");
            item.UnitPrice.Should().Be(1000000);
            item.Value.Should().Be(2000000);
            item.Cogs.Should().Be(0, "COGS 0 = unknown cost, like code 1");
            item.Remark.Should().BeNullOrEmpty("no estimate to tag");
        }

        [Test]
        public void AddMiscItem_WithCategory_IgnoresMarginAndCostOnTheRow()
        {
            // Even if someone edits the category row, no cost is estimated from it.
            Exec("UPDATE products SET margin_pct = 4000, cost_price = 500000 WHERE product_code = 'MY'");
            _service.AddMiscItem(1, 1000000, "MY").Cogs.Should().Be(0);
        }

        [Test]
        public void AddMiscItem_UnknownCategory_Throws()
        {
            Action act = () => _service.AddMiscItem(1, 1000000, "P001");
            act.Should().Throw<ArgumentException>();
        }

        [Test]
        public void AddMiscItem_WithoutCategory_StillCode1_ZeroCogs()
        {
            var item = _service.AddMiscItem(1, 500000);
            item.ProductCode.Should().Be(SalesService.MiscProductCode);
            item.Cogs.Should().Be(0);
        }

        [Test]
        public void UpdateItemQty_CategoryLine_KeepsPrice_ZeroCogs()
        {
            _service.AddMiscItem(1, 1000000, "AT");
            _service.UpdateItemQty(0, 3);

            var item = _service.CurrentItems[0];
            item.UnitPrice.Should().Be(1000000, "the typed open price is kept");
            item.Value.Should().Be(3000000);
            item.Cogs.Should().Be(0);
        }

        [Test]
        public void CompleteSale_CategoryKey_NoStockMovement_ZeroCogs()
        {
            _service.AddMiscItem(2, 1000000, "LL");
            var sale = _service.CompleteSale(2000000, 0, 0, "", "", "");

            Scalar("SELECT COUNT(*) FROM stock_movements").Should().Be(0, "category keys are non-stock");
            Scalar($"SELECT cogs FROM sale_items WHERE journal_no = '{sale.JournalNo}' AND product_code = 'LL'")
                .Should().Be(0);
            Text($"SELECT remark FROM sale_items WHERE journal_no = '{sale.JournalNo}' AND product_code = 'LL'")
                .Should().BeEmpty("no estimated-COGS tag");
        }

        [Test]
        public void CategoryLine_SurvivesCrashRecovery()
        {
            _service.AddMiscItem(1, 1000000, "PR");
            var recovered = new SalesService(_db, new FakeClock(new DateTime(2026, 4, 4, 15, 0, 0)));
            recovered.RecoverPendingSale().Should().Be(1);
            recovered.CurrentItems[0].ProductCode.Should().Be("PR");
            recovered.CurrentItems[0].ProductName.Should().Be("PERABOT");
            recovered.CurrentItems[0].Cogs.Should().Be(0);
        }

        [Test]
        public void OpnameSheet_ExcludesNonStockCodes()
        {
            // L3: category keys (and code "1") have no stock to count.
            var repo = new ProductRepository(_db);
            repo.Insert(new Product
            {
                ProductCode = "1", Name = "Barang Tanpa Kode", Status = "A",
                OpenPrice = "Y", VatFlag = "N", LuxuryTaxFlag = "N", IsConsignment = "N"
            });
            repo.Insert(new Product
            {
                ProductCode = "P001", Name = "TEST", Price = 500000, Status = "A",
                OpenPrice = "N", VatFlag = "N", LuxuryTaxFlag = "N", IsConsignment = "N"
            });

            var sheet = new StockOpnameService(_db, new FakeClock(new DateTime(2026, 4, 4, 10, 0, 0)))
                .GetOpnameSheet(1000);

            sheet.Select(l => l.ProductCode).Should().Contain("P001");
            sheet.Select(l => l.ProductCode).Should().NotContain(c => SalesService.IsNonStockCode(c));
        }

        [Test]
        public void OpnameSheet_SkipsManualCodes2_44_99()
        {
            // K1/K4 (follow-up item 7): the legacy manual price codes kept by the D3
            // cleanup are not stock items either.
            var repo = new ProductRepository(_db);
            foreach (var code in new[] { "2", "44", "99", "P001" })
                repo.Insert(new Product
                {
                    ProductCode = code, Name = "TEST " + code, Price = 500000, Status = "A",
                    OpenPrice = code == "P001" ? "N" : "Y", VatFlag = "N", LuxuryTaxFlag = "N", IsConsignment = "N"
                });

            var codes = new StockOpnameService(_db, new FakeClock(new DateTime(2026, 4, 4, 10, 0, 0)))
                .GetOpnameSheet(1000).Select(l => l.ProductCode).ToList();

            codes.Should().Contain("P001");
            codes.Should().NotContain(new[] { "2", "44", "99" });
            codes.Should().NotContain(c => SalesService.IsNonStockItem(c));
        }

        [Test]
        public void IsNonStockItem_IsTheSaleRulePlusManualCodes()
        {
            foreach (var c in new[] { "1", "AL", "AT", "PR", "PL", "MY", "LL" })
            {
                SalesService.IsNonStockCode(c).Should().BeTrue(c);
                SalesService.IsNonStockItem(c).Should().BeTrue(c);
            }
            foreach (var c in new[] { "2", "44", "99" })
            {
                SalesService.IsNonStockItem(c).Should().BeTrue(c);
                SalesService.IsNonStockCode(c).Should().BeFalse(c + ": the sale path is unchanged");
            }
            SalesService.IsNonStockItem("P001").Should().BeFalse();
            SalesService.IsNonStockItem(null).Should().BeFalse();
        }

        [Test]
        public void Migration013_EnsuresMigration012Table_WhenK3WasSkipped()
        {
            // H3: a register that reached schema 13 from a build without 012 must still
            // have inactive_sale_log (012 would never run there afterwards).
            Exec("DROP TABLE IF EXISTS inactive_sale_log");
            new Migration_013().Up(_db);
            new Migration_013().Up(_db);
            Scalar("SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = 'inactive_sale_log'").Should().Be(1);
            Exec("INSERT OR IGNORE INTO inactive_sale_log (register_id, product_code, sale_date) VALUES ('01','X','2026-04-04')");
            Exec("INSERT OR IGNORE INTO inactive_sale_log (register_id, product_code, sale_date) VALUES ('01','X','2026-04-04')");
            Scalar("SELECT COUNT(*) FROM inactive_sale_log").Should().Be(1);
        }

        private string Text(string sql)
        {
            using var cmd = _db.CreateCommand();
            cmd.CommandText = sql;
            return Convert.ToString(cmd.ExecuteScalar());
        }

        [TestCase("al", "AL")]
        [TestCase("AL", "AL")]
        [TestCase(" ll ", "LL")]
        [TestCase("P001", null)]
        [TestCase("1", null)]
        [TestCase("", null)]
        public void ResolveCategoryKey_IsCaseInsensitive(string typed, string expected)
        {
            SalesService.ResolveCategoryKey(typed).Should().Be(expected);
        }
    }
}
