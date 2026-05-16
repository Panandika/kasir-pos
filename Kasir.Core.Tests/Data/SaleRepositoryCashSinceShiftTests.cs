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

            _repo.GetCashSinceShift("01", "1").Should().Be(5000000);
        }

        [Test]
        public void CashWithChange_ReturnsNet()
        {
            // tendered 10k, change 3k → drawer keeps 7k
            InsertSale("J1", "01", "1", cash: 10000000, change: 3000000, nonCash: 0);

            _repo.GetCashSinceShift("01", "1").Should().Be(7000000);
        }

        [Test]
        public void CardOnly_ContributesZero()
        {
            InsertSale("J1", "01", "1", cash: 0, change: 0, nonCash: 5000000);

            _repo.GetCashSinceShift("01", "1").Should().Be(0);
        }

        [Test]
        public void Mixed_OnlyCashPortion()
        {
            // 5k cash + 5k card = 10k total; drawer only +5k
            InsertSale("J1", "01", "1", cash: 5000000, change: 0, nonCash: 5000000);

            _repo.GetCashSinceShift("01", "1").Should().Be(5000000);
        }

        [Test]
        public void DifferentShift_Excluded()
        {
            InsertSale("J1", "01", "1", cash: 5000000, change: 0, nonCash: 0);
            InsertSale("J2", "01", "2", cash: 9000000, change: 0, nonCash: 0);

            _repo.GetCashSinceShift("01", "1").Should().Be(5000000);
            _repo.GetCashSinceShift("01", "2").Should().Be(9000000);
        }

        [Test]
        public void DifferentRegister_Excluded()
        {
            InsertSale("J1", "01", "1", cash: 5000000, change: 0, nonCash: 0);
            InsertSale("J2", "02", "1", cash: 9000000, change: 0, nonCash: 0);

            _repo.GetCashSinceShift("01", "1").Should().Be(5000000);
        }

        [Test]
        public void VoidedSale_Excluded()
        {
            InsertSale("J1", "01", "1", cash: 5000000, change: 0, nonCash: 0, control: 3);

            _repo.GetCashSinceShift("01", "1").Should().Be(0);
        }

        [Test]
        public void EditedAndReplaced_Excluded()
        {
            InsertSale("J1", "01", "1", cash: 5000000, change: 0, nonCash: 0, control: 4);
            InsertSale("J2", "01", "1", cash: 5000000, change: 0, nonCash: 0, control: 5);

            _repo.GetCashSinceShift("01", "1").Should().Be(0);
        }

        [Test]
        public void SaleReturn_SubtractsCashRefund()
        {
            InsertSale("J1", "01", "1", cash: 10000000, change: 0, nonCash: 0);
            InsertSale("J2", "01", "1", cash: 3000000, change: 0, nonCash: 0, docType: "SALE_RETURN");

            _repo.GetCashSinceShift("01", "1").Should().Be(7000000);
        }
    }
}
