using Microsoft.Data.Sqlite;
using NUnit.Framework;
using FluentAssertions;
using Kasir.Data;
using Kasir.Data.Repositories;
using Kasir.Services;
using Kasir.Tests.TestHelpers;

namespace Kasir.Tests.Services
{
    [TestFixture]
    public class InventoryServiceTests
    {
        private SqliteConnection _db;
        private InventoryService _service;
        private StockMovementRepository _movementRepo;

        [SetUp]
        public void SetUp()
        {
            _db = TestDb.Create();
            _service = new InventoryService(_db);
            _movementRepo = new StockMovementRepository(_db);

            // Seed config
            var configRepo = new ConfigRepository(_db);
            configRepo.Set("register_id", "01");
            configRepo.Set("costing_method", "AVG");
            // Post-cutover mode: the POS cost engine maintains products.cost_price.
            // Flag-OFF (pre-cutover default) behaviour is covered by the *_FlagOff_* tests.
            configRepo.Set(InventoryService.CostEngineOwnsCostPriceKey, "true");
        }

        [TearDown]
        public void TearDown()
        {
            _db.Close();
            _db.Dispose();
        }

        [Test]
        public void GetStockOnHand_NoMovements_ReturnsZero()
        {
            _service.GetStockOnHand("P001").Should().Be(0);
        }

        [Test]
        public void RecordStockIn_IncreasesStock()
        {
            _service.RecordStockIn("P001", 10, 100000, "PURCHASE", "BPB-01-2604-0001", "2026-04-04", 1);

            _service.GetStockOnHand("P001").Should().Be(10);
        }

        [Test]
        public void RecordStockOut_DecreasesStock()
        {
            _service.RecordStockIn("P001", 10, 100000, "PURCHASE", "BPB-01-2604-0001", "2026-04-04", 1);
            _service.RecordStockOut("P001", 3, 100000, "SALE", "KLR-01-2604-0001", "2026-04-04", 1);

            _service.GetStockOnHand("P001").Should().Be(7);
        }

        [Test]
        public void NegativeStock_Allowed()
        {
            _service.RecordStockOut("P001", 5, 100000, "SALE", "KLR-01-2604-0001", "2026-04-04", 1);

            _service.GetStockOnHand("P001").Should().Be(-5);
        }

        [Test]
        public void RecordStockIn_DefaultsLocationToT()
        {
            _service.RecordStockIn("P001", 10, 100000, "PURCHASE", "BPB-01-2604-0001", "2026-04-04", 1);

            var m = _movementRepo.GetByJournal("BPB-01-2604-0001");
            m.Should().ContainSingle();
            m[0].LocationCode.Should().Be("T");
            _service.GetStockOnHandByLocation("P001", "T").Should().Be(10);
        }

        [Test]
        public void RecordStockOut_DefaultsLocationToT()
        {
            _service.RecordStockOut("P001", 3, 100000, "SALE", "KLR-01-2604-0001", "2026-04-04", 1);

            var m = _movementRepo.GetByJournal("KLR-01-2604-0001");
            m.Should().ContainSingle();
            m[0].LocationCode.Should().Be("T");
        }

        // ---- Stock integrity PR-K2: perpetual moving-average cost on products.cost_price ----

        private void SeedProduct(string code, long costPrice = 0)
        {
            new ProductRepository(_db).Insert(new Kasir.Models.Product
            {
                ProductCode = code, Name = "TEST " + code, Price = 500000, CostPrice = costPrice,
                Status = "A", OpenPrice = "N", VatFlag = "N", LuxuryTaxFlag = "N", IsConsignment = "N"
            });
        }

        private long CostPriceOf(string code)
        {
            return new ProductRepository(_db).GetByCode(code).CostPrice;
        }

        [Test]
        public void CalculateAverageCost_SingleLot()
        {
            SeedProduct("P001");
            _service.RecordStockIn("P001", 10, 100000, "PURCHASE", "BPB-01-2604-0001", "2026-04-04", 1);

            _service.CalculateAverageCost("P001").Should().Be(100000);
        }

