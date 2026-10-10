using System;
using Microsoft.Data.Sqlite;
using NUnit.Framework;
using FluentAssertions;
using Kasir.Data;
using Kasir.Data.Repositories;
using Kasir.Models;
using Kasir.Services;
using Kasir.Tests.TestHelpers;

namespace Kasir.Tests.Data
{
    // WP-04 / OB-13: dashboard-originated movements live at ids >= 5,000,000,000.
    // stock_movements.id is a plain INTEGER PRIMARY KEY, so without an explicit id
    // SQLite would hand the next POS row MAX(id)+1 = 5,000,000,001 once one pulled row
    // exists: the POS sale would sit in the reserved range, collide with the next
    // pulled id and never be cloud-pushed (WatermarkPusher only pushes id < floor).
    [TestFixture]
    public class DashboardMovementIdTests
    {
        private SqliteConnection _db;
        private StockMovementRepository _repo;

        [SetUp]
        public void SetUp()
        {
            _db = TestDb.Create();
            _repo = new StockMovementRepository(_db);
        }

        [TearDown]
        public void TearDown() { _db.Close(); _db.Dispose(); }

        private static StockMovement Sale(string journal) => new StockMovement
        {
            ProductCode = "P001", JournalNo = journal, MovementType = "SALE",
            DocDate = "2026-10-09", PeriodCode = "202610", QtyOut = 100, LocationCode = "T"
        };

        private long MaxId() => SqlHelper.ExecuteScalar<long>(_db, "SELECT MAX(id) FROM stock_movements");

        [Test]
        public void PosInsert_AfterADashboardRow_StaysBelowTheReservedRange()
        {
            _repo.Insert(Sale("KLR-01-2610-0001"));
            long first = MaxId();
            _repo.InsertWithId(Sale("OPN-DB"), StockMovementRepository.DashboardIdFloor, "2026-10-09 09:00:00");

            _repo.Insert(Sale("KLR-01-2610-0002"));

            long posId = SqlHelper.ExecuteScalar<long>(_db,
                "SELECT id FROM stock_movements WHERE journal_no = 'KLR-01-2610-0002'");
            posId.Should().Be(first + 1);
            posId.Should().BeLessThan(StockMovementRepository.DashboardIdFloor);
        }

        [Test]
        public void PosInsert_OnAnEmptyLedger_StartsAtOne()
        {
            _repo.Insert(Sale("KLR-01-2610-0001")).Should().Be(1);
        }

        [Test]
        public void InsertWithId_UsesTheIdAndTheEventTime()
        {
            long id = _repo.InsertWithId(Sale("OPN-DB"), 5_000_000_007L, "2026-10-09 09:15:00");

            id.Should().Be(5_000_000_007L);
            SqlHelper.ExecuteScalar<string>(_db,
                "SELECT changed_at || '|' || created_at FROM stock_movements WHERE id = 5000000007")
                .Should().Be("2026-10-09 09:15:00|2026-10-09 09:15:00");
        }

        [Test]
        public void Readers_HandleIdsAboveInt32()
        {
            _repo.InsertWithId(Sale("OPN-DB"), 5_000_000_001L, "2026-10-09 09:00:00");
            var p = new StockMovement
            {
                ProductCode = "P001", JournalNo = "RCV-1", MovementType = "PURCHASE",
                DocDate = "2026-10-09", PeriodCode = "202610", QtyIn = 100, ValIn = 1000, LocationCode = "T"
            };
            _repo.InsertWithId(p, 5_000_000_002L, "2026-10-09 10:00:00");

            _repo.GetByJournal("OPN-DB").Should().ContainSingle().Which.Id.Should().Be(5_000_000_001L);
            _repo.GetPurchaseMovements("P001").Should().ContainSingle().Which.Id.Should().Be(5_000_000_002L);
            _repo.GetByProduct("P001", "2026-10-01", "2026-10-31").Should().HaveCount(2);
        }

        [Test]
        public void InventoryService_WithPlacement_WritesThePlacedRow_AndKeepsTheAverageEngine()
        {
            var cfg = new ConfigRepository(_db);
            cfg.Set(InventoryService.CostEngineOwnsCostPriceKey, "true");
            new ProductRepository(_db).Insert(new Product
            {
                ProductCode = "P001", Name = "P001", Price = 500000, CostPrice = 300000,
                Status = "A", OpenPrice = "N", VatFlag = "N", LuxuryTaxFlag = "N", IsConsignment = "N"
            });
            var inv = new InventoryService(_db);
            inv.RecordStockIn("P001", 1000, 300000, "PURCHASE", "OLD", "2026-10-01", 0); // 10 @ 3,000
            inv.RecordStockIn("P001", 1000, 500000, "PURCHASE", "RCV-1", "2026-10-09", 0,
                new MovementPlacement { Id = 5_000_000_000L, MovedAt = "2026-10-09 10:00:00" }); // 10 @ 5,000

            SqlHelper.ExecuteScalar<long>(_db, "SELECT qty_in FROM stock_movements WHERE id = 5000000000")
                .Should().Be(1000);
            new ProductRepository(_db).GetByCode("P001").CostPrice.Should().Be(400000, "moving average of 10@3000 + 10@5000");
            inv.GetStockOnHand("P001").Should().Be(2000);
        }
    }
}
