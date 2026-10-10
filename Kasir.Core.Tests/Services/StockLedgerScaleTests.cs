using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Data.Sqlite;
using NUnit.Framework;
using FluentAssertions;
using Kasir.Data.Repositories;
using Kasir.Models;
using Kasir.Services;
using Kasir.Tests.TestHelpers;
using Kasir.Tests.TestHelpers.Fakes;

namespace Kasir.Tests.Services
{
    // WP-02: stock_movements.qty_in / qty_out are x100 (FoxPro QIN/QOUT N(13,2)), the
    // scale every legacy GHIST/GSMRY row and every dashboard pos_stock_requests row
    // already uses. The POS used to write plain units, so on-hand mixed scales and the
    // pushed movements were wrong in Supabase. Every POS writer now converts at the
    // caller; val_in / val_out stay money x100 (unit cost x whole units).
    [TestFixture]
    public class StockLedgerScaleTests
    {
        private SqliteConnection _db;
        private FakeClock _clock;
        private StockMovementRepository _movements;

        [SetUp]
        public void SetUp()
        {
            _db = TestDb.Create();
            _clock = new FakeClock(new DateTime(2026, 10, 9, 15, 0, 0));
            _movements = new StockMovementRepository(_db);
            var cfg = new ConfigRepository(_db);
            cfg.Set("register_id", "01");
            cfg.Set(InventoryService.CostEngineOwnsCostPriceKey, "true");
            var products = new ProductRepository(_db);
            foreach (var code in new[] { "P001", "GALON" })
            {
                products.Insert(new Product
                {
                    ProductCode = code, Name = code, Price = 500000, CostPrice = 300000,
                    Status = "A", OpenPrice = "N", VatFlag = "N", LuxuryTaxFlag = "N", IsConsignment = "N"
                });
            }
            new SubsidiaryRepository(_db).Insert(new Subsidiary { SubCode = "V001", Name = "VENDOR", GroupCode = "1", Status = "A" });
        }

        [TearDown]
        public void TearDown()
        {
            _db.Close();
            _db.Dispose();
        }

        // A legacy GHIST-style row: x100 qty, the way the snapshot brings history in.
        private void SeedLegacyStock(string code, int ledgerQty, long unitCost)
        {
            using var cmd = _db.CreateCommand();
            cmd.CommandText =
                @"INSERT INTO stock_movements (product_code, journal_no, movement_type, doc_date, period_code,
                    location_code, qty_in, val_in, cost_price, is_archived)
                  VALUES (@c, 'GHIST-0001', 'PURCHASE', '2026-01-02', '202601', 'T', @q, @v, @cost, 1)";
            cmd.Parameters.AddWithValue("@c", code);
            cmd.Parameters.AddWithValue("@q", ledgerQty);
            cmd.Parameters.AddWithValue("@v", StockQty.Value(unitCost, ledgerQty));
            cmd.Parameters.AddWithValue("@cost", unitCost);
            cmd.ExecuteNonQuery();
        }

        [Test]
        public void StockQty_ToLedger_Value_Format()
        {
            StockQty.ToLedger(2).Should().Be(200);
            StockQty.ToLedger(-3).Should().Be(-300);
            StockQty.Value(300000, 200).Should().Be(600000, "2 units at Rp 3.000");
            StockQty.Value(300000, 50).Should().Be(150000, "half a unit");
            StockQty.Value(333, 50).Should().Be(167, "rounded half away from zero");
            StockQty.Format(1250).Should().Be("12,5");
            StockQty.Format(123450).Should().Be("1.234,5");
            StockQty.Format(-25).Should().Be("-0,25");
            StockQty.Format(0).Should().Be("0");
            StockQty.ToUnits(1299).Should().Be(12);
            Action overflow = () => StockQty.ToLedger(int.MaxValue / 10);
            overflow.Should().Throw<OverflowException>();
        }

        [Test]
        public void Sale_WritesQtyOutX100_AndValueAtUnitCost()
        {
            var sales = new SalesService(_db, _clock);
            sales.SetCashier("ADM", 1);
            sales.AddItem("P001", 2);
            var sale = sales.CompleteSale(10000000, 0, 0, "", "", "");

            var m = _movements.GetByJournal(sale.JournalNo).Single();
            m.MovementType.Should().Be("SALE");
            m.QtyOut.Should().Be(200, "2 units sold = 200 in the x100 ledger");
            m.CostPrice.Should().Be(300000);
            m.ValOut.Should().Be(600000, "val_out is money x100 for 2 whole units");
        }

        [Test]
        public void VoidSale_ReturnsQtyInX100_NetsToZero()
        {
            var sales = new SalesService(_db, _clock);
            sales.SetCashier("ADM", 1);
            sales.AddItem("P001", 3);
            var sale = sales.CompleteSale(10000000, 0, 0, "", "", "");
            sales.VoidSale(sale.JournalNo);

            var ms = _movements.GetByJournal(sale.JournalNo);
            ms.Single(x => x.MovementType == "RETURN_IN").QtyIn.Should().Be(300);
            _movements.GetStockOnHand("P001").Should().Be(0);
            ms.Sum(x => x.ValIn - x.ValOut).Should().Be(0, "return_reverses_stock_and_cost");
        }

