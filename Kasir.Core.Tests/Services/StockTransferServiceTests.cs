using System;
using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Kasir.Data.Repositories;
using Kasir.Models;
using Kasir.Services;
using Kasir.Tests.TestHelpers;
using Kasir.Tests.TestHelpers.Fakes;
using Kasir.Utils;
using Microsoft.Data.Sqlite;
using NUnit.Framework;

namespace Kasir.Tests.Services
{
    // CreateTransfer writes a header plus a TRANSFER_OUT / TRANSFER_IN pair per item.
    // It must be atomic: a failure between the two movements may not leave stock
    // removed at the source without it arriving at the destination.
    [TestFixture]
    public class StockTransferServiceTests
    {
        private SqliteConnection _db;
        private FakeClock _clock;

        [SetUp]
        public void SetUp()
        {
            _db = TestDb.Create();
            _clock = new FakeClock(new DateTime(2026, 10, 9, 15, 0, 0));
            new ConfigRepository(_db).Set("register_id", "01");
            new ProductRepository(_db).Insert(new Product
            {
                ProductCode = "P001", Name = "P001", Price = 500000, CostPrice = 300000,
                Status = "A", OpenPrice = "N", VatFlag = "N", LuxuryTaxFlag = "N", IsConsignment = "N"
            });
            Exec(@"INSERT INTO stock_movements (product_code, journal_no, movement_type, doc_date, period_code,
                     location_code, qty_in, val_in, cost_price)
                   VALUES ('P001', 'GHIST-0001', 'PURCHASE', '2026-01-02', '202601', 'T', 1000, 3000000, 300000)");

            // StockTransferRepository writes from_location / to_location / cost_price, which the
            // shipped Schema.sql does not have yet (a separate pre-existing gap: the header insert
            // fails before any movement today). Add them here so the movement path is reachable.
            Exec("ALTER TABLE stock_transfers ADD COLUMN from_location TEXT");
            Exec("ALTER TABLE stock_transfers ADD COLUMN to_location TEXT");
            Exec("ALTER TABLE stock_transfer_items ADD COLUMN cost_price INTEGER DEFAULT 0");
        }

        [TearDown]
        public void TearDown()
        {
            _db.Close();
            _db.Dispose();
        }

        private void Exec(string sql)
        {
            using var cmd = _db.CreateCommand();
            cmd.CommandText = sql;
            cmd.ExecuteNonQuery();
        }

        private long Scalar(string sql)
        {
            using var cmd = _db.CreateCommand();
            cmd.CommandText = sql;
            return Convert.ToInt64(cmd.ExecuteScalar());
        }

        private static List<StockTransferItem> TwoUnits() =>
            new List<StockTransferItem> { new StockTransferItem { ProductCode = "P001", Quantity = 2 } };

        [Test]
        public void CreateTransfer_WritesHeaderAndBothMovements()
        {
            string jnl = new StockTransferService(_db, _clock).CreateTransfer("T", "G", TwoUnits(), 1);

            Scalar($"SELECT count(*) FROM stock_transfers WHERE journal_no = '{jnl}'").Should().Be(1);
            Scalar($"SELECT qty_out FROM stock_movements WHERE journal_no = '{jnl}' AND movement_type = 'TRANSFER_OUT'")
                .Should().Be(200);
            Scalar($"SELECT qty_in FROM stock_movements WHERE journal_no = '{jnl}' AND movement_type = 'TRANSFER_IN'")
                .Should().Be(200);
        }

        [Test]
        public void FailureOnTransferIn_RollsBackHeaderAndTransferOut()
        {
            Exec(@"CREATE TRIGGER fail_transfer_in BEFORE INSERT ON stock_movements
                   WHEN NEW.movement_type = 'TRANSFER_IN'
                   BEGIN SELECT RAISE(ABORT, 'simulated failure between the paired movements'); END;");
            long movementsBefore = Scalar("SELECT count(*) FROM stock_movements");

            Action act = () => new StockTransferService(_db, _clock).CreateTransfer("T", "G", TwoUnits(), 1);

            act.Should().Throw<SqliteException>();
            Scalar("SELECT count(*) FROM stock_movements").Should().Be(movementsBefore,
                "the TRANSFER_OUT must not stay behind without its TRANSFER_IN");
            Scalar("SELECT count(*) FROM stock_movements WHERE movement_type = 'TRANSFER_OUT'").Should().Be(0);
            Scalar("SELECT count(*) FROM stock_transfers").Should().Be(0, "the header rolls back with the movements");
            Scalar("SELECT count(*) FROM stock_transfer_items").Should().Be(0);
            new StockMovementRepository(_db).GetStockOnHand("P001").Should().Be(1000);
        }
    }
}
