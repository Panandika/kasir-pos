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
    // PR-K4: six category quick keys replace code "1" for unlabelled goods. Open price,
    // non-stock, and an estimated COGS = price x (1 - margin) so the margin report is
    // no longer inflated by zero-COGS manual entries.
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
                p.MarginPct.Should().Be(2500, "default margin 25.00% (x100 like products.margin_pct)");
            }
            SalesService.CategoryKeys.Select(k => k.Code).Should().Equal("AL", "AT", "PR", "PL", "MY", "LL");
            SalesService.CategoryKeys.Select(k => k.Code).Should().BeEquivalentTo(SalesService.CategoryKeyCodes);
        }

        [Test]
        public void Migration_IsIdempotent_AndKeepsAConfiguredMargin()
        {
            Exec("UPDATE products SET margin_pct = 4000 WHERE product_code = 'MY'");
            new Migration_013().Up(_db);
            Scalar("SELECT COUNT(*) FROM products WHERE product_code IN ('AL','AT','PR','PL','MY','LL')").Should().Be(6);
            Scalar("SELECT margin_pct FROM products WHERE product_code = 'MY'").Should().Be(4000);
        }

        private void Exec(string sql)
        {
            using var cmd = _db.CreateCommand();
            cmd.CommandText = sql;
            cmd.ExecuteNonQuery();
        }

        [Test]
        public void AddMiscItem_WithCategory_EstimatesCogs()
        {
            var item = _service.AddMiscItem(2, 1000000, "AL"); // 2 x Rp 10.000

            item.ProductCode.Should().Be("AL");
            item.ProductName.Should().Be("ALAT LISTRIK");
            item.UnitPrice.Should().Be(1000000);
            item.Value.Should().Be(2000000);
            item.Cogs.Should().Be(1500000, "COGS = price x 75% x qty at the default 25% margin");
        }

        [Test]
        public void AddMiscItem_WithCategory_UsesTheCategoryMargin()
        {
            Exec("UPDATE products SET margin_pct = 4000 WHERE product_code = 'MY'");
            var item = _service.AddMiscItem(1, 1000000, "MY");
            item.Cogs.Should().Be(600000, "40% margin -> COGS 60% of price");
        }

        [Test]
        public void AddMiscItem_WithCategory_ZeroMargin_FallsBackToDefault()
        {
            Exec("UPDATE products SET margin_pct = 0 WHERE product_code = 'PL'");
            _service.AddMiscItem(1, 1000000, "PL").Cogs.Should().Be(750000);
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
        public void UpdateItemQty_CategoryLine_ScalesEstimatedCogs_KeepsPrice()
        {
            _service.AddMiscItem(1, 1000000, "AT");
            _service.UpdateItemQty(0, 3);

            var item = _service.CurrentItems[0];
            item.UnitPrice.Should().Be(1000000, "the typed open price is kept");
            item.Value.Should().Be(3000000);
            item.Cogs.Should().Be(2250000);
        }

        [Test]
        public void CompleteSale_CategoryKey_NoStockMovement_HasCogs()
        {
            _service.AddMiscItem(2, 1000000, "LL");
            var sale = _service.CompleteSale(2000000, 0, 0, "", "", "");

            Scalar("SELECT COUNT(*) FROM stock_movements").Should().Be(0, "category keys are non-stock");
            Scalar($"SELECT cogs FROM sale_items WHERE journal_no = '{sale.JournalNo}' AND product_code = 'LL'")
                .Should().Be(1500000);
        }

        [Test]
        public void CategoryLine_SurvivesCrashRecovery_WithItsCogs()
        {
            _service.AddMiscItem(1, 1000000, "PR");
            var recovered = new SalesService(_db, new FakeClock(new DateTime(2026, 4, 4, 15, 0, 0)));
            recovered.RecoverPendingSale().Should().Be(1);
            recovered.CurrentItems[0].ProductCode.Should().Be("PR");
            recovered.CurrentItems[0].ProductName.Should().Be("PERABOT");
            recovered.CurrentItems[0].Cogs.Should().Be(750000);
        }

        [Test]
        public void AddMiscItem_WithCategory_MarginAbove100Pct_IsClamped_NoNegativeCogs()
        {
            // L2: a mistyped margin_pct > 10000 must not produce a negative COGS.
            Exec("UPDATE products SET margin_pct = 15000 WHERE product_code = 'AL'");
            _service.AddMiscItem(1, 1000000, "AL").Cogs.Should().Be(0);
        }

        [Test]
        public void CompleteSale_CategoryLine_IsTaggedEstimatedCogs()
        {
            // M8: estimated COGS lines are tagged so reports can split estimated vs actual.
            _service.AddMiscItem(1, 1000000, "PL");
            _service.AddMiscItem(1, 500000); // plain code "1": no estimate
            var sale = _service.CompleteSale(1500000, 0, 0, "", "", "");

            Text($"SELECT remark FROM sale_items WHERE journal_no = '{sale.JournalNo}' AND product_code = 'PL'")
                .Should().Be(SalesService.EstimatedCogsRemark);
            Text($"SELECT remark FROM sale_items WHERE journal_no = '{sale.JournalNo}' AND product_code = '1'")
                .Should().BeEmpty();
        }

        [Test]
        public void CompleteSale_RecoveredCategoryLine_IsStillTaggedEstimated()
        {
            // pending_sales does not keep the remark; the tag is derived from the code.
            _service.AddMiscItem(1, 1000000, "MY");
            var recovered = new SalesService(_db, new FakeClock(new DateTime(2026, 4, 4, 15, 0, 0)));
            recovered.SetCashier("ADM", 1);
            recovered.RecoverPendingSale();
            var sale = recovered.CompleteSale(1000000, 0, 0, "", "", "");

            Text($"SELECT remark FROM sale_items WHERE journal_no = '{sale.JournalNo}'")
                .Should().Be(SalesService.EstimatedCogsRemark);
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
