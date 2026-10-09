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
    // F21: CreateOpnameAdjustment wrote the OPNAME stock movements in the loop BEFORE
    // (and outside any transaction with) the adjustment document insert. A failure on the
    // header left orphaned OPNAME movements with no adjustment record. The movements and
    // the document must now be atomic.
    [TestFixture]
    public class StockOpnameServiceTests
    {
        private SqliteConnection _db;
        private StockOpnameService _service;
        private InventoryService _inventory;
        private static readonly DateTime CountTime = new DateTime(2026, 4, 4, 10, 0, 0);

        [SetUp]
        public void SetUp()
        {
            _db = TestDb.Create();
            _service = new StockOpnameService(_db, new FakeClock(CountTime));
            _inventory = new InventoryService(_db);

            new ConfigRepository(_db).Set("register_id", "01");
            new ProductRepository(_db).Insert(new Product
            {
                ProductCode = "P001", Name = "TEST", Price = 500000, BuyingPrice = 300000,
                Status = "A", OpenPrice = "N", VatFlag = "N", LuxuryTaxFlag = "N", IsConsignment = "N"
            });
            new ProductRepository(_db).Insert(new Product
            {
                ProductCode = "P002", Name = "TEST 2", Price = 500000, BuyingPrice = 300000,
                Status = "A", OpenPrice = "N", VatFlag = "N", LuxuryTaxFlag = "N", IsConsignment = "N"
            });
        }

        [TearDown]
        public void TearDown()
        {
            _db?.Close();
            _db?.Dispose();
        }

        private int CountMovements(string type)
        {
            using var cmd = _db.CreateCommand();
            cmd.CommandText = "SELECT COUNT(*) FROM stock_movements WHERE movement_type = @t";
            cmd.Parameters.AddWithValue("@t", type);
            return Convert.ToInt32(cmd.ExecuteScalar());
        }

        private int CountAdjustments()
        {
            using var cmd = _db.CreateCommand();
            cmd.CommandText = "SELECT COUNT(*) FROM stock_adjustments";
            return Convert.ToInt32(cmd.ExecuteScalar());
        }

        [Test]
        public void CreateOpnameAdjustment_WritesMovementAndDocument()
        {
            var lines = new List<OpnameLine>
            {
                new OpnameLine { ProductCode = "P001", PhysicalQty = 5, CountTime = CountTime }
            };

            string jnl = _service.CreateOpnameAdjustment(lines, 1);

            // CounterRepository uses the real clock (no injected IClock), so the journal
            // carries the current yyMM. Pin the default format shape.
            jnl.Should().Be($"OPN-01-{DateTime.Now:yyMM}-0001");
            CountMovements("OPNAME").Should().Be(1);
            CountAdjustments().Should().Be(1);
        }

        [Test]
        public void CreateOpnameAdjustment_HeaderFailure_RollsBackMovements()
        {
            // Pre-seed a row with the journal_no the counter will generate (real-clock
            // yyMM) so the adjustment header insert hits UNIQUE(journal_no) AFTER the
            // OPNAME movement is written.
            string collidingJnl = $"OPN-01-{DateTime.Now:yyMM}-0001";
            using (var cmd = _db.CreateCommand())
            {
                cmd.CommandText =
                    "INSERT INTO stock_adjustments (doc_type, journal_no, doc_date, control, period_code, register_id, changed_by) " +
                    "VALUES ('OPNAME', @jnl, '2026-04-04', 1, '202604', '01', 1)";
                cmd.Parameters.AddWithValue("@jnl", collidingJnl);
                cmd.ExecuteNonQuery();
            }

            var lines = new List<OpnameLine>
            {
                new OpnameLine { ProductCode = "P001", PhysicalQty = 5, CountTime = CountTime }
            };

            Action act = () => _service.CreateOpnameAdjustment(lines, 1);
            act.Should().Throw<SqliteException>();

            CountMovements("OPNAME").Should().Be(0, "the OPNAME movement must roll back when the document insert fails");
            CountAdjustments().Should().Be(1, "only the pre-seeded row remains — no partial adjustment");
        }

        // Movement written now, then stamped at a fixed local time so it falls before or
        // after the 10:00 count (changed_at is what GetMovementsSince compares).
        private void Move(string jnl, string code, int qtyIn, int qtyOut, string changedAt)
        {
            if (qtyIn > 0) _inventory.RecordStockIn(code, qtyIn, 300000, "PURCHASE", jnl, "2026-04-04", 1);
            else _inventory.RecordStockOut(code, qtyOut, 300000, "SALE", jnl, "2026-04-04", 1);
            using var cmd = _db.CreateCommand();
            cmd.CommandText = "UPDATE stock_movements SET changed_at = @t WHERE journal_no = @j";
            cmd.Parameters.AddWithValue("@t", changedAt);
            cmd.Parameters.AddWithValue("@j", jnl);
            cmd.ExecuteNonQuery();
        }

        private OpnameLine Line(string code) =>
            _service.GetOpnameSheet(100).Single(l => l.ProductCode == code);

        [Test]
        public void GetOpnameSheet_DoesNotPreCaptureSystemQty_LinesStartUncounted()
        {
            Move("B1", "P001", 10, 0, "2026-04-04 09:00:00");
            var line = Line("P001");
            line.IsCounted.Should().BeFalse();
            line.CountTime.Should().BeNull();
            line.SystemQty.Should().Be(0, "system qty is taken per line when it is counted");
        }

        [Test]
        public void RecordCount_StampsCountTime_AndTakesSystemQtyAtThatMoment()
        {
            Move("B1", "P001", 10, 0, "2026-04-04 09:00:00");
            var line = Line("P001");

            _service.RecordCount(line, 8);

            line.PhysicalQty.Should().Be(8);
            line.CountTime.Should().Be(CountTime);
            line.IsCounted.Should().BeTrue();
            line.SystemQty.Should().Be(10);
            line.Variance.Should().Be(-2);
        }

        [Test]
        public void Opname_SaleAfterCount_AdjustsVariance()
        {
            Move("B1", "P001", 10, 0, "2026-04-04 09:00:00");
            var line = Line("P001");
            _service.RecordCount(line, 10);             // shelf matches at 10:00
            Move("S1", "P001", 0, 2, "2026-04-04 10:30:00"); // sold after the count

            _service.CreateOpnameAdjustment(new List<OpnameLine> { line }, 1);

            CountMovements("OPNAME").Should().Be(0, "the later sale is not a shortage");
            _inventory.GetStockOnHand("P001").Should().Be(8);
        }

        [Test]
        public void Opname_PurchaseAfterCount_AdjustsVariance()
        {
            Move("B1", "P001", 10, 0, "2026-04-04 09:00:00");
            var line = Line("P001");
            _service.RecordCount(line, 7);              // 3 missing at 10:00
            Move("B2", "P001", 5, 0, "2026-04-04 10:30:00"); // delivery after the count

            _service.CreateOpnameAdjustment(new List<OpnameLine> { line }, 1);

            line.SystemQty.Should().Be(10, "on-hand at count time = now (15) minus the later +5");
            line.Variance.Should().Be(-3);
            _inventory.GetStockOnHand("P001").Should().Be(12, "counted 7 + 5 received afterwards");
        }

        [Test]
        public void Opname_MovementBeforeCount_IsNotSubtracted()
        {
            Move("B1", "P001", 10, 0, "2026-04-04 09:00:00");
            Move("S1", "P001", 0, 4, "2026-04-04 09:59:59");
            var line = Line("P001");
            _service.RecordCount(line, 6);

            _service.CreateOpnameAdjustment(new List<OpnameLine> { line }, 1);

            CountMovements("OPNAME").Should().Be(0);
            _inventory.GetStockOnHand("P001").Should().Be(6);
        }

        [Test]
        public void Opname_UncountedProduct_NoMovement()
        {
            Move("B1", "P001", 10, 0, "2026-04-04 09:00:00");
            Move("B2", "P002", 4, 0, "2026-04-04 09:00:00");
            var sheet = _service.GetOpnameSheet(100);
            _service.RecordCount(sheet.Single(l => l.ProductCode == "P001"), 9);

            _service.CreateOpnameAdjustment(sheet, 1);

            CountMovements("OPNAME").Should().Be(1, "only the counted product is adjusted");
            _inventory.GetStockOnHand("P002").Should().Be(4, "belum dihitung: stock left as it is");
            sheet.Single(l => l.ProductCode == "P002").IsCounted.Should().BeFalse();
        }
    }
}
