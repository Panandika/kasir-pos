using System;
using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Kasir.CloudSync.Pull;
using Kasir.CloudSync.Tests.TestHelpers;
using Kasir.Data;
using Kasir.Data.Repositories;
using Kasir.Models;
using Kasir.Services;
using Kasir.Utils;
using Microsoft.Data.Sqlite;
using NUnit.Framework;

namespace Kasir.CloudSync.Tests.Pull
{
    // Item 6: dashboard pos_stock_requests carry qty x100 (pcs x100) and unit_cost x100
    // per stock unit. Applied on the hub they share the ledger with POS sales and
    // receipts (WP-02 x100), so the PR-K2 moving average and the PR-K6 count-time rule
    // must give the same answer whichever side moved the stock. Flag OFF: the pull
    // never writes products.cost_price.
    [TestFixture]
    public class PullLedgerCostEngineTests
    {
        private static readonly TimeSpan Wita = TimeSpan.FromHours(8);

        private SqliteConnection _db;
        private PosRequestApplier _applier;
        private ProductRepository _products;
        private ConfigRepository _config;

        private sealed class FixedClock : IClock
        {
            public DateTime Now { get; set; } = new DateTime(2026, 10, 9, 15, 0, 0);
            public DateTime UtcNow => Now.AddHours(-7);
        }

        [SetUp]
        public void SetUp()
        {
            _db = TestDb.Create();
            _config = new ConfigRepository(_db);
            _config.Set("register_id", "01");
            _config.Set(InventoryService.CostEngineOwnsCostPriceKey, "true");
            _products = new ProductRepository(_db);
            _products.Insert(new Product
            {
                ProductCode = "P001", Name = "P001", Price = 800000, CostPrice = 300000,
                Status = "A", OpenPrice = "N", VatFlag = "N", LuxuryTaxFlag = "N", IsConsignment = "N"
            });
            new SubsidiaryRepository(_db).Insert(new Subsidiary { SubCode = "V001", Name = "VENDOR", GroupCode = "1", Status = "A" });
            _applier = new PosRequestApplier(_db);
        }

        [TearDown]
        public void TearDown()
        {
            _db.Close();
            _db.Dispose();
        }

        private static DateTimeOffset At(int hour) => new DateTimeOffset(2026, 10, 9, hour, 0, 0, Wita);

        private long Cost() => _products.GetByCode("P001").CostPrice;
        private int OnHand() => new InventoryService(_db).GetStockOnHand("P001");
        private string Text(string sql) => SqlHelper.ExecuteScalar<string>(_db, sql);
        private long ProductSyncRows() => SqlHelper.ExecuteScalar<long>(_db,
            "SELECT COUNT(*) FROM sync_queue WHERE table_name = 'products' AND record_key = 'P001'");

        private void SeedStock(int ledgerQty)
        {
            SqlHelper.ExecuteNonQuery(_db,
                @"INSERT INTO stock_movements (product_code, journal_no, movement_type, doc_date, period_code,
                    location_code, qty_in, val_in, cost_price, changed_at, created_at)
                  VALUES ('P001', 'GSMRY-2609', 'PURCHASE', '2026-09-30', '202609', 'T', @q, @v, 300000,
                    '2026-09-30 20:00:00', '2026-09-30 20:00:00')",
                SqlHelper.Param("@q", ledgerQty),
                SqlHelper.Param("@v", StockQty.Value(300000, ledgerQty)));
        }

        // Moves a POS-written document's movements to a fixed wall-clock time, the way
        // it would sit on the hub when written at that time.
        private void Stamp(string journalNo, int hour)
        {
            string ts = new DateTime(2026, 10, 9, hour, 0, 0).ToString("yyyy-MM-dd HH:mm:ss");
            SqlHelper.ExecuteNonQuery(_db,
                "UPDATE stock_movements SET created_at = @t, changed_at = @t WHERE journal_no = @j",
                SqlHelper.Param("@t", ts), SqlHelper.Param("@j", journalNo));
        }

