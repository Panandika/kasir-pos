using System;
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
    // PR-K3: an inactive product (status 'I') scanned by barcode still sells - selling
    // never blocks - but the scan is logged once per product per day so the owner can
    // see which "inactive" items are really still on the shelf.
    [TestFixture]
    public class InactiveSaleLogTests
    {
        private SqliteConnection _db;
        private FakeClock _clock;
        private SalesService _service;

        [SetUp]
        public void SetUp()
        {
            _db = TestDb.Create();
            _clock = new FakeClock(new DateTime(2026, 4, 4, 14, 30, 0));
            new ConfigRepository(_db).Set("register_id", "02");
            var repo = new ProductRepository(_db);
            repo.Insert(Product("P-ACT", "BARANG AKTIF", "A"));
            repo.Insert(Product("P-INA", "BARANG NONAKTIF", "I"));
            _service = new SalesService(_db, _clock);
            _service.SetCashier("ADM", 1);
        }

        [TearDown]
        public void TearDown()
        {
            _db.Close();
            _db.Dispose();
        }

        private static Product Product(string code, string name, string status) => new Product
        {
            ProductCode = code, Name = name, Price = 1000000, Status = status,
            OpenPrice = "N", VatFlag = "N", LuxuryTaxFlag = "N", IsConsignment = "N"
        };

        private long Scalar(string sql)
        {
            using var cmd = _db.CreateCommand();
            cmd.CommandText = sql;
            return Convert.ToInt64(cmd.ExecuteScalar());
        }

        [Test]
        public void AddItem_InactiveProduct_SellsAndLogsOnce()
        {
            var first = _service.AddItem("P-INA", 1);
            var second = _service.AddItem("P-INA", 2);

            first.Should().NotBeNull("an inactive product still sells by barcode");
            second.Should().NotBeNull();
            _service.CurrentItems.Should().HaveCount(2);
            Scalar("SELECT COUNT(*) FROM inactive_sale_log").Should().Be(1, "one row per product per day");

            using var cmd = _db.CreateCommand();
            cmd.CommandText = "SELECT product_code, sale_date, register_id FROM inactive_sale_log";
            using var rd = cmd.ExecuteReader();
            rd.Read().Should().BeTrue();
            rd.GetString(0).Should().Be("P-INA");
            rd.GetString(1).Should().Be("2026-04-04");
            rd.GetString(2).Should().Be("02");
        }

        [Test]
        public void AddItem_InactiveProduct_NextDay_LogsAgain()
        {
            _service.AddItem("P-INA", 1);
            _clock.Advance(TimeSpan.FromDays(1));
            _service.AddItem("P-INA", 1);

            Scalar("SELECT COUNT(*) FROM inactive_sale_log").Should().Be(2);
        }

        [Test]
        public void AddItem_ActiveProduct_NoLog()
        {
            _service.AddItem("P-ACT", 1).Should().NotBeNull();
            Scalar("SELECT COUNT(*) FROM inactive_sale_log").Should().Be(0);
        }

        [Test]
        public void AddItem_InactiveProduct_DoesNotQueueSync()
        {
            // M1: the log stays out of sync_queue. Cloud delivery is deferred (the CloudSync
            // worker is a no-op today); a later mirror loads it by natural key.
            long queuedBefore = Scalar("SELECT COUNT(*) FROM sync_queue");

            _service.AddItem("P-INA", 1);

            Scalar("SELECT COUNT(*) FROM inactive_sale_log").Should().Be(1);
            Scalar("SELECT COUNT(*) FROM sync_queue").Should().Be(queuedBefore);
        }

        [Test]
        public void AddItem_InactiveProduct_LogFailure_DoesNotBlockSale()
        {
            using (var cmd = _db.CreateCommand())
            {
                cmd.CommandText = "DROP TABLE inactive_sale_log";
                cmd.ExecuteNonQuery();
            }

            var item = _service.AddItem("P-INA", 1);

            item.Should().NotBeNull("a logging failure must never stop a sale");
            _service.CurrentItems.Should().HaveCount(1);
        }

        [Test]
        public void InactiveProduct_HiddenFromSearch_ButSellableByCode()
        {
            var repo = new ProductRepository(_db);
            // A prefix list hides it; only the full code (a scan) finds it.
            repo.SearchByCodePrefix("P-IN", 10).Should().BeEmpty();
            repo.SearchByName("NONAKTIF", 10).Should().BeEmpty("the FTS path filters status too");
            repo.SearchByText("NONAKTIF", 10).Should().BeEmpty();
            _service.AddItem("P-INA", 1).Should().NotBeNull();
        }

        [Test]
        public void SearchByText_IncludeInactive_FindsInactiveForMasterBarang()
        {
            // M4: Master > Barang must still find an inactive product by name to fix or
            // re-activate it; only the POS search hides it.
            var repo = new ProductRepository(_db);
            repo.SearchByText("NONAKTIF", 10, includeInactive: true)
                .Should().ContainSingle(p => p.ProductCode == "P-INA");
            repo.SearchByText("NONAK", 10, includeInactive: true)
                .Should().Contain(p => p.ProductCode == "P-INA");
        }

        [Test]
        public void CompleteSale_InactiveProduct_Succeeds()
        {
            _service.AddItem("P-INA", 1);
            var sale = _service.CompleteSale(1000000, 0, 0, "", "", "");
            sale.JournalNo.Should().NotBeNullOrEmpty();
        }
    }
}