        [Test]
        public void CalculateAverageCost_MultipleLots()
        {
            SeedProduct("P001");
            _service.RecordStockIn("P001", 10, 100000, "PURCHASE", "BPB-01-2604-0001", "2026-04-04", 1);
            _service.RecordStockIn("P001", 5, 120000, "PURCHASE", "BPB-01-2604-0002", "2026-04-05", 1);

            // (10*100000 + 5*120000) / 15 = 106666.67 -> rounded to 106667
            _service.CalculateAverageCost("P001").Should().Be(106667);
        }

        [Test]
        public void CalculateAverageCost_NoStock_ReturnsZero()
        {
            _service.CalculateAverageCost("P001").Should().Be(0, "unknown product, no purchases");
        }

        [Test]
        public void CalculateAverageCost_ReturnsProductCostPrice_WhenPositive()
        {
            SeedProduct("P001", costPrice: 4500);

            _service.CalculateAverageCost("P001").Should().Be(4500);
        }

        // The legacy SUM(val_in)/SUM(qty_in) path is gone: movements no longer drive the
        // average, products.cost_price does (it also mixed x100 legacy qty with POS units).
        [Test]
        public void CalculateAverageCost_IgnoresPurchaseMovementHistory()
        {
            SeedProduct("P001", costPrice: 4500);
            _movementRepo.Insert(new Kasir.Models.StockMovement
            {
                ProductCode = "P001", JournalNo = "LEGACY-1", MovementType = "PURCHASE",
                DocDate = "2020-01-01", PeriodCode = "202001",
                QtyIn = 1000, ValIn = 10000, CostPrice = 1000
            });

            _service.CalculateAverageCost("P001").Should().Be(4500);
        }

        [Test]
        public void CalculateAverageCost_NoPurchases_FallbackChain()
        {
            SeedProduct("P001", costPrice: 0);

            _service.CalculateAverageCost("P001").Should().Be(0, "cost_price 0 and no purchase history -> 0");
        }

        // D28: the fallback reads the line cost under the shared rule, not unit_price.
        private void SeedLegacyPurchaseLine(string journal, string docDate, long unitPrice, long discValue, long cogs)
        {
            SqlHelper.ExecuteNonQuery(_db,
                @"INSERT INTO purchases (doc_type, journal_no, doc_date, sub_code, total_value, period_code, legacy_source)
                  VALUES ('PURCHASE', @j, @d, 'V001', 0, '202609', 'SM');
                  INSERT INTO purchase_items (journal_no, product_code, quantity, unit_price, disc_value, cogs, value)
                  VALUES (@j, 'P001', 500, @price, @disc, @cogs, 0);",
                SqlHelper.Param("@j", journal), SqlHelper.Param("@d", docDate),
                SqlHelper.Param("@price", unitPrice), SqlHelper.Param("@disc", discValue),
                SqlHelper.Param("@cogs", cogs));
        }

        [Test]
        public void CalculateAverageCost_Fallback_UsesCogs_NotPreDiscountUnitPrice()
        {
            SeedProduct("P001", costPrice: 0);
            SeedLegacyPurchaseLine("DSRI-1", "2026-09-01", unitPrice: 1000000, discValue: 200000, cogs: 800000);

            _service.CalculateAverageCost("P001").Should().Be(800000, "D28: cogs (after discount) when > 0");
        }

        [Test]
        public void CalculateAverageCost_Fallback_CogsZero_UsesUnitPriceMinusDiscValue()
        {
            SeedProduct("P001", costPrice: 0);
            SeedLegacyPurchaseLine("DSRI-1", "2026-09-01", unitPrice: 1000000, discValue: 100000, cogs: 0);

            _service.CalculateAverageCost("P001").Should().Be(900000, "D28 fallback: unit_price - disc_value");
        }

        [Test]
        public void CalculateAverageCost_Fallback_SkipsLinesWhoseRuleCostIsZero()
        {
            SeedProduct("P001", costPrice: 0);
            SeedLegacyPurchaseLine("DSRI-1", "2026-09-01", unitPrice: 700000, discValue: 0, cogs: 0);
            // Later, but fully discounted: cost 0 under the rule, so it is not a price.
            SeedLegacyPurchaseLine("DSRI-2", "2026-09-10", unitPrice: 100000, discValue: 100000, cogs: 0);

            _service.CalculateAverageCost("P001").Should().Be(700000);
        }

