using System;
using System.Collections.Generic;
using System.Data.SQLite;
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
    public class PostingServiceTests
    {
        private SQLiteConnection _db;
        private PostingService _service;
        private AccountRepository _accountRepo;
        private AccountBalanceRepository _balanceRepo;
        private GlDetailRepository _glRepo;
        private FiscalPeriodRepository _periodRepo;
        private SaleRepository _saleRepo;
        private CashTransactionRepository _cashTxnRepo;

        [SetUp]
        public void SetUp()
        {
            _db = TestDb.Create();
            _service = new PostingService(_db);
            _accountRepo = new AccountRepository(_db);
            _balanceRepo = new AccountBalanceRepository(_db);
            _glRepo = new GlDetailRepository(_db);
            _periodRepo = new FiscalPeriodRepository(_db);
            _saleRepo = new SaleRepository(_db);
            _cashTxnRepo = new CashTransactionRepository(_db);

            var configRepo = new ConfigRepository(_db);
            configRepo.Set("register_id", "01");
            configRepo.Set("ACCOUNT_CASH", "1100");
            configRepo.Set("ACCOUNT_BANK", "1200");
            configRepo.Set("ACCOUNT_SALES_REVENUE", "4100");
            configRepo.Set("ACCOUNT_COGS", "5100");
            configRepo.Set("ACCOUNT_INVENTORY", "1300");
            configRepo.Set("ACCOUNT_PAYABLES", "2100");

            // Seed accounts
            SeedAccount("1100", "Cash", 1, "D");
            SeedAccount("1200", "Bank", 1, "D");
            SeedAccount("1300", "Inventory", 1, "D");
            SeedAccount("2100", "Accounts Payable", 2, "K");
            SeedAccount("4100", "Sales Revenue", 4, "K");
            SeedAccount("5100", "COGS", 5, "D");
            SeedAccount("6100", "Expense", 5, "D");

            // Seed products
            var productRepo = new ProductRepository(_db);
            productRepo.Insert(new Product
            {
                ProductCode = "P001", Name = "TEST PRODUCT", Price = 500000,
                BuyingPrice = 300000, Status = "A", OpenPrice = "N",
                VatFlag = "N", LuxuryTaxFlag = "N", IsConsignment = "N"
            });

            // Seed vendor
            var vendorRepo = new SubsidiaryRepository(_db);
            vendorRepo.Insert(new Subsidiary
            {
                SubCode = "V001", Name = "TEST VENDOR", GroupCode = "1", Status = "A"
            });

            // Ensure fiscal period
            _periodRepo.EnsurePeriod("202604", 2026, 4);
        }

        [TearDown]
        public void TearDown()
        {
            _db.Close();
            _db.Dispose();
        }

        // --- PostSales ---

        [Test]
        public void PostSales_PostsUnpostedSales()
        {
            InsertSale("JFA-01-2604-0001", 500000);
            InsertSale("JFA-01-2604-0002", 300000);

            var result = _service.PostSales("202604");

            result.PostedCount.Should().Be(2);
            result.ErrorCount.Should().Be(0);

            // Verify GL entries created
            var gl1 = _glRepo.GetByJournalNo("JFA-01-2604-0001");
            gl1.Count.Should().BeGreaterThan(0);

            // Verify sales marked as posted
            var sale = SqlHelper.QuerySingle(_db,
                "SELECT is_posted FROM sales WHERE journal_no = 'JFA-01-2604-0001'",
                r => SqlHelper.GetString(r, "is_posted"));
            sale.Should().Be("Y");
        }

        [Test]
        public void PostSales_SkipsAlreadyPosted()
        {
            InsertSale("JFA-01-2604-0003", 100000);
            SqlHelper.ExecuteNonQuery(_db,
                "UPDATE sales SET is_posted = 'Y' WHERE journal_no = 'JFA-01-2604-0003'");

            var result = _service.PostSales("202604");
            result.PostedCount.Should().Be(0);
        }

        [Test]
        public void PostSales_GlEntriesAreBalanced()
        {
            InsertSale("JFA-01-2604-0004", 500000);

            _service.PostSales("202604");

            long debits = _glRepo.GetDebitTotal("202604");
            long credits = _glRepo.GetCreditTotal("202604");
            debits.Should().Be(credits);
        }

        // --- PostPurchases ---

        [Test]
        public void PostPurchases_PostsUnpostedPurchases()
        {
            InsertPurchase("MSK-01-2604-0001", 1000000);

            var result = _service.PostPurchases("202604");

            result.PostedCount.Should().Be(1);
            result.ErrorCount.Should().Be(0);
        }

        // --- PostReturns ---

        [Test]
        public void PostReturns_PostsReturnDocuments()
        {
            InsertPurchaseReturn("RMS-01-2604-0001", 200000);

            var result = _service.PostReturns("202604");

            result.PostedCount.Should().Be(1);
        }

        // --- PostCashTransactions ---

        [Test]
        public void PostCashTransactions_PostsCashIn()
        {
            InsertCashTransaction("KMS-01-2604-0001", "CASH_IN", 100000, "6100");

            var result = _service.PostCashTransactions("202604");

            result.PostedCount.Should().Be(1);

            var txn = _cashTxnRepo.GetByJournalNo("KMS-01-2604-0001");
            txn.IsPosted.Should().Be("Y");
        }

        // --- Closed Period ---

        [Test]
        public void PostSales_ToClosedPeriod_Throws()
        {
            _periodRepo.ClosePeriod("202604");

            Action act = () => _service.PostSales("202604");
            act.Should().Throw<InvalidOperationException>()
                .WithMessage("*closed period*");
        }

        // --- ClosePeriod ---

        [Test]
        public void ClosePeriod_WithUnpostedSales_Throws()
        {
            InsertSale("JFA-01-2604-0005", 100000);

            Action act = () => _service.ClosePeriod("202604");
            act.Should().Throw<InvalidOperationException>()
                .WithMessage("*unposted sales*");
        }

        [Test]
        public void ClosePeriod_AllPosted_Succeeds()
        {
            // No unposted transactions — just close
            _service.ClosePeriod("202604");

            var period = _periodRepo.GetByCode("202604");
            period.Status.Should().Be("C");
        }

        [Test]
        public void ClosePeriod_AlreadyClosed_Throws()
        {
            _service.ClosePeriod("202604");

            Action act = () => _service.ClosePeriod("202604");
            act.Should().Throw<InvalidOperationException>()
                .WithMessage("*already closed*");
        }

        [Test]
        public void ClosePeriod_CarriesForwardBalances()
        {
            // Set up balance for 202604
            _balanceRepo.SetOpeningBalance("1100", "202604", 1000000);
            _balanceRepo.AddDebit("1100", "202604", 500000);
            _balanceRepo.AddCredit("1100", "202604", 200000);

            _service.ClosePeriod("202604");

            // Check 202605 opening balance = 1000000 + 500000 - 200000 = 1300000
            var nextBal = _balanceRepo.GetBalance("1100", "202605");
            nextBal.Should().NotBeNull();
            nextBal.OpeningBalance.Should().Be(1300000);
        }

        // --- CheckBalance ---

        [Test]
        public void CheckBalance_Balanced_ReturnsTrue()
        {
            // Post a balanced entry
            InsertSale("JFA-01-2604-0006", 500000);
            _service.PostSales("202604");

            var result = _service.CheckBalance("202604");
            result.IsBalanced.Should().BeTrue();
            result.Difference.Should().Be(0);
        }

        [Test]
        public void CheckBalance_NoPosts_IsBalanced()
        {
            var result = _service.CheckBalance("202604");
            result.IsBalanced.Should().BeTrue();
            result.TotalDebits.Should().Be(0);
            result.TotalCredits.Should().Be(0);
        }

        // --- Helpers ---

        private void InsertSale(string journalNo, long totalValue)
        {
            _saleRepo.Insert(new Sale
            {
                DocType = "SALE",
                JournalNo = journalNo,
                DocDate = "2026-04-04",
                TotalValue = totalValue,
                CashAmount = totalValue,
                Control = 1,
                PeriodCode = "202604",
                RegisterId = "01",
                ChangedBy = 1
            }, new List<SaleItem>
            {
                new SaleItem
                {
                    ProductCode = "P001",
                    Quantity = 100,
                    Value = totalValue,
                    UnitPrice = (int)totalValue,
                    Cogs = totalValue * 60 / 100
                }
            });
        }

        private void InsertPurchase(string journalNo, long totalValue)
        {
            var purchaseRepo = new PurchaseRepository(_db);
            purchaseRepo.Insert(new Purchase
            {
                DocType = "PURCHASE",
                JournalNo = journalNo,
                DocDate = "2026-04-04",
                SubCode = "V001",
                TotalValue = totalValue,
                Control = 1,
                PeriodCode = "202604",
                RegisterId = "01",
                ChangedBy = 1
            }, new List<PurchaseItem>
            {
                new PurchaseItem
                {
                    ProductCode = "P001",
                    Quantity = 100,
                    Value = totalValue,
                    UnitPrice = (int)totalValue
                }
            });
        }

        private void InsertPurchaseReturn(string journalNo, long totalValue)
        {
            var purchaseRepo = new PurchaseRepository(_db);
            purchaseRepo.Insert(new Purchase
            {
                DocType = "PURCHASE_RETURN",
                JournalNo = journalNo,
                DocDate = "2026-04-04",
                SubCode = "V001",
                TotalValue = totalValue,
                Control = 1,
                PeriodCode = "202604",
                RegisterId = "01",
                ChangedBy = 1
            }, new List<PurchaseItem>
            {
                new PurchaseItem
                {
                    ProductCode = "P001",
                    Quantity = 100,
                    Value = totalValue,
                    UnitPrice = (int)totalValue
                }
            });
        }

        private void InsertCashTransaction(string journalNo, string docType, long amount, string contraAccount)
        {
            _cashTxnRepo.Insert(new CashTransaction
            {
                DocType = docType,
                JournalNo = journalNo,
                DocDate = "2026-04-04",
                TotalValue = amount,
                Control = 1,
                PeriodCode = "202604",
                RegisterId = "01",
                ChangedBy = 1
            }, new List<CashTransactionLine>
            {
                new CashTransactionLine
                {
                    AccountCode = contraAccount,
                    Direction = docType.Contains("IN") ? "K" : "D",
                    Value = amount,
                    Remark = "Test"
                }
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
