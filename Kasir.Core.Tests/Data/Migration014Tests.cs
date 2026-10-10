using Microsoft.Data.Sqlite;
using NUnit.Framework;
using FluentAssertions;
using Kasir.Data.Migrations;
using Kasir.Data.Repositories;
using Kasir.Tests.TestHelpers;

namespace Kasir.Tests.Data
{
    [TestFixture]
    public class Migration014Tests
    {
        private SqliteConnection _db;

        [SetUp]
        public void SetUp() { _db = TestDb.Create(); }

        [TearDown]
        public void TearDown() { _db.Close(); _db.Dispose(); }

        private void Exec(string sql)
        {
            using var cmd = _db.CreateCommand();
            cmd.CommandText = sql;
            cmd.ExecuteNonQuery();
        }

        [Test]
        public void SeedsMissingWatermarks_AndMarksTheX100Cutover()
        {
            Exec("DELETE FROM config WHERE key LIKE 'cloud_push_wm_%'"); // a DB from before WP-02
            Exec(@"INSERT INTO stock_movements (id, product_code, journal_no, movement_type, doc_date, period_code, qty_out)
                   VALUES (41, 'P', 'KLR-01-2610-0001', 'SALE', '2026-10-01', '202610', 2),
                          (5000000003, 'P', 'OPNAME:x', 'OPNAME', '2026-10-01', '202610', 100)");

            new Migration_014().Up(_db);

            var cfg = new ConfigRepository(_db);
            cfg.Get("cloud_push_wm_stock_movements").Should().Be("0");
            cfg.Get("cloud_push_wm_shifts").Should().Be("0");
            cfg.Get("stock_ledger_x100_from_id").Should().Be("42", "dashboard-range ids are ignored");
        }

        [Test]
        public void IsIdempotent_AndKeepsExistingValues()
        {
            var cfg = new ConfigRepository(_db);
            cfg.Set("cloud_push_wm_stock_movements", "900");
            new Migration_014().Up(_db);
            Exec(@"INSERT INTO stock_movements (product_code, journal_no, movement_type, doc_date, period_code, qty_out)
                   VALUES ('P', 'KLR-01-2610-0009', 'SALE', '2026-10-01', '202610', 200)");
            new Migration_014().Up(_db);

            cfg.Get("cloud_push_wm_stock_movements").Should().Be("900");
            cfg.Get("stock_ledger_x100_from_id").Should().Be("1", "the marker is set once, on the first run");
        }
    }
}
