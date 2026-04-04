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
    public class AccountingServiceTests
    {
        private SQLiteConnection _db;
        private AccountingService _service;
        private GlDetailRepository _glRepo;
        private AccountBalanceRepository _balanceRepo;
        private AccountRepository _accountRepo;

        [SetUp]
        public void SetUp()
        {
            _db = TestDb.Create();
            _service = new AccountingService(_db);
            _glRepo = new GlDetailRepository(_db);
            _balanceRepo = new AccountBalanceRepository(_db);
            _accountRepo = new AccountRepository(_db);

            var configRepo = new ConfigRepository(_db);
            configRepo.Set("register_id", "01");

            // Seed accounts
            SeedAccount("1100", "Cash", 1, "D");
            SeedAccount("1300", "Inventory", 1, "D");
            SeedAccount("2100", "Accounts Payable", 2, "K");
            SeedAccount("4100", "Sales Revenue", 4, "K");
            SeedAccount("5100", "COGS", 5, "D");
            SeedAccount("6100", "Expense", 5, "D");

            // Seed config for default accounts
            configRepo.Set("ACCOUNT_SALES_REVENUE", "4100");
            configRepo.Set("ACCOUNT_COGS", "5100");
            configRepo.Set("ACCOUNT_INVENTORY", "1300");
            configRepo.Set("ACCOUNT_PAYABLES", "2100");
        }

        [TearDown]
        public void TearDown()
        {
            _db.Close();
            _db.Dispose();
        }

        // --- Validation Tests ---

        [Test]
        public void CreateJournalEntry_Balanced_Succeeds()
        {
            var entry = MakeBalancedEntry(100000);

            var journalNo = _service.CreateJournalEntry(entry);

            journalNo.Should().NotBeNullOrEmpty();
            var glLines = _glRepo.GetByJournalNo(journalNo);
            glLines.Should().HaveCount(2);
            glLines[0].Debit.Should().Be(100000);
            glLines[1].Credit.Should().Be(100000);
        }

        [Test]
        public void CreateJournalEntry_Unbalanced_ThrowsInvalidOperationException()
        {
            var entry = new JournalEntry
            {
                DocDate = "2026-04-04",
                Remark = "Unbalanced",
                PeriodCode = "202604",
                ChangedBy = 1,
                Lines = new List<JournalLine>
                {
                    new JournalLine { AccountCode = "1100", Debit = 100000 },
                    new JournalLine { AccountCode = "6100", Credit = 90000 }
                }
            };

            Action act = () => _service.CreateJournalEntry(entry);
            act.Should().Throw<InvalidOperationException>()
                .WithMessage("*not balanced*");
        }

        [Test]
        public void CreateJournalEntry_ZeroAmount_ThrowsInvalidOperationException()
        {
            var entry = new JournalEntry
            {
                DocDate = "2026-04-04",
                PeriodCode = "202604",
                ChangedBy = 1,
                Lines = new List<JournalLine>
                {
                    new JournalLine { AccountCode = "1100", Debit = 100000 },
                    new JournalLine { AccountCode = "6100", Debit = 0, Credit = 0 }
                }
            };

            Action act = () => _service.CreateJournalEntry(entry);
            act.Should().Throw<InvalidOperationException>()
                .WithMessage("*zero debit and zero credit*");
        }

        [Test]
        public void CreateJournalEntry_NegativeAmount_ThrowsInvalidOperationException()
        {
            var entry = new JournalEntry
            {
                DocDate = "2026-04-04",
                PeriodCode = "202604",
                ChangedBy = 1,
                Lines = new List<JournalLine>
                {
                    new JournalLine { AccountCode = "1100", Debit = -100000 },
                    new JournalLine { AccountCode = "6100", Credit = 100000 }
                }
            };

            Action act = () => _service.CreateJournalEntry(entry);
            act.Should().Throw<InvalidOperationException>()
                .WithMessage("*non-negative*");
        }

        [Test]
        public void CreateJournalEntry_BothDebitAndCredit_ThrowsInvalidOperationException()
        {
            var entry = new JournalEntry
            {
                DocDate = "2026-04-04",
                PeriodCode = "202604",
                ChangedBy = 1,
                Lines = new List<JournalLine>
                {
                    new JournalLine { AccountCode = "1100", Debit = 100000, Credit = 100000 },
                    new JournalLine { AccountCode = "6100", Credit = 100000 }
                }
            };

            Action act = () => _service.CreateJournalEntry(entry);
            act.Should().Throw<InvalidOperationException>()
                .WithMessage("*both debit and credit*");
        }

        [Test]
        public void CreateJournalEntry_MissingAccountCode_ThrowsInvalidOperationException()
        {
            var entry = new JournalEntry
            {
                DocDate = "2026-04-04",
                PeriodCode = "202604",
                ChangedBy = 1,
                Lines = new List<JournalLine>
                {
                    new JournalLine { AccountCode = "1100", Debit = 100000 },
                    new JournalLine { AccountCode = "", Credit = 100000 }
                }
            };

            Action act = () => _service.CreateJournalEntry(entry);
            act.Should().Throw<InvalidOperationException>()
                .WithMessage("*Account code is required*");
        }

        [Test]
        public void CreateJournalEntry_SingleLine_ThrowsInvalidOperationException()
        {
            var entry = new JournalEntry
            {
                DocDate = "2026-04-04",
                PeriodCode = "202604",
                ChangedBy = 1,
                Lines = new List<JournalLine>
                {
                    new JournalLine { AccountCode = "1100", Debit = 100000 }
                }
            };

            Action act = () => _service.CreateJournalEntry(entry);
            act.Should().Throw<InvalidOperationException>()
                .WithMessage("*at least 2 lines*");
        }

        // --- GL Posting Tests ---

        [Test]
        public void CreateJournalEntry_UpdatesAccountBalances()
        {
            var entry = MakeBalancedEntry(250000);

            _service.CreateJournalEntry(entry);

            var cashBal = _balanceRepo.GetBalance("1100", "202604");
            cashBal.Should().NotBeNull();
            cashBal.DebitTotal.Should().Be(250000);

            var expBal = _balanceRepo.GetBalance("6100", "202604");
            expBal.Should().NotBeNull();
            expBal.CreditTotal.Should().Be(250000);
        }

        [Test]
        public void CreateJournalEntry_MultipleEntries_AccumulateBalances()
        {
            _service.CreateJournalEntry(MakeBalancedEntry(100000));
            _service.CreateJournalEntry(MakeBalancedEntry(200000));

            var cashBal = _balanceRepo.GetBalance("1100", "202604");
            cashBal.DebitTotal.Should().Be(300000);
        }

        // --- Sale Posting ---

        [Test]
        public void PostSaleJournal_CreatesBalancedGlEntries()
        {
            var sale = new Sale
            {
                JournalNo = "JFA-01-2604-0001",
                DocDate = "2026-04-04",
                TotalValue = 500000,
                PeriodCode = "202604",
                ChangedBy = 1
            };

            var items = new List<SaleItem>
            {
                new SaleItem { ProductCode = "P001", Value = 500000, Cogs = 300000 }
            };

            _service.PostSaleJournal(sale, items, "1100");

            // Verify GL entries are balanced
            var glLines = _glRepo.GetByJournalNo(sale.JournalNo);
            glLines.Should().HaveCount(4); // cash debit, revenue credit, cogs debit, inv credit

            long totalDebit = 0, totalCredit = 0;
            foreach (var line in glLines)
            {
                totalDebit += line.Debit;
                totalCredit += line.Credit;
            }
            totalDebit.Should().Be(totalCredit);
        }

        [Test]
        public void PostSaleJournal_NoCogs_CreatesOnlyRevenueEntries()
        {
            var sale = new Sale
            {
                JournalNo = "JFA-01-2604-0002",
                DocDate = "2026-04-04",
                TotalValue = 150000,
                PeriodCode = "202604",
                ChangedBy = 1
            };

            var items = new List<SaleItem>
            {
                new SaleItem { ProductCode = "P001", Value = 150000, Cogs = 0 }
            };

            _service.PostSaleJournal(sale, items, "1100");

            var glLines = _glRepo.GetByJournalNo(sale.JournalNo);
            glLines.Should().HaveCount(2); // cash debit, revenue credit only
        }

        [Test]
        public void PostSaleJournal_MissingCashAccount_Throws()
        {
            var sale = new Sale
            {
                JournalNo = "JFA-01-2604-0003",
                DocDate = "2026-04-04",
                TotalValue = 100000,
                PeriodCode = "202604",
                ChangedBy = 1
            };

            Action act = () => _service.PostSaleJournal(sale, new List<SaleItem>(), "");
            act.Should().Throw<ArgumentException>();
        }

        // --- Purchase Posting ---

        [Test]
        public void PostPurchaseJournal_CreatesBalancedGlEntries()
        {
            var purchase = new Purchase
            {
                JournalNo = "MSK-01-2604-0001",
                DocDate = "2026-04-04",
                SubCode = "V001",
                TotalValue = 1000000,
                PeriodCode = "202604",
                ChangedBy = 1
            };

            _service.PostPurchaseJournal(purchase);

            var glLines = _glRepo.GetByJournalNo(purchase.JournalNo);
            glLines.Should().HaveCount(2);

            long totalDebit = 0, totalCredit = 0;
            foreach (var line in glLines)
            {
                totalDebit += line.Debit;
                totalCredit += line.Credit;
            }
            totalDebit.Should().Be(totalCredit);
            totalDebit.Should().Be(1000000);
        }

        [Test]
        public void PostPurchaseJournal_UpdatesBalances()
        {
            var purchase = new Purchase
            {
                JournalNo = "MSK-01-2604-0002",
                DocDate = "2026-04-04",
                SubCode = "V001",
                TotalValue = 800000,
                PeriodCode = "202604",
                ChangedBy = 1
            };

            _service.PostPurchaseJournal(purchase);

            var invBal = _balanceRepo.GetBalance("1300", "202604");
            invBal.DebitTotal.Should().Be(800000);

            var apBal = _balanceRepo.GetBalance("2100", "202604");
            apBal.CreditTotal.Should().Be(800000);
        }

        // --- Return Posting ---

        [Test]
        public void PostReturnJournal_ReversesOfPurchase()
        {
            var returnDoc = new Purchase
            {
                JournalNo = "RMS-01-2604-0001",
                DocDate = "2026-04-04",
                SubCode = "V001",
                TotalValue = 200000,
                PeriodCode = "202604",
                ChangedBy = 1
            };

            _service.PostReturnJournal(returnDoc);

            var glLines = _glRepo.GetByJournalNo(returnDoc.JournalNo);
            glLines.Should().HaveCount(2);

            // AP is debited (reduces payable), Inventory is credited (reduces stock)
            glLines[0].AccountCode.Should().Be("2100");
            glLines[0].Debit.Should().Be(200000);
            glLines[1].AccountCode.Should().Be("1300");
            glLines[1].Credit.Should().Be(200000);
        }

        // --- Payment Posting ---

        [Test]
        public void PostPaymentJournal_CreatesBalancedGlEntries()
        {
            _service.PostPaymentJournal("KKL-01-2604-0001", "2026-04-04", "202604",
                "V001", 500000, "1100", 1);

            var glLines = _glRepo.GetByJournalNo("KKL-01-2604-0001");
            glLines.Should().HaveCount(2);

            long totalDebit = 0, totalCredit = 0;
            foreach (var line in glLines)
            {
                totalDebit += line.Debit;
                totalCredit += line.Credit;
            }
            totalDebit.Should().Be(totalCredit);
            totalDebit.Should().Be(500000);
        }

        // --- ValidateBalance ---

        [Test]
        public void ValidateBalance_BalancedEntry_ReturnsTrue()
        {
            var entry = MakeBalancedEntry(100000);
            _service.ValidateBalance(entry).Should().BeTrue();
        }

        [Test]
        public void ValidateBalance_UnbalancedEntry_ReturnsFalse()
        {
            var entry = new JournalEntry
            {
                Lines = new List<JournalLine>
                {
                    new JournalLine { AccountCode = "1100", Debit = 100000 },
                    new JournalLine { AccountCode = "6100", Credit = 50000 }
                }
            };
            _service.ValidateBalance(entry).Should().BeFalse();
        }

        // --- Helpers ---

        private JournalEntry MakeBalancedEntry(long amount)
        {
            return new JournalEntry
            {
                DocDate = "2026-04-04",
                Remark = "Test entry",
                PeriodCode = "202604",
                ChangedBy = 1,
                Lines = new List<JournalLine>
                {
                    new JournalLine { AccountCode = "1100", Debit = amount, Remark = "Debit" },
                    new JournalLine { AccountCode = "6100", Credit = amount, Remark = "Credit" }
                }
            };
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
