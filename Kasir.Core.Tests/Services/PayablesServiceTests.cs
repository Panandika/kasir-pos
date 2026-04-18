using System;
using System.Collections.Generic;
using Microsoft.Data.Sqlite;
using NUnit.Framework;
using FluentAssertions;
using Kasir.Data;
using Kasir.Data.Repositories;
using Kasir.Models;
using Kasir.Services;
using Kasir.Tests.TestHelpers;

namespace Kasir.Tests.Services
{
    [TestFixture]
    public class PayablesServiceTests
    {
        private SqliteConnection _db;
        private PayablesService _service;
        private PayablesRepository _payablesRepo;
        private AccountRepository _accountRepo;
        private GlDetailRepository _glRepo;

        [SetUp]
        public void SetUp()
        {
            _db = TestDb.Create();
            _service = new PayablesService(_db);
            _payablesRepo = new PayablesRepository(_db);
            _accountRepo = new AccountRepository(_db);
            _glRepo = new GlDetailRepository(_db);

            var configRepo = new ConfigRepository(_db);
            configRepo.Set("register_id", "01");
            configRepo.Set("ACCOUNT_CASH", "1100");
            configRepo.Set("ACCOUNT_PAYABLES", "2100");

            // Seed accounts
            SeedAccount("1100", "Cash", 1, "D");
            SeedAccount("2100", "Accounts Payable", 2, "K");

            // Seed vendor
            var vendorRepo = new SubsidiaryRepository(_db);
            vendorRepo.Insert(new Subsidiary
            {
                SubCode = "V001", Name = "TEST VENDOR", GroupCode = "1", Status = "A"
            });
            vendorRepo.Insert(new Subsidiary
            {
                SubCode = "V002", Name = "VENDOR TWO", GroupCode = "1", Status = "A"
            });

            // Seed fiscal period
            var periodRepo = new FiscalPeriodRepository(_db);
            periodRepo.EnsurePeriod("202604", 2026, 4);
        }

        [TearDown]
        public void TearDown()
        {
            _db.Close();
            _db.Dispose();
        }

        // --- GetOutstanding ---

        [Test]
        public void GetOutstanding_ReturnsUnpaidInvoices()
        {
            InsertPayable("V001", "MSK-0001", 1000000, "2026-04-01", "2026-05-01");
            InsertPayable("V001", "MSK-0002", 500000, "2026-04-02", "2026-05-02");

            var outstanding = _service.GetOutstanding("V001");
            outstanding.Should().HaveCount(2);
        }

        [Test]
        public void GetOutstanding_ExcludesPaid()
        {
            InsertPayable("V001", "MSK-0003", 100000, "2026-04-01", "2026-05-01");
            _payablesRepo.RecordPayment("MSK-0003", 100000);

            var outstanding = _service.GetOutstanding("V001");
            outstanding.Should().HaveCount(0);
        }

        // --- AllocatePayment ---

        [Test]
        public void AllocatePayment_FullPayment_MarksAsPaid()
        {
            InsertPayable("V001", "MSK-0010", 500000, "2026-04-01", "2026-05-01");

            var result = _service.AllocatePayment("V001", 500000, "1100",
                "2026-04-04", "202604", 1, null);

            result.AmountAllocated.Should().Be(500000);
            result.AmountRemaining.Should().Be(0);
            result.InvoicesPaid.Should().Be(1);
            result.InvoicesPartiallyPaid.Should().Be(0);

            var entry = _payablesRepo.GetByJournalNo("MSK-0010");
            entry.IsPaid.Should().Be("Y");
        }

        [Test]
        public void AllocatePayment_PartialPayment_UpdatesBalance()
        {
            InsertPayable("V001", "MSK-0011", 500000, "2026-04-01", "2026-05-01");

            var result = _service.AllocatePayment("V001", 300000, "1100",
                "2026-04-04", "202604", 1, null);

            result.AmountAllocated.Should().Be(300000);
            result.InvoicesPaid.Should().Be(0);
            result.InvoicesPartiallyPaid.Should().Be(1);

            var entry = _payablesRepo.GetByJournalNo("MSK-0011");
            entry.PaymentAmount.Should().Be(300000);
            entry.IsPaid.Should().Be("N");
        }

        [Test]
        public void AllocatePayment_OldestFirst()
        {
            InsertPayable("V001", "MSK-0020", 300000, "2026-03-01", "2026-04-01");
            InsertPayable("V001", "MSK-0021", 400000, "2026-03-15", "2026-04-15");

            var result = _service.AllocatePayment("V001", 500000, "1100",
                "2026-04-04", "202604", 1, null);

            result.AmountAllocated.Should().Be(500000);
            result.InvoicesPaid.Should().Be(1); // MSK-0020 fully paid
            result.InvoicesPartiallyPaid.Should().Be(1); // MSK-0021 partially paid

            var first = _payablesRepo.GetByJournalNo("MSK-0020");
            first.IsPaid.Should().Be("Y");

            var second = _payablesRepo.GetByJournalNo("MSK-0021");
            second.PaymentAmount.Should().Be(200000); // 500000 - 300000
        }