        // Dashboard purchases reach the POS as RECEIPT documents (PosRequestApplier),
        // so the fallback must read them too, not only legacy PURCHASE invoices.
        [Test]
        public void CalculateAverageCost_Fallback_ReadsDashboardReceiptLine()
        {
            SeedProduct("P001", costPrice: 0);
            SqlHelper.ExecuteNonQuery(_db,
                @"INSERT INTO purchases (doc_type, journal_no, doc_date, sub_code, total_value, period_code, control, legacy_source)
                  VALUES ('RECEIPT', 'RCV-DB-1', '2026-10-01', 'V001', 0, '202610', 1, 'DASHBOARD');
                  INSERT INTO purchase_items (journal_no, product_code, quantity, unit_price, value)
                  VALUES ('RCV-DB-1', 'P001', 10, 280000, 2800000);");

            _service.CalculateAverageCost("P001").Should().Be(280000);
        }

        [Test]
        public void CalculateAverageCost_Fallback_IgnoresOtherDocTypes()
        {
            SeedProduct("P001", costPrice: 0);
            SqlHelper.ExecuteNonQuery(_db,
                @"INSERT INTO purchases (doc_type, journal_no, doc_date, sub_code, total_value, period_code, control)
                  VALUES ('PURCHASE_RETURN', 'RTN-1', '2026-10-01', 'V001', 0, '202610', 1);
                  INSERT INTO purchase_items (journal_no, product_code, quantity, unit_price, value)
                  VALUES ('RTN-1', 'P001', 1, 990000, 990000);");

            _service.CalculateAverageCost("P001").Should().Be(0, "a return is not a cost source");
        }

        // Legacy hash ids reach ~4.29B (below the dashboard floor): Insert must return the
        // real id, not an int-truncated negative one.
        [Test]
        public void Insert_AfterALegacyIdAboveIntRange_ReturnsTheRealLongId()
        {
            SqlHelper.ExecuteNonQuery(_db,
                @"INSERT INTO stock_movements (id, product_code, journal_no, movement_type, doc_date, period_code, qty_in)
                  VALUES (4290000000, 'P001', 'LEGACY-H', 'PURCHASE', '2026-09-01', '202609', 100)");

            long id = _movementRepo.Insert(new Kasir.Models.StockMovement
            {
                ProductCode = "P001", JournalNo = "KLR-1", MovementType = "SALE",
                DocDate = "2026-10-01", PeriodCode = "202610", QtyOut = 100
            });

            id.Should().Be(4290000001L);
            SqlHelper.ExecuteScalar<long>(_db, "SELECT id FROM stock_movements WHERE journal_no = 'KLR-1'")
                .Should().Be(id);
        }

        [Test]
        public void RecordStockIn_UpdatesCostPrice_PerpetualAvg()
        {
            SeedProduct("P001");

            _service.RecordStockIn("P001", 10, 1000, "PURCHASE", "BPB-1", "2026-04-01", 1);
            CostPriceOf("P001").Should().Be(1000, "first purchase from on-hand 0 sets the cost");

            _service.RecordStockOut("P001", 5, 1000, "SALE", "KLR-1", "2026-04-02", 1);
            CostPriceOf("P001").Should().Be(1000, "a sale does not change the average");

            _service.RecordStockIn("P001", 5, 2000, "PURCHASE", "BPB-2", "2026-04-03", 1);
            // perpetual: (5 on hand * 1000 + 5 * 2000) / 10 = 1500
            // (the deleted SUM/SUM formula would have given 20000/15 = 1333)
            CostPriceOf("P001").Should().Be(1500);
        }

        [Test]
        public void RecordStockIn_NegativeOnHand_ResetsAvg()
        {
            SeedProduct("P001", costPrice: 1000);
            _service.RecordStockOut("P001", 3, 1000, "SALE", "KLR-1", "2026-04-01", 1);
            _service.GetStockOnHand("P001").Should().Be(-3);

            _service.RecordStockIn("P001", 10, 1500, "PURCHASE", "BPB-1", "2026-04-02", 1);

            CostPriceOf("P001").Should().Be(1500, "on-hand <= 0 before the purchase resets the average");
        }

        [Test]
        public void RecordStockIn_ZeroOnHand_ResetsAvg()
        {
            SeedProduct("P001", costPrice: 1000);
            _service.RecordStockIn("P001", 4, 1000, "PURCHASE", "BPB-1", "2026-04-01", 1);
            _service.RecordStockOut("P001", 4, 1000, "SALE", "KLR-1", "2026-04-01", 1);

            _service.RecordStockIn("P001", 2, 3000, "PURCHASE", "BPB-2", "2026-04-02", 1);

            CostPriceOf("P001").Should().Be(3000);
        }

        // On-hand > 0 but no cost basis yet (cost_price 0, e.g. legacy stock without AVGCOST):
        // averaging against 0 would dilute the cost, so the purchase cost is taken as is.
        [Test]
        public void RecordStockIn_NoCostBasis_UsesPurchaseCost()
        {
            SeedProduct("P001", costPrice: 0);
            _movementRepo.Insert(new Kasir.Models.StockMovement
            {
                ProductCode = "P001", JournalNo = "OPN-0", MovementType = "OPNAME",
                DocDate = "2026-04-01", PeriodCode = "202604", QtyIn = 10
            });

            _service.RecordStockIn("P001", 10, 2000, "PURCHASE", "BPB-1", "2026-04-02", 1);

            CostPriceOf("P001").Should().Be(2000);
        }

        // A zero-cost stock-in (e.g. voiding a sale whose COGS was 0) must not wipe the cost.
        [Test]
        public void RecordStockIn_ZeroUnitCost_DoesNotChangeCostPrice()
        {
            SeedProduct("P001", costPrice: 1000);

            _service.RecordStockIn("P001", 2, 0, "RETURN_IN", "KLR-1", "2026-04-02", 1);

            CostPriceOf("P001").Should().Be(1000);
        }

        [Test]
        public void RecordStockOut_DoesNotChangeCostPrice()
        {
            SeedProduct("P001", costPrice: 1000);

            _service.RecordStockOut("P001", 2, 5000, "RETURN_OUT", "RMS-1", "2026-04-02", 1);

            CostPriceOf("P001").Should().Be(1000);
        }

        // Review MEDIUM-1: with on-hand <= 0, only a PURCHASE resets the average. A RETURN_IN
        // (sale void) carries the stale sale-time cost and must not overwrite a newer cost.
        [Test]
        public void RecordStockIn_ReturnIn_NegativeOnHand_KeepsExistingCost()
        {
            SeedProduct("P001", costPrice: 2000);
            _service.RecordStockOut("P001", 5, 1000, "SALE", "KLR-1", "2026-04-01", 1);

            _service.RecordStockIn("P001", 2, 1000, "RETURN_IN", "KLR-1", "2026-04-02", 1);

            CostPriceOf("P001").Should().Be(2000);
        }

        [Test]
        public void RecordStockIn_Opname_ZeroOnHand_KeepsExistingCost()
        {
            SeedProduct("P001", costPrice: 2000);

            _service.RecordStockIn("P001", 3, 1500, "OPNAME", "OPN-1", "2026-04-02", 1);

            CostPriceOf("P001").Should().Be(2000);
        }

        [Test]
        public void RecordStockIn_ReturnIn_NoCostBasis_UsesUnitCost()
        {
            SeedProduct("P001", costPrice: 0);

            _service.RecordStockIn("P001", 2, 1000, "RETURN_IN", "KLR-1", "2026-04-02", 1);

            CostPriceOf("P001").Should().Be(1000, "no known cost: any cost basis beats 0");
        }

        [Test]
        public void RecordStockIn_ReturnIn_PositiveOnHand_StillAverages()
        {
            SeedProduct("P001");
            _service.RecordStockIn("P001", 10, 1000, "PURCHASE", "BPB-1", "2026-04-01", 1);

            _service.RecordStockIn("P001", 10, 2000, "RETURN_IN", "KLR-1", "2026-04-02", 1);

            CostPriceOf("P001").Should().Be(1500);
        }

        // Review MEDIUM-3: pre-cutover FoxPro AVGCOST owns cost_price. With the flag OFF
        // (the default) the POS writes the movement but never touches products.cost_price,
        // so nothing reaches sync_queue / Supabase.
        private long ProductSyncRows(string code)
        {
            return SqlHelper.ExecuteScalar<long>(_db,
                "SELECT COUNT(*) FROM sync_queue WHERE table_name = 'products' AND record_key = @k",
                SqlHelper.Param("@k", code));
        }

        [Test]
        public void RecordStockIn_FlagOff_DoesNotUpdateCostPrice()
        {
            new ConfigRepository(_db).Set(InventoryService.CostEngineOwnsCostPriceKey, "false");
            SeedProduct("P001", costPrice: 1000);
            long syncBefore = ProductSyncRows("P001");

            _service.RecordStockIn("P001", StockQty.ToLedger(10), 3000, "PURCHASE", "BPB-1", "2026-04-02", 1);

            CostPriceOf("P001").Should().Be(1000);
            ProductSyncRows("P001").Should().Be(syncBefore, "no products 'U' row may be queued");
            var m = _movementRepo.GetByJournal("BPB-1");
            m.Should().ContainSingle();
            m[0].ValIn.Should().Be(30000);
            m[0].CostPrice.Should().Be(3000);
        }

        [Test]
        public void RecordStockIn_FlagAbsent_DefaultsOff()
        {
            SqlHelper.ExecuteNonQuery(_db, "DELETE FROM config WHERE key = @k",
                SqlHelper.Param("@k", InventoryService.CostEngineOwnsCostPriceKey));
            SeedProduct("P001", costPrice: 1000);

            _service.RecordStockIn("P001", 10, 3000, "PURCHASE", "BPB-1", "2026-04-02", 1);

            CostPriceOf("P001").Should().Be(1000);
        }

        [Test]
        public void RecordStockIn_FlagOn_UpdatesCostPriceAndQueuesSync()
        {
            SeedProduct("P001", costPrice: 1000);
            long syncBefore = ProductSyncRows("P001");

            _service.RecordStockIn("P001", 10, 3000, "PURCHASE", "BPB-1", "2026-04-02", 1);

            CostPriceOf("P001").Should().Be(3000);
            ProductSyncRows("P001").Should().BeGreaterThan(syncBefore);
        }

        // Flag OFF: COGS still comes from the fallback chain (cost_price, else last purchase).
        [Test]
        public void CalculateAverageCost_FlagOff_UsesExistingCostPrice()
        {
            new ConfigRepository(_db).Set(InventoryService.CostEngineOwnsCostPriceKey, "false");
            SeedProduct("P001", costPrice: 1000);
            _service.RecordStockIn("P001", StockQty.ToLedger(10), 3000, "PURCHASE", "BPB-1", "2026-04-02", 1);

            _service.CalculateAverageCost("P001").Should().Be(1000);
            _service.GetCostPrice("P001", StockQty.ToLedger(2)).Should().Be(2000);
        }

        // EC8: on-hand and the purchase qty are in the same unit (the local ledger's), so the
        // average is a pure ratio; scaling both by 100 (legacy x100 qty) gives the same cost.
        [Test]
        public void StockMovement_QtyScale_AverageIsScaleInvariant()
        {
            SeedProduct("P001");
            SeedProduct("P100");

            _service.RecordStockIn("P001", 10, 1000, "PURCHASE", "BPB-1", "2026-04-01", 1);
            _service.RecordStockIn("P001", 5, 2000, "PURCHASE", "BPB-2", "2026-04-02", 1);
            _service.RecordStockIn("P100", 1000, 1000, "PURCHASE", "BPB-3", "2026-04-01", 1);
            _service.RecordStockIn("P100", 500, 2000, "PURCHASE", "BPB-4", "2026-04-02", 1);

            CostPriceOf("P001").Should().Be(1333);
            CostPriceOf("P100").Should().Be(CostPriceOf("P001"));
        }

        [Test]
        public void CalculateFifoCost_SingleLot()
        {
            _service.RecordStockIn("P001", StockQty.ToLedger(10), 100000, "PURCHASE", "BPB-01-2604-0001", "2026-04-04", 1);

            long cost = _service.CalculateFifoCost("P001", StockQty.ToLedger(5));
            // 5 units at 100,000 each = 500,000
            cost.Should().Be(500000);
        }

        [Test]
        public void CalculateFifoCost_MultipleLots_CrossesBoundary()
        {
            // Lot 1: 10 units at 100,000
            _service.RecordStockIn("P001", StockQty.ToLedger(10), 100000, "PURCHASE", "BPB-01-2604-0001", "2026-04-04", 1);
            // Lot 2: 5 units at 120,000
            _service.RecordStockIn("P001", StockQty.ToLedger(5), 120000, "PURCHASE", "BPB-01-2604-0002", "2026-04-05", 1);

            // Sell 12: should take 10 from lot1 + 2 from lot2
            long cost = _service.CalculateFifoCost("P001", StockQty.ToLedger(12));
            // (10 × 100,000) + (2 × 120,000) = 1,000,000 + 240,000 = 1,240,000
            cost.Should().Be(1240000);
        }

        [Test]
        public void CalculateFifoCost_AfterSomeConsumed()
        {
            _service.RecordStockIn("P001", StockQty.ToLedger(10), 100000, "PURCHASE", "BPB-01-2604-0001", "2026-04-04", 1);
            _service.RecordStockIn("P001", StockQty.ToLedger(5), 120000, "PURCHASE", "BPB-01-2604-0002", "2026-04-05", 1);

            // Sell 8 first (consumed from lot 1)
            _service.RecordStockOut("P001", StockQty.ToLedger(8), 100000, "SALE", "KLR-01-2604-0001", "2026-04-04", 1);

            // Now sell 4 more: should take remaining 2 from lot1 + 2 from lot2
            long cost = _service.CalculateFifoCost("P001", StockQty.ToLedger(4));
            // (2 × 100,000) + (2 × 120,000) = 200,000 + 240,000 = 440,000
            cost.Should().Be(440000);
        }

        [Test]
        public void CalculateVariance_Surplus()
        {
            _service.RecordStockIn("P001", 10, 100000, "PURCHASE", "BPB-01-2604-0001", "2026-04-04", 1);

            var variance = _service.CalculateVariance("P001", 12);

            variance.SystemQty.Should().Be(10);
            variance.PhysicalQty.Should().Be(12);
            variance.Variance.Should().Be(2); // surplus
        }

        [Test]
        public void CalculateVariance_Shortage()
        {
            _service.RecordStockIn("P001", 10, 100000, "PURCHASE", "BPB-01-2604-0001", "2026-04-04", 1);

            var variance = _service.CalculateVariance("P001", 8);

            variance.SystemQty.Should().Be(10);
            variance.PhysicalQty.Should().Be(8);
            variance.Variance.Should().Be(-2); // shortage
        }

        [Test]
        public void CalculateVariance_ExactMatch()
        {
            _service.RecordStockIn("P001", 10, 100000, "PURCHASE", "BPB-01-2604-0001", "2026-04-04", 1);

            var variance = _service.CalculateVariance("P001", 10);

            variance.Variance.Should().Be(0);
            variance.VarianceCost.Should().Be(0);
        }

        [Test]
        public void GetStockOnHandByLocation_FiltersCorrectly()
        {
            // Stock in at location TOKO
            var m1 = new Kasir.Models.StockMovement
            {
                ProductCode = "P001",
                JournalNo = "BPB-01-2604-0001",
                MovementType = "PURCHASE",
                DocDate = "2026-04-04",
                PeriodCode = "202604",
                LocationCode = "TOKO",
                QtyIn = 10,
                CostPrice = 100000,
                ValIn = 1000000
            };
            _movementRepo.Insert(m1);

            // Stock in at location GUDANG
            var m2 = new Kasir.Models.StockMovement
            {
                ProductCode = "P001",
                JournalNo = "BPB-01-2604-0002",
                MovementType = "PURCHASE",
                DocDate = "2026-04-04",
                PeriodCode = "202604",
                LocationCode = "GUDANG",
                QtyIn = 5,
                CostPrice = 100000,
                ValIn = 500000
            };
            _movementRepo.Insert(m2);

            _service.GetStockOnHandByLocation("P001", "TOKO").Should().Be(10);
            _service.GetStockOnHandByLocation("P001", "GUDANG").Should().Be(5);
        }
    }
}