        [Test]
        public void Purchase_Receipt_Invoice_Return_WriteX100()
        {
            var purchasing = new PurchasingService(_db, _clock);
            string gr = purchasing.CreateGoodsReceipt(new Purchase { SubCode = "V001" },
                new List<PurchaseItem> { new PurchaseItem { ProductCode = "P001", Quantity = 10, UnitPrice = 300000 } }, 1);
            string inv = purchasing.CreatePurchaseInvoice(new Purchase { SubCode = "V001", DueDate = "2026-11-09" },
                new List<PurchaseItem> { new PurchaseItem { ProductCode = "P001", Quantity = 4, UnitPrice = 300000 } }, 1);
            string ret = purchasing.CreatePurchaseReturn(new Purchase { SubCode = "V001" },
                new List<PurchaseItem> { new PurchaseItem { ProductCode = "P001", Quantity = 1, UnitPrice = 300000 } }, false, 1);

            var r = _movements.GetByJournal(gr).Single();
            r.QtyIn.Should().Be(1000);
            r.ValIn.Should().Be(3000000);
            _movements.GetByJournal(inv).Single().QtyIn.Should().Be(400);
            var o = _movements.GetByJournal(ret).Single();
            o.QtyOut.Should().Be(100);
            o.ValOut.Should().Be(300000);
            _movements.GetStockOnHand("P001").Should().Be(1300, "10 + 4 - 1 units");
        }

        // StockTransferService also converts to x100, but its header insert fails on the
        // current schema (stock_transfers has no from_location column, a pre-existing bug
        // outside WP-02), so it is not exercised here.
        [Test]
        public void StockOut_WritesX100()
        {
            var opname = new StockOpnameService(_db, _clock);
            string otm = opname.CreateStockOut("DAMAGE", "T",
                new List<StockAdjustmentItem> { new StockAdjustmentItem { ProductCode = "P001", Quantity = 2 } }, 1);
            var so = _movements.GetByJournal(otm).Single();
            so.QtyOut.Should().Be(200);
            so.ValOut.Should().Be(600000);

        }

        [Test]
        public void QuickIntake_WritesX100()
        {
            var result = new ProductService(_db, _clock).CreateQuickProduct("Lampu", "MY", 200000, 300000, 4, 1);
            var m = _movements.GetByJournal(result.JournalNo).Single();
            m.QtyIn.Should().Be(400);
            m.ValIn.Should().Be(800000);
        }

        [Test]
        public void LegacyX100Stock_PlusPosSale_IsOneScale()
        {
            SeedLegacyStock("P001", 1000, 300000); // 10 units from the FoxPro history
            var sales = new SalesService(_db, _clock);
            sales.SetCashier("ADM", 1);
            sales.AddItem("P001", 2);
            sales.CompleteSale(10000000, 0, 0, "", "", "");

            _movements.GetStockOnHand("P001").Should().Be(800, "8 units: legacy and POS rows share the x100 scale");
        }

        [Test]
        public void HalfGalon_50_x100_OpnameVarianceIsFractional()
        {
            // half_galon_50_x100: legacy stock 12.5 galon, shelf count 12 -> shortage 0.5.
            SeedLegacyStock("GALON", 1250, 300000);
            var opname = new StockOpnameService(_db, new FakeClock(DateTime.Now.AddHours(1)));
            var line = opname.GetOpnameSheet(100).Single(l => l.ProductCode == "GALON");
            opname.RecordCount(line, StockQty.ToLedger(12));

            line.SystemQty.Should().Be(1250);
            line.Variance.Should().Be(-50);

            string jnl = opname.CreateOpnameAdjustment(new List<OpnameLine> { line }, 1);
            var m = _movements.GetByJournal(jnl).Single();
            m.MovementType.Should().Be("OPNAME");
            m.QtyOut.Should().Be(50);
            m.ValOut.Should().Be(150000, "half a galon at Rp 3.000");
            _movements.GetStockOnHand("GALON").Should().Be(1200);
        }

        [Test]
        public void PerpetualAverage_IsUnchangedByScale()
        {
            // 10 units @3.000 on hand (cost_price seeded), receive 10 @5.000 -> avg 4.000.
            SeedLegacyStock("P001", 1000, 300000);
            new PurchasingService(_db, _clock).CreateGoodsReceipt(new Purchase { SubCode = "V001" },
                new List<PurchaseItem> { new PurchaseItem { ProductCode = "P001", Quantity = 10, UnitPrice = 500000 } }, 1);

            new ProductRepository(_db).GetByCode("P001").CostPrice.Should().Be(400000);
        }
    }
}