        [Test]
        public void AllocatePayment_SpecificInvoices()
        {
            InsertPayable("V001", "MSK-0030", 300000, "2026-03-01", "2026-04-01");
            InsertPayable("V001", "MSK-0031", 200000, "2026-03-15", "2026-04-15");

            var result = _service.AllocatePayment("V001", 200000, "1100",
                "2026-04-04", "202604", 1,
                new List<string> { "MSK-0031" });

            result.InvoicesPaid.Should().Be(1);
            result.PaidJournalNos.Should().Contain("MSK-0031");

            // MSK-0030 should remain untouched
            var first = _payablesRepo.GetByJournalNo("MSK-0030");
            first.PaymentAmount.Should().Be(0);
        }

        [Test]
        public void AllocatePayment_PostsGlEntries()
        {
            InsertPayable("V001", "MSK-0040", 500000, "2026-04-01", "2026-05-01");

            _service.AllocatePayment("V001", 500000, "1100",
                "2026-04-04", "202604", 1, null);

            // Verify GL entries exist (AP debit, Cash credit)
            long debits = _glRepo.GetDebitTotal("202604");
            long credits = _glRepo.GetCreditTotal("202604");
            debits.Should().Be(credits);
            debits.Should().Be(500000);
        }

        [Test]
        public void AllocatePayment_ZeroAmount_Throws()
        {
            Action act = () => _service.AllocatePayment("V001", 0, "1100",
                "2026-04-04", "202604", 1, null);
            act.Should().Throw<ArgumentException>();
        }

        [Test]
        public void AllocatePayment_NegativeAmount_Throws()
        {
            Action act = () => _service.AllocatePayment("V001", -100, "1100",
                "2026-04-04", "202604", 1, null);
            act.Should().Throw<ArgumentException>();
        }

        // --- Aging Report ---

        [Test]
        public void GetAgingReport_ClassifiesIntoBuckets()
        {
            // Current (not yet due)
            InsertPayable("V001", "MSK-0050", 100000, "2026-04-01", "2026-04-10");
            // 30 days overdue
            InsertPayable("V001", "MSK-0051", 200000, "2026-03-01", "2026-03-15");
            // 60 days overdue
            InsertPayable("V001", "MSK-0052", 300000, "2026-02-01", "2026-02-04");
            // 90 days overdue
            InsertPayable("V001", "MSK-0053", 400000, "2026-01-01", "2026-01-04");

            var aging = _service.GetAgingReport("2026-04-04");

            aging.Should().HaveCount(1);
            var bucket = aging[0];
            bucket.VendorCode.Should().Be("V001");
            bucket.Current.Should().Be(100000); // not yet due
            bucket.Days30.Should().Be(200000); // 20 days past due
            bucket.Days60.Should().Be(300000); // 59 days past due
            bucket.Days90.Should().Be(400000); // 90 days past due
            bucket.Total.Should().Be(1000000);
        }

        [Test]
        public void GetAgingReport_MultipleVendors()
        {
            InsertPayable("V001", "MSK-0060", 100000, "2026-04-01", "2026-05-01");
            InsertPayable("V002", "MSK-0061", 200000, "2026-04-02", "2026-05-02");

            var aging = _service.GetAgingReport("2026-04-04");
            aging.Should().HaveCount(2);
        }

        [Test]
        public void GetAgingReport_ExcludesPaidInvoices()
        {
            InsertPayable("V001", "MSK-0070", 100000, "2026-04-01", "2026-05-01");
            _payablesRepo.RecordPayment("MSK-0070", 100000);

            var aging = _service.GetAgingReport("2026-04-04");
            aging.Should().HaveCount(0);
        }

        // --- Helpers ---

        private void InsertPayable(string vendorCode, string journalNo, long amount,
            string docDate, string dueDate)
        {
            _payablesRepo.Insert(new PayablesEntry
            {
                SubCode = vendorCode,
                JournalNo = journalNo,
                DocDate = docDate,
                DueDate = dueDate,
                Direction = "D",
                GrossAmount = amount,
                Amount = amount,
                PaymentAmount = 0,
                Control = 1,
                PeriodCode = "202604",
                ChangedBy = 1
            });
        }

        private void SeedAccount(string code, string name, int group, string normalBal)
        {
            _accountRepo.Insert(new Account
            {
                AccountCode = code,
                AccountName = name,
                IsDetail = 1,
                AccountGroup = group,
                NormalBalance = normalBal
            });
        }
    }
}