        private void PullPurchase(string lineId, int qty, long unitCost, int hour, string doc = "RCV-0001")
        {
            _applier.Apply(new PosStockRequest
            {
                Id = Guid.NewGuid(), RequestKind = "PURCHASE", IdempotencyKey = "PURCHASE:" + lineId,
                ProductCode = "P001", Qty = qty, UnitCost = unitCost, VendorCode = "V001", DocNo = doc,
                PayloadJson = "{\"po_no\":\"PO-0001\"}", HappenedAt = At(hour), CreatedAt = At(hour)
            }).Should().Be(ApplyOutcome.Applied);
        }

        private void PullOpname(int counted, int hour)
        {
            _applier.Apply(new PosStockRequest
            {
                Id = Guid.NewGuid(), RequestKind = "OPNAME", IdempotencyKey = "OPNAME:sess:P001",
                ProductCode = "P001", Qty = counted, UnitCost = 999999, DocNo = "OPN-DB-OKT26",
                HappenedAt = At(hour), CreatedAt = At(16)
            }).Should().Be(ApplyOutcome.Applied);
        }

        private void PullReturn(int qty, int hour)
        {
            _applier.Apply(new PosStockRequest
            {
                Id = Guid.NewGuid(), RequestKind = "RETURN_OUT", IdempotencyKey = "RETURN_OUT:r1",
                ProductCode = "P001", Qty = qty, UnitCost = null, VendorCode = "V001", DocNo = "RTN-0001",
                PayloadJson = "{\"ref_no\":\"RCV-0001\"}", HappenedAt = At(hour), CreatedAt = At(hour)
            }).Should().Be(ApplyOutcome.Applied);
        }

        private string PosSell(int units, int hour)
        {
            var sales = new SalesService(_db, new FixedClock { Now = new DateTime(2026, 10, 9, hour, 0, 0) });
            sales.SetCashier("ADM", 1);
            sales.AddItem("P001", units);
            string jnl = sales.CompleteSale(100000000, 0, 0, "", "", "").JournalNo;
            Stamp(jnl, hour);
            return jnl;
        }

        private string PosReceive(int units, long unitCost, int hour)
        {
            string jnl = new PurchasingService(_db, new FixedClock { Now = new DateTime(2026, 10, 9, hour, 0, 0) })
                .CreateGoodsReceipt(new Purchase { SubCode = "V001" },
                    new List<PurchaseItem> { new PurchaseItem { ProductCode = "P001", Quantity = units, UnitPrice = unitCost } }, 1);
            Stamp(jnl, hour);
            return jnl;
        }

        // ---------- cost engine ON ----------

        [Test]
        public void PullPurchase_FractionalQty_WeighsByLedgerQty()
        {
            SeedStock(1000); // 10 @ 3.000

            PullPurchase("l1", 1050, 500000, 10); // 10,5 pcs @ 5.000

            OnHand().Should().Be(2050);
            // (10 x 3.000 + 10,5 x 5.000) / 20,5 = 4.024,39 -> 402439
            Cost().Should().Be(402439);
            var m = new StockMovementRepository(_db).GetByJournal("RCV-0001").Single();
            m.QtyIn.Should().Be(1050);
            m.ValIn.Should().Be(5250000);
            Text("SELECT quantity || ':' || unit_price || ':' || value || ':' || remark FROM purchase_items")
                .Should().Be("11:500000:5250000:qty 10,5", "the document rounds the units, the value stays exact");
        }

        [Test]
        public void PullPurchase_AfterAPosSale_WeighsAgainstTheMixedLedger()
        {
            SeedStock(1000);
            PosSell(4, 9); // 6 left (POS x100 row)

            PullPurchase("l1", 600, 600000, 10);

            OnHand().Should().Be(1200);
            Cost().Should().Be(450000, "(6 x 3.000 + 6 x 6.000) / 12");

            string jnl = PosSell(2, 11);
            var sold = new StockMovementRepository(_db).GetByJournal(jnl).Single();
            sold.CostPrice.Should().Be(450000, "a POS sale after the pull uses the pulled average");
            sold.ValOut.Should().Be(900000);
            OnHand().Should().Be(1000);
        }

