using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Data.Sqlite;
using NUnit.Framework;
using FluentAssertions;
using Kasir.Data;
using Kasir.Data.Repositories;
using Kasir.Models;
using Kasir.Services;
using Kasir.Tests.TestHelpers;
using Kasir.Tests.TestHelpers.Fakes;

namespace Kasir.Tests.Services
{
    // Item 6: the WP-02 x100 ledger (PR #107) against the merged PR-K2 perpetual moving
    // average and the PR-K6 opname count time. Every service that moves stock writes
    // ledger qty (x100), and the average reads on-hand from that same ledger, so the
    // average must come out the same as it would in whole units, and the on-hand must
    // be the x100 sum. With the cost engine flag OFF (the default until cutover)
    // products.cost_price must never change, whatever moves.
    [TestFixture]
    public class StockLedgerCostEngineTests
    {
        private SqliteConnection _db;
        private FakeClock _clock;
        private StockMovementRepository _movements;
        private ProductRepository _products;
        private ConfigRepository _config;

        [SetUp]
        public void SetUp()
        {
            _db = TestDb.Create();
            _clock = new FakeClock(DateTime.Now);
            _movements = new StockMovementRepository(_db);
            _products = new ProductRepository(_db);
            _config = new ConfigRepository(_db);
            _config.Set("register_id", "01");
            _config.Set(InventoryService.CostEngineOwnsCostPriceKey, "true");
            _products.Insert(new Product
            {
                ProductCode = "P001", Name = "P001", Price = 800000, CostPrice = 300000,
                Status = "A", OpenPrice = "N", VatFlag = "N", LuxuryTaxFlag = "N", IsConsignment = "N"
            });
            new SubsidiaryRepository(_db).Insert(new Subsidiary { SubCode = "V001", Name = "VENDOR", GroupCode = "1", Status = "A" });
        }

        [TearDown]
        public void TearDown()
        {
            _db.Close();
            _db.Dispose();
        }

        private void FlagOff() => _config.Set(InventoryService.CostEngineOwnsCostPriceKey, "false");

        private long Cost() => _products.GetByCode("P001").CostPrice;

        private int OnHand() => _movements.GetStockOnHand("P001");

        private long ProductSyncRows() => SqlHelper.ExecuteScalar<long>(_db,
            "SELECT COUNT(*) FROM sync_queue WHERE table_name = 'products' AND record_key = 'P001'");

        // Opening stock the way the snapshot brings FoxPro history in: an x100 GSMRY row
        // stamped well before any count.
        private void SeedLegacyStock(int ledgerQty, long unitCost)
        {
            SqlHelper.ExecuteNonQuery(_db,
                @"INSERT INTO stock_movements (product_code, journal_no, movement_type, doc_date, period_code,
                    location_code, qty_in, val_in, cost_price, is_archived, changed_at, created_at)
                  VALUES ('P001', 'GSMRY-2601', 'PURCHASE', '2026-01-02', '202601', 'T', @q, @v, @c, 1,
                    '2026-01-02 08:00:00', '2026-01-02 08:00:00')",
                SqlHelper.Param("@q", ledgerQty),
                SqlHelper.Param("@v", StockQty.Value(unitCost, ledgerQty)),
                SqlHelper.Param("@c", unitCost));
        }

        private string Receive(int units, long unitCost)
        {
            return new PurchasingService(_db, _clock).CreateGoodsReceipt(new Purchase { SubCode = "V001" },
                new List<PurchaseItem> { new PurchaseItem { ProductCode = "P001", Quantity = units, UnitPrice = unitCost } }, 1);
        }

        private Sale Sell(int units)
        {
            var sales = new SalesService(_db, _clock);
            sales.SetCashier("ADM", 1);
            sales.AddItem("P001", units);
            return sales.CompleteSale(100000000, 0, 0, "", "", "");
        }

        // ---------- cost engine ON ----------

