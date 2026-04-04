using System;
using System.Collections.Generic;
using System.Data.SQLite;
using Kasir.Data.Repositories;
using Kasir.Models;

namespace Kasir.Services
{
    public class AccountingService
    {
        private readonly SQLiteConnection _db;
        private readonly GlDetailRepository _glRepo;
        private readonly AccountBalanceRepository _balanceRepo;
        private readonly AccountRepository _accountRepo;
        private readonly FiscalPeriodRepository _periodRepo;
        private readonly CounterRepository _counterRepo;

        public AccountingService(SQLiteConnection db)
        {
            _db = db;
            _glRepo = new GlDetailRepository(db);
            _balanceRepo = new AccountBalanceRepository(db);
            _accountRepo = new AccountRepository(db);
            _periodRepo = new FiscalPeriodRepository(db);
            _counterRepo = new CounterRepository(db);
        }

        public string CreateJournalEntry(JournalEntry entry)
        {
            ValidateJournalEntry(entry);

            if (string.IsNullOrEmpty(entry.JournalNo))
            {
                entry.JournalNo = _counterRepo.GetNext("UMH", "01");
            }

            using (var txn = _db.BeginTransaction())
            {
                try
                {
                    // Insert memorial journal header
                    SqlHelper.ExecuteNonQuery(_db,
                        @"INSERT INTO memorial_journals (doc_type, journal_no, doc_date, remark,
                          control, period_code, register_id, changed_by, changed_at)
                          VALUES ('MEMORIAL', @jnl, @date, @remark, 1, @period, '01', @changedBy,
                          datetime('now','localtime'))",
                        SqlHelper.Param("@jnl", entry.JournalNo),
                        SqlHelper.Param("@date", entry.DocDate),
                        SqlHelper.Param("@remark", entry.Remark ?? ""),
                        SqlHelper.Param("@period", entry.PeriodCode),
                        SqlHelper.Param("@changedBy", entry.ChangedBy));

                    // Insert memorial journal lines
                    foreach (var line in entry.Lines)
                    {
                        SqlHelper.ExecuteNonQuery(_db,
                            @"INSERT INTO memorial_journal_lines (journal_no, account_code, sub_code,
                              product_code, remark, direction, value)
                              VALUES (@jnl, @acc, @sub, @product, @remark, @dir, @val)",
                            SqlHelper.Param("@jnl", entry.JournalNo),
                            SqlHelper.Param("@acc", line.AccountCode),
                            SqlHelper.Param("@sub", line.SubCode ?? ""),
                            SqlHelper.Param("@product", line.ProductCode ?? ""),
                            SqlHelper.Param("@remark", line.Remark ?? ""),
                            SqlHelper.Param("@dir", line.Debit > 0 ? "D" : "K"),
                            SqlHelper.Param("@val", line.Debit > 0 ? line.Debit : line.Credit));
                    }

                    // Post GL details and update balances
                    PostGlLines(entry);

                    txn.Commit();
                    return entry.JournalNo;
                }
                catch
                {
                    txn.Rollback();
                    throw;
                }
            }
        }

        public void PostSaleJournal(Sale sale, List<SaleItem> items, string cashAccountCode)
        {
            if (string.IsNullOrEmpty(cashAccountCode))
            {
                throw new ArgumentException("Cash account code is required");
            }

            var entry = new JournalEntry
            {
                JournalNo = sale.JournalNo,
                DocDate = sale.DocDate,
                Remark = "Sale " + sale.JournalNo,
                PeriodCode = sale.PeriodCode,
                ChangedBy = sale.ChangedBy
            };

            // Debit: Cash/Bank for total sale value
            entry.Lines.Add(new JournalLine
            {
                AccountCode = cashAccountCode,
                Debit = sale.TotalValue,
                Remark = "Cash sale"
            });

            // Credit: Sales revenue (aggregate by account)
            // For simplicity, use total value as revenue credit
            // In full implementation, each item maps to its sold_account via account_config
            entry.Lines.Add(new JournalLine
            {
                AccountCode = GetSalesRevenueAccount(),
                Credit = sale.TotalValue,
                Remark = "Sales revenue"
            });

            // COGS entries: Debit COGS, Credit Inventory
            long totalCogs = 0;
            foreach (var item in items)
            {
                if (item.Cogs > 0)
                {
                    totalCogs += item.Cogs;
                }
            }

            if (totalCogs > 0)
            {
                entry.Lines.Add(new JournalLine
                {
                    AccountCode = GetCogsAccount(),
                    Debit = totalCogs,
                    Remark = "Cost of goods sold"
                });

                entry.Lines.Add(new JournalLine
                {
                    AccountCode = GetInventoryAccount(),
                    Credit = totalCogs,
                    Remark = "Inventory reduction"
                });
            }

            ValidateJournalEntry(entry);
            PostGlLines(entry);
        }

        public void PostPurchaseJournal(Purchase purchase)
        {
            var entry = new JournalEntry
            {
                JournalNo = purchase.JournalNo,
                DocDate = purchase.DocDate,
                Remark = "Purchase " + purchase.JournalNo,
                PeriodCode = purchase.PeriodCode,
                ChangedBy = purchase.ChangedBy
            };

            // Debit: Inventory account
            entry.Lines.Add(new JournalLine
            {
                AccountCode = GetInventoryAccount(),
                SubCode = purchase.SubCode,
                Debit = purchase.TotalValue,
                Remark = "Purchase inventory"
            });

            // Credit: AP account
            entry.Lines.Add(new JournalLine
            {
                AccountCode = GetPayablesAccount(),
                SubCode = purchase.SubCode,
                Credit = purchase.TotalValue,
                Remark = "Accounts payable"
            });

            ValidateJournalEntry(entry);
            PostGlLines(entry);
        }

        public void PostReturnJournal(Purchase returnDoc)
        {
            var entry = new JournalEntry
            {
                JournalNo = returnDoc.JournalNo,
                DocDate = returnDoc.DocDate,
                Remark = "Return " + returnDoc.JournalNo,
                PeriodCode = returnDoc.PeriodCode,
                ChangedBy = returnDoc.ChangedBy
            };

            // Reverse of purchase: Debit AP, Credit Inventory
            entry.Lines.Add(new JournalLine
            {
                AccountCode = GetPayablesAccount(),
                SubCode = returnDoc.SubCode,
                Debit = returnDoc.TotalValue,
                Remark = "AP reduction (return)"
            });

            entry.Lines.Add(new JournalLine
            {
                AccountCode = GetInventoryAccount(),
                SubCode = returnDoc.SubCode,
                Credit = returnDoc.TotalValue,
                Remark = "Inventory return"
            });

            ValidateJournalEntry(entry);
            PostGlLines(entry);
        }

        public void PostPaymentJournal(string journalNo, string docDate, string periodCode,
            string vendorCode, long amount, string cashAccountCode, int changedBy)
        {
            var entry = new JournalEntry
            {
                JournalNo = journalNo,
                DocDate = docDate,
                Remark = "Payment to " + vendorCode,
                PeriodCode = periodCode,
                ChangedBy = changedBy
            };

            // Debit: AP account
            entry.Lines.Add(new JournalLine
            {
                AccountCode = GetPayablesAccount(),
                SubCode = vendorCode,
                Debit = amount,
                Remark = "AP payment"
            });

            // Credit: Cash/Bank account
            entry.Lines.Add(new JournalLine
            {
                AccountCode = cashAccountCode,
                Credit = amount,
                Remark = "Cash payment"
            });

            ValidateJournalEntry(entry);
            PostGlLines(entry);
        }

        public bool ValidateBalance(JournalEntry entry)
        {
            long totalDebit = 0;
            long totalCredit = 0;

            foreach (var line in entry.Lines)
            {
                totalDebit += line.Debit;
                totalCredit += line.Credit;
            }

            return totalDebit == totalCredit;
        }

        private void ValidateJournalEntry(JournalEntry entry)
        {
            if (entry.Lines == null || entry.Lines.Count < 2)
            {
                throw new InvalidOperationException("Journal entry must have at least 2 lines");
            }

            long totalDebit = 0;
            long totalCredit = 0;

            foreach (var line in entry.Lines)
            {
                if (line.Debit < 0 || line.Credit < 0)
                {
                    throw new InvalidOperationException("Debit and credit amounts must be non-negative");
                }

                if (line.Debit == 0 && line.Credit == 0)
                {
                    throw new InvalidOperationException("Journal line cannot have zero debit and zero credit");
                }

                if (line.Debit > 0 && line.Credit > 0)
                {
                    throw new InvalidOperationException("Journal line cannot have both debit and credit");
                }

                if (string.IsNullOrEmpty(line.AccountCode))
                {
                    throw new InvalidOperationException("Account code is required for each journal line");
                }

                totalDebit += line.Debit;
                totalCredit += line.Credit;
            }

            if (totalDebit != totalCredit)
            {
                throw new InvalidOperationException(
                    string.Format("Journal entry is not balanced: debits={0}, credits={1}",
                        totalDebit, totalCredit));
            }
        }

        private void PostGlLines(JournalEntry entry)
        {
            foreach (var line in entry.Lines)
            {
                _glRepo.Insert(new GlDetail
                {
                    AccountCode = line.AccountCode,
                    SubCode = line.SubCode ?? "",
                    ProductCode = line.ProductCode ?? "",
                    JournalNo = entry.JournalNo,
                    Remark = line.Remark ?? "",
                    DocDate = entry.DocDate,
                    Debit = line.Debit,
                    Credit = line.Credit,
                    QtyIn = line.QtyIn,
                    QtyOut = line.QtyOut,
                    PeriodCode = entry.PeriodCode
                });

                // Update account balances
                if (line.Debit > 0)
                {
                    _balanceRepo.AddDebit(line.AccountCode, entry.PeriodCode, line.Debit);
                }
                if (line.Credit > 0)
                {
                    _balanceRepo.AddCredit(line.AccountCode, entry.PeriodCode, line.Credit);
                }
            }
        }

        private string GetSalesRevenueAccount()
        {
            return GetConfigAccount("SALES_REVENUE", "4100");
        }

        private string GetCogsAccount()
        {
            return GetConfigAccount("COGS", "5100");
        }

        private string GetInventoryAccount()
        {
            return GetConfigAccount("INVENTORY", "1300");
        }

        private string GetPayablesAccount()
        {
            return GetConfigAccount("PAYABLES", "2100");
        }

        private string GetConfigAccount(string key, string defaultCode)
        {
            var config = SqlHelper.QuerySingle(_db,
                "SELECT value FROM config WHERE key = @key",
                r => SqlHelper.GetString(r, "value"),
                SqlHelper.Param("@key", "ACCOUNT_" + key));

            return string.IsNullOrEmpty(config) ? defaultCode : config;
        }
    }
}
