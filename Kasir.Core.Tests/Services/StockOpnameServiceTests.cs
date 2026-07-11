using System;
using System.Collections.Generic;
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

        [SetUp]
        public void SetUp()
        {
            _db = TestDb.Create();
            _service = new StockOpnameService(_db, new FakeClock(new DateTime(2026, 4, 4, 10, 0, 0)));

            new ConfigRepository(_db).Set("register_id", "01");
            new ProductRepository(_db).Insert(new Product
            {
                ProductCode = "P001", Name = "TEST", Price = 500000, BuyingPrice = 300000,
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
                new OpnameLine { ProductCode = "P001", SystemQty = 0, PhysicalQty = 5 }
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
                new OpnameLine { ProductCode = "P001", SystemQty = 0, PhysicalQty = 5 }
            };

            Action act = () => _service.CreateOpnameAdjustment(lines, 1);
            act.Should().Throw<SqliteException>();

            CountMovements("OPNAME").Should().Be(0, "the OPNAME movement must roll back when the document insert fails");
            CountAdjustments().Should().Be(1, "only the pre-seeded row remains — no partial adjustment");
        }
    }
}