        [Test]
        public void Purchase_IntoFractionalLegacyStock_WeighsByLedgerQty()
        {
            SeedLegacyStock(1250, 300000); // 12,5 units @ Rp 3.000 (cost_price 300000)

            Receive(10, 500000);

            OnHand().Should().Be(2250, "12,5 + 10 units on the x100 ledger");
            // (12,5 x 3.000 + 10 x 5.000) / 22,5 = 3.888,89 -> 388889 (x100 money)
            Cost().Should().Be(388889);
        }

        [Test]
        public void Sale_CogsAndValOut_UseTheAverage_AndLeaveItUnchanged()
        {
            SeedLegacyStock(1250, 300000);
            Receive(10, 500000);

            var sale = Sell(3);

            OnHand().Should().Be(1950);
            Cost().Should().Be(388889, "a sale never re-weights the average");
            var item = new SaleRepository(_db).GetItemsByJournalNo(sale.JournalNo).Single();
            item.Cogs.Should().Be(388889L * 3, "sale_items.cogs = unit average x plain units");
            var m = _movements.GetByJournal(sale.JournalNo).Single();
            m.QtyOut.Should().Be(300);
            m.CostPrice.Should().Be(388889);
            m.ValOut.Should().Be(item.Cogs, "the GL COGS and the ledger value agree at x100 qty");
        }

        [Test]
        public void PurchaseAfterSale_WeighsAgainstTheReducedOnHand()
        {
            SeedLegacyStock(1000, 300000); // 10 @ 3.000
            Sell(4);                         // 6 left
            Receive(6, 600000);

            OnHand().Should().Be(1200);
            // (6 x 3.000 + 6 x 6.000) / 12 = 4.500
            Cost().Should().Be(450000);
        }

        [Test]
        public void Oversold_ThenPurchase_ResetsToThePurchaseCost()
        {
            SeedLegacyStock(100, 300000); // 1 unit
            Sell(3);                        // on-hand -2
            OnHand().Should().Be(-200);

            Receive(5, 400000);

            OnHand().Should().Be(300);
            Cost().Should().Be(400000, "on-hand <= 0 before a PURCHASE resets the average");
        }

        [Test]
        public void VoidSale_ReturnsX100_AtTheSaleCost()
        {
            SeedLegacyStock(1000, 300000);
            var sale = Sell(4);
            Receive(4, 600000); // 10 on hand, avg (6 x 3.000 + 4 x 6.000)/10 = 4.200
            Cost().Should().Be(420000);

            new SalesService(_db, _clock).VoidSale(sale.JournalNo);

            OnHand().Should().Be(1400);
            var back = _movements.GetByJournal(sale.JournalNo).Single(x => x.MovementType == "RETURN_IN");
            back.QtyIn.Should().Be(400);
            back.CostPrice.Should().Be(300000, "the unit cost the sale went out at");
            back.ValIn.Should().Be(1200000);
            // (10 x 4.200 + 4 x 3.000) / 14 = 3.857,14 -> 385714
            Cost().Should().Be(385714);
        }

        [Test]
        public void Opname_CountTime_SubtractsLaterReceipts_AndValuesAtTheCurrentAverage()
        {
            SeedLegacyStock(1000, 300000);
            var opname = new StockOpnameService(_db, new FakeClock(DateTime.Now.AddHours(-1)));
            var line = opname.GetOpnameSheet(100).Single(l => l.ProductCode == "P001");
            opname.RecordCount(line, StockQty.ToLedger(7)); // shelf seen an hour ago: 7 of 10

            Receive(10, 500000); // after the count; avg (10 x 3.000 + 10 x 5.000)/20 = 4.000
            Cost().Should().Be(400000);

            string jnl = opname.CreateOpnameAdjustment(new List<OpnameLine> { line }, 1);

            line.SystemQty.Should().Be(1000, "on-hand at the count, not now (PR-K6)");
            var m = _movements.GetByJournal(jnl).Single();
            m.MovementType.Should().Be("OPNAME");
            m.QtyOut.Should().Be(300);
            m.CostPrice.Should().Be(400000);
            m.ValOut.Should().Be(1200000, "3 units at Rp 4.000");
            OnHand().Should().Be(1700, "20 - 3 units: the later receipt stays on top of the count");
            Cost().Should().Be(400000, "an opname shortage never re-weights the average");
        }

