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
        public void CalculateAverageCost_SingleLot()
        {
            _service.RecordStockIn("P001", 10, 100000, "PURCHASE", "BPB-01-2604-0001", "2026-04-04", 1);

            _service.CalculateAverageCost("P001").Should().Be(100000);
        }

        [Test]
        public void CalculateAverageCost_MultipleLots()
        {
            // 10 units at 100,000 + 5 units at 120,000 = 1,600,000 / 15 = 106,666
            _service.RecordStockIn("P001", 10, 100000, "PURCHASE", "BPB-01-2604-0001", "2026-04-04", 1);
            _service.RecordStockIn("P001", 5, 120000, "PURCHASE", "BPB-01-2604-0002", "2026-04-05", 1);

            long avg = _service.CalculateAverageCost("P001");
            // (10*100000 + 5*120000) / 15 = 1600000 / 15 = 106666
            avg.Should().Be(106666);
        }

        [Test]
        public void CalculateAverageCost_NoStock_ReturnsZero()
        {
            _service.CalculateAverageCost("P001").Should().Be(0);
        }

        [Test]
        public void CalculateFifoCost_SingleLot()
        {
            _service.RecordStockIn("P001", 10, 100000, "PURCHASE", "BPB-01-2604-0001", "2026-04-04", 1);

            long cost = _service.CalculateFifoCost("P001", 5);
            // 5 units at 100,000 each = 500,000
            cost.Should().Be(500000);
        }

        [Test]
        public void CalculateFifoCost_MultipleLots_CrossesBoundary()
        {
            // Lot 1: 10 units at 100,000
            _service.RecordStockIn("P001", 10, 100000, "PURCHASE", "BPB-01-2604-0001", "2026-04-04", 1);
            // Lot 2: 5 units at 120,000
            _service.RecordStockIn("P001", 5, 120000, "PURCHASE", "BPB-01-2604-0002", "2026-04-05", 1);

            // Sell 12: should take 10 from lot1 + 2 from lot2
            long cost = _service.CalculateFifoCost("P001", 12);
            // (10 × 100,000) + (2 × 120,000) = 1,000,000 + 240,000 = 1,240,000
            cost.Should().Be(1240000);
        }

        [Test]
        public void CalculateFifoCost_AfterSomeConsumed()
        {
            _service.RecordStockIn("P001", 10, 100000, "PURCHASE", "BPB-01-2604-0001", "2026-04-04", 1);
            _service.RecordStockIn("P001", 5, 120000, "PURCHASE", "BPB-01-2604-0002", "2026-04-05", 1);

            // Sell 8 first (consumed from lot 1)
            _service.RecordStockOut("P001", 8, 100000, "SALE", "KLR-01-2604-0001", "2026-04-04", 1);

            // Now sell 4 more: should take remaining 2 from lot1 + 2 from lot2
            long cost = _service.CalculateFifoCost("P001", 4);
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
