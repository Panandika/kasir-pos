using System.Collections.Generic;
using Microsoft.Data.Sqlite;
using NUnit.Framework;
using FluentAssertions;
using Kasir.Data.Repositories;
using Kasir.Models;
using Kasir.Tests.TestHelpers;

namespace Kasir.Tests.Data
{
    [TestFixture]
    public class SaleRepositoryCashSinceShiftTests
    {
        private SqliteConnection _db;
        private SaleRepository _repo;

        [SetUp]
        public void SetUp()
        {
            _db = TestDb.Create();
            _repo = new SaleRepository(_db);

            var prodRepo = new ProductRepository(_db);
            prodRepo.Insert(new Product
            {
                ProductCode = "P1",
                Name = "Item",
                Price = 1000000,
                CostPrice = 800000,
                Status = "A"
            });
        }

        [TearDown]
        public void TearDown()
        {
            _db.Close();
            _db.Dispose();
        }

        private void InsertSale(string journalNo, string register, string shift,
            long cash, long change, long nonCash, string docType = "SALE", int control = 1)
        {
            var sale = new Sale
            {
                DocType = docType,
                JournalNo = journalNo,
                DocDate = "2026-05-07",
                Cashier = "ADM",
                Shift = shift,
                CashAmount = cash,
                NonCash = nonCash,
                ChangeAmount = change,
                TotalValue = cash - change + nonCash,
                PaymentAmount = cash + nonCash,
                GrossAmount = cash - change + nonCash,
                Control = control,
                PeriodCode = "202605",
                RegisterId = register,
                ChangedBy = 1
            };
            var items = new List<SaleItem>
            {
                new SaleItem
                {
                    ProductCode = "P1",
                    ProductName = "Item",
                    Quantity = 1,
                    UnitPrice = sale.TotalValue,
                    Value = sale.TotalValue,
                    Cogs = 800000
                }
            };
            _repo.Insert(sale, items);
        }

        [Test]
        public void CashOnly_ReturnsNetCash()
        {
            InsertSale("J1", "01", "1", cash: 5000000, change: 0, nonCash: 0);

            _repo.GetCashSinceShift("01", "1", "2000-01-01 00:00:00").Should().Be(5000000);
        }

        [Test]
        public void CashWithChange_ReturnsNet()
        {
            // tendered 10k, change 3k → drawer keeps 7k
            InsertSale("J1", "01", "1", cash: 10000000, change: 3000000, nonCash: 0);

            _repo.GetCashSinceShift("01", "1", "2000-01-01 00:00:00").Should().Be(7000000);
        }

        [Test]
        public void CardOnly_ContributesZero()
        {
            InsertSale("J1", "01", "1", cash: 0, change: 0, nonCash: 5000000);

            _repo.GetCashSinceShift("01", "1", "2000-01-01 00:00:00").Should().Be(0);
        }

        [Test]
        public void Mixed_OnlyCashPortion()
        {
            // 5k cash + 5k card = 10k total; drawer only +5k
            InsertSale("J1", "01", "1", cash: 5000000, change: 0, nonCash: 5000000);

            _repo.GetCashSinceShift("01", "1", "2000-01-01 00:00:00").Should().Be(5000000);
        }

        [Test]
        public void DifferentShift_Excluded()
        {
            InsertSale("J1", "01", "1", cash: 5000000, change: 0, nonCash: 0);
            InsertSale("J2", "01", "2", cash: 9000000, change: 0, nonCash: 0);

            _repo.GetCashSinceShift("01", "1", "2000-01-01 00:00:00").Should().Be(5000000);
            _repo.GetCashSinceShift("01", "2", "2000-01-01 00:00:00").Should().Be(9000000);
        }

        [Test]
        public void DifferentRegister_Excluded()
        {
            InsertSale("J1", "01", "1", cash: 5000000, change: 0, nonCash: 0);
            InsertSale("J2", "02", "1", cash: 9000000, change: 0, nonCash: 0);

            _repo.GetCashSinceShift("01", "1", "2000-01-01 00:00:00").Should().Be(5000000);
        }

        [Test]
        public void VoidedSale_Excluded()
        {
            InsertSale("J1", "01", "1", cash: 5000000, change: 0, nonCash: 0, control: 3);

            _repo.GetCashSinceShift("01", "1", "2000-01-01 00:00:00").Should().Be(0);
        }

        [Test]
        public void EditedAndReplaced_Excluded()
        {
            InsertSale("J1", "01", "1", cash: 5000000, change: 0, nonCash: 0, control: 4);
            InsertSale("J2", "01", "1", cash: 5000000, change: 0, nonCash: 0, control: 5);

            _repo.GetCashSinceShift("01", "1", "2000-01-01 00:00:00").Should().Be(0);
        }

        [Test]
        public void SaleReturn_SubtractsCashRefund()
        {
            InsertSale("J1", "01", "1", cash: 10000000, change: 0, nonCash: 0);
            InsertSale("J2", "01", "1", cash: 3000000, change: 0, nonCash: 0, docType: "SALE_RETURN");

            _repo.GetCashSinceShift("01", "1", "2000-01-01 00:00:00").Should().Be(7000000);
        }

        // F03: two shifts on register 01 both reuse shift number '1' on different days.
        // Without the time window, the closing count for today's shift would include
        // yesterday's cash. The open→close window must isolate each shift.
        [Test]
        public void SameShiftNumber_DifferentDays_AreIsolatedByWindow()
        {
            InsertSale("J1", "01", "1", cash: 5000000, change: 0, nonCash: 0); // "yesterday"
            InsertSale("J2", "01", "1", cash: 8000000, change: 0, nonCash: 0); // "today"

            // Force distinct creation timestamps (Insert stamps changed_at = now).
            using (var cmd = _db.CreateCommand())
            {
                cmd.CommandText = "UPDATE sales SET changed_at = '2026-05-06 10:00:00' WHERE journal_no = 'J1';" +
                                  "UPDATE sales SET changed_at = '2026-05-07 10:00:00' WHERE journal_no = 'J2';";
                cmd.ExecuteNonQuery();
            }

            // Today's shift opened 2026-05-07 08:00 → only J2 (8000000) counts, not J1.
            _repo.GetCashSinceShift("01", "1", "2026-05-07 08:00:00").Should().Be(8000000,
                "yesterday's same-numbered shift must not leak into today's drawer count");

            // Yesterday's closed shift window isolates J1.
            _repo.GetCashSinceShift("01", "1", "2026-05-06 08:00:00", "2026-05-06 23:59:59").Should().Be(5000000);
        }
    }
}