        [Test]
        public void PullPurchase_OntoOversoldStock_ResetsToTheReceiptCost()
        {
            SeedStock(100);
            PosSell(3, 9); // -2

            PullPurchase("l1", 500, 400000, 10);

            OnHand().Should().Be(300);
            Cost().Should().Be(400000);
        }

        [Test]
        public void PullOpname_CountTime_IgnoresALaterPosReceipt_AndKeepsTheAverage()
        {
            SeedStock(1000);                 // 10 @ 3.000 on hand at 09:00
            PosReceive(10, 500000, 15);      // 15:00, after the count: avg 4.000
            Cost().Should().Be(400000);

            PullOpname(700, 9);              // counted 7 at 09:00, applied at 16:00

            var m = new StockMovementRepository(_db).GetByJournal("OPN-DB-OKT26").Single();
            m.QtyOut.Should().Be(300, "on-hand at 09:00 was 10, not the 20 there are now");
            m.CostPrice.Should().Be(400000, "OB-14: the local average when applied; the request's 999999 is ignored");
            m.ValOut.Should().Be(1200000);
            OnHand().Should().Be(1700);
            Cost().Should().Be(400000, "a shortage never re-weights the average");
        }

        [Test]
        public void PullOpname_FractionalSurplus_KeepsTheAverage()
        {
            SeedStock(1250);

            PullOpname(1300, 9); // 13 counted vs 12,5

            var m = new StockMovementRepository(_db).GetByJournal("OPN-DB-OKT26").Single();
            m.QtyIn.Should().Be(50);
            m.ValIn.Should().Be(150000);
            OnHand().Should().Be(1300);
            Cost().Should().Be(300000);
        }

        // ---------- cost engine OFF ----------

        [Test]
        public void FlagOff_PullPurchaseReturnOpname_LeaveCostPriceUntouched()
        {
            _config.Set(InventoryService.CostEngineOwnsCostPriceKey, "false");
            SeedStock(1000);
            long syncBefore = ProductSyncRows();

            PullPurchase("l1", 1000, 500000, 10);
            PullReturn(200, 11);
            PullOpname(2000, 12); // 18 on hand at 12:00, counted 20

            Cost().Should().Be(300000);
            ProductSyncRows().Should().Be(syncBefore, "nothing may queue a products update for the cloud");
            OnHand().Should().Be(2000);
            var ms = new StockMovementRepository(_db);
            ms.GetByJournal("RCV-0001").Single().ValIn.Should().Be(5000000, "the movement keeps the receipt cost");
            var ret = ms.GetByJournal("RTN-0001").Single();
            ret.QtyOut.Should().Be(200);
            ret.CostPrice.Should().Be(300000, "a null unit_cost reads the untouched cost_price");
            var opn = ms.GetByJournal("OPN-DB-OKT26").Single();
            opn.QtyIn.Should().Be(200);
            opn.CostPrice.Should().Be(300000);
        }

        // A dashboard-only product (create_dashboard_product, cost_price 0) whose stock
        // arrived as a dashboard receipt: with the flag off the pull never sets cost_price,
        // so the opname must value the variance at the RECEIPT line's cost, not at 0.
        [Test]
        public void FlagOff_PullOpname_ZeroCostPrice_ValuesAtTheDashboardReceiptCost()
        {
            _config.Set(InventoryService.CostEngineOwnsCostPriceKey, "false");
            SqlHelper.ExecuteNonQuery(_db, "UPDATE products SET cost_price = 0 WHERE product_code = 'P001'");

            PullPurchase("l1", 1000, 280000, 10); // 10 pcs @ Rp 2.800
            Cost().Should().Be(0, "flag off: the receipt does not write cost_price");

            PullOpname(700, 12);                  // counted 7 vs 10

            var opn = new StockMovementRepository(_db).GetByJournal("OPN-DB-OKT26").Single();
            opn.QtyOut.Should().Be(300);
            opn.CostPrice.Should().Be(280000, "the dashboard RECEIPT line is the last known cost");
            opn.ValOut.Should().Be(840000, "3 pcs x Rp 2.800, not Rp 0");
        }
    }
}