        [Test]
        public void Opname_Surplus_AtTheAverage_KeepsIt()
        {
            SeedLegacyStock(1250, 300000);
            var opname = new StockOpnameService(_db, new FakeClock(DateTime.Now.AddMinutes(-5)));
            var line = opname.GetOpnameSheet(100).Single(l => l.ProductCode == "P001");
            opname.RecordCount(line, StockQty.ToLedger(14));

            string jnl = opname.CreateOpnameAdjustment(new List<OpnameLine> { line }, 1);

            var m = _movements.GetByJournal(jnl).Single();
            m.QtyIn.Should().Be(150, "14 - 12,5 units");
            m.ValIn.Should().Be(450000);
            OnHand().Should().Be(1400);
            Cost().Should().Be(300000);
            var adj = new StockAdjustmentRepository(_db).GetAllItemsByDateRange("2000-01-01", "2999-12-31").Single();
            adj.Quantity.Should().Be(150, "opname adjustment lines keep the ledger qty");
            adj.Value.Should().Be(450000);
        }

        // ---------- cost engine OFF (default until cutover) ----------

        [Test]
        public void FlagOff_EveryStockMove_LeavesCostPriceAndSyncUntouched()
        {
            FlagOff();
            SeedLegacyStock(1250, 300000);
            long syncBefore = ProductSyncRows();

            Receive(10, 500000);
            var sale = Sell(3);
            new SalesService(_db, _clock).VoidSale(sale.JournalNo);
            new PurchasingService(_db, _clock).CreatePurchaseReturn(new Purchase { SubCode = "V001" },
                new List<PurchaseItem> { new PurchaseItem { ProductCode = "P001", Quantity = 1, UnitPrice = 500000 } }, false, 1);
            // Counted after all of the above (count time later than their changed_at).
            var opname = new StockOpnameService(_db, new FakeClock(DateTime.Now.AddHours(1)));
            var line = opname.GetOpnameSheet(100).Single(l => l.ProductCode == "P001");
            opname.RecordCount(line, StockQty.ToLedger(25));
            opname.CreateOpnameAdjustment(new List<OpnameLine> { line }, 1);

            Cost().Should().Be(300000, "FoxPro AVGCOST owns cost_price while the flag is off");
            ProductSyncRows().Should().Be(syncBefore, "no products row may be queued for the cloud");
            // 12,5 + 10 - 3 + 3 - 1 = 21,5 at the count, counted 25 -> +3,5
            OnHand().Should().Be(2500);
            var sold = _movements.GetByJournal(sale.JournalNo).Single(x => x.MovementType == "SALE");
            sold.CostPrice.Should().Be(300000, "COGS still reads the untouched cost_price");
            sold.ValOut.Should().Be(900000);
        }

        [Test]
        public void FlagOff_CostPriceZero_CogsFallsBackToLastPurchasePrice_StillUntouched()
        {
            FlagOff();
            _products.UpdateCostPrice("P001", 0);
            new PurchasingService(_db, _clock).CreatePurchaseInvoice(new Purchase { SubCode = "V001", DueDate = "2026-12-31" },
                new List<PurchaseItem> { new PurchaseItem { ProductCode = "P001", Quantity = 4, UnitPrice = 250000 } }, 1);

            var sale = Sell(2);

            Cost().Should().Be(0);
            OnHand().Should().Be(200);
            var m = _movements.GetByJournal(sale.JournalNo).Single();
            m.CostPrice.Should().Be(250000, "fallback: last PURCHASE unit_price, per stock unit");
            m.ValOut.Should().Be(500000);
        }
    }
}
