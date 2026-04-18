using System;
using System.Collections.Generic;
using Microsoft.Data.Sqlite;
using Kasir.Data;
using Kasir.Data.Repositories;
using Kasir.Models;

namespace Kasir.Services
{
    public class PostingResult
    {
        public int PostedCount { get; set; }
        public int ErrorCount { get; set; }
        public List<string> Errors { get; set; }

        public PostingResult()
        {
            Errors = new List<string>();
        }
    }

    public class BalanceCheckResult
    {
        public bool IsBalanced { get; set; }
        public long TotalDebits { get; set; }
        public long TotalCredits { get; set; }
        public long Difference { get; set; }
        public List<string> DiscrepancyAccounts { get; set; }

        public BalanceCheckResult()
        {
            DiscrepancyAccounts = new List<string>();
        }
    }

    public class PostingService
    {
        private readonly SqliteConnection _db;
        private readonly AccountingService _accountingService;
        private readonly SaleRepository _saleRepo;
        private readonly PurchaseRepository _purchaseRepo;
        private readonly CashTransactionRepository _cashTxnRepo;
        private readonly FiscalPeriodRepository _periodRepo;
        private readonly AccountBalanceRepository _balanceRepo;
        private readonly GlDetailRepository _glRepo;

        public PostingService(SqliteConnection db)
        {
            _db = db;
            _accountingService = new AccountingService(db);
            _saleRepo = new SaleRepository(db);
            _purchaseRepo = new PurchaseRepository(db);
            _cashTxnRepo = new CashTransactionRepository(db);
            _periodRepo = new FiscalPeriodRepository(db);
            _balanceRepo = new AccountBalanceRepository(db);
            _glRepo = new GlDetailRepository(db);
        }

        public PostingResult PostSales(string periodCode)
        {
            EnsurePeriodOpen(periodCode);
            var result = new PostingResult();

            var sales = GetUnpostedSales(periodCode);
            foreach (var sale in sales)
            {
                try
                {
                    using (var txn = _db.BeginTransaction())
                    {
                        try
                        {
                            var items = _saleRepo.GetItemsByJournalNo(sale.JournalNo);
                            string cashAccount = GetCashAccountForSale(sale);
                            _accountingService.PostSaleJournal(sale, items, cashAccount);
                            MarkSalePosted(sale.JournalNo);
                            txn.Commit();
                            result.PostedCount++;
                        }
                        catch
                        {
                            txn.Rollback();
                            throw;
                        }
                    }
                }
                catch (Exception ex)
                {
                    result.ErrorCount++;
                    result.Errors.Add(sale.JournalNo + ": " + ex.Message);
                }
            }

            return result;
        }

        public PostingResult PostPurchases(string periodCode)
        {
            EnsurePeriodOpen(periodCode);
            var result = new PostingResult();

            var purchases = GetUnpostedPurchases(periodCode, "PURCHASE");
            foreach (var purchase in purchases)
            {
                try
                {
                    using (var txn = _db.BeginTransaction())
                    {
                        try
                        {
                            _accountingService.PostPurchaseJournal(purchase);
                            MarkPurchasePosted(purchase.JournalNo);
                            txn.Commit();
                            result.PostedCount++;
                        }
                        catch
                        {
                            txn.Rollback();
                            throw;
                        }
                    }
                }
                catch (Exception ex)
                {
                    result.ErrorCount++;
                    result.Errors.Add(purchase.JournalNo + ": " + ex.Message);
                }
            }

            return result;
        }

        public PostingResult PostReturns(string periodCode)
        {
            EnsurePeriodOpen(periodCode);
            var result = new PostingResult();

            var returns = GetUnpostedPurchases(periodCode, "PURCHASE_RETURN");
            foreach (var ret in returns)
            {
                try
                {
                    using (var txn = _db.BeginTransaction())
                    {
                        try
                        {
                            _accountingService.PostReturnJournal(ret);
                            MarkPurchasePosted(ret.JournalNo);
                            txn.Commit();
                            result.PostedCount++;
                        }
                        catch
                        {
                            txn.Rollback();
                            throw;
                        }
                    }
                }
                catch (Exception ex)
                {
                    result.ErrorCount++;
                    result.Errors.Add(ret.JournalNo + ": " + ex.Message);
                }
            }

            return result;
        }

        public PostingResult PostCashTransactions(string periodCode)
        {
            EnsurePeriodOpen(periodCode);
            var result = new PostingResult();

            var transactions = _cashTxnRepo.GetUnpostedByPeriod(periodCode);
            foreach (var cashTxn in transactions)
            {
                try
                {
                    using (var txn = _db.BeginTransaction())
                    {
                        try
                        {
                            var lines = _cashTxnRepo.GetLines(cashTxn.JournalNo);
                            PostCashTransactionToGl(cashTxn, lines);
                            _cashTxnRepo.MarkPosted(cashTxn.JournalNo);
                            txn.Commit();
                            result.PostedCount++;
                        }
                        catch
                        {
                            txn.Rollback();
                            throw;
                        }
                    }
                }
                catch (Exception ex)
                {
                    result.ErrorCount++;
                    result.Errors.Add(cashTxn.JournalNo + ": " + ex.Message);
                }
            }

            return result;
        }

        public void ClosePeriod(string periodCode)
        {
            var period = _periodRepo.GetByCode(periodCode);
            if (period == null)
            {
                throw new InvalidOperationException("Period not found: " + periodCode);
            }
            if (period.Status == "C")
            {
                throw new InvalidOperationException("Period already closed: " + periodCode);
            }

            // Check for unposted transactions
            var unpostedSales = GetUnpostedSales(periodCode);
            if (unpostedSales.Count > 0)
            {
                throw new InvalidOperationException(
                    string.Format("{0} unposted sales in period {1}", unpostedSales.Count, periodCode));
            }

            var unpostedPurchases = GetUnpostedPurchases(periodCode, "PURCHASE");
            if (unpostedPurchases.Count > 0)
            {
                throw new InvalidOperationException(
                    string.Format("{0} unposted purchases in period {1}", unpostedPurchases.Count, periodCode));
            }

            var unpostedCash = _cashTxnRepo.GetUnpostedByPeriod(periodCode);
            if (unpostedCash.Count > 0)
            {
                throw new InvalidOperationException(
                    string.Format("{0} unposted cash transactions in period {1}", unpostedCash.Count, periodCode));
            }

            // Carry forward balances to next period
            string nextPeriod = GetNextPeriod(periodCode);
            int nextYear = int.Parse(nextPeriod.Substring(0, 4));
            int nextMonth = int.Parse(nextPeriod.Substring(4, 2));
            _periodRepo.EnsurePeriod(nextPeriod, nextYear, nextMonth);
            _balanceRepo.CarryForward(periodCode, nextPeriod);

            // Close the period
            _periodRepo.ClosePeriod(periodCode);
        }

        public BalanceCheckResult CheckBalance(string periodCode)
        {
            var result = new BalanceCheckResult();

            result.TotalDebits = _glRepo.GetDebitTotal(periodCode);
            result.TotalCredits = _glRepo.GetCreditTotal(periodCode);
            result.Difference = result.TotalDebits - result.TotalCredits;
            result.IsBalanced = result.TotalDebits == result.TotalCredits;

            if (!result.IsBalanced)
            {
                // Find accounts where GL detail totals don't match account_balances
                var balances = _balanceRepo.GetAllForPeriod(periodCode);
                foreach (var bal in balances)
                {
                    long glDebit = _glRepo.GetDebitTotalForAccount(periodCode, bal.AccountCode);
                    long glCredit = _glRepo.GetCreditTotalForAccount(periodCode, bal.AccountCode);

                    if (glDebit != bal.DebitTotal || glCredit != bal.CreditTotal)
                    {
                        result.DiscrepancyAccounts.Add(
                            string.Format("{0}: bal(d={1},c={2}) vs gl(d={3},c={4})",
                                bal.AccountCode, bal.DebitTotal, bal.CreditTotal, glDebit, glCredit));
                    }
                }
            }

            return result;
        }

        private void EnsurePeriodOpen(string periodCode)
        {
            if (_periodRepo.IsClosed(periodCode))
            {
                throw new InvalidOperationException("Cannot post to closed period: " + periodCode);
            }

            int year = int.Parse(periodCode.Substring(0, 4));
            int month = int.Parse(periodCode.Substring(4, 2));
            _periodRepo.EnsurePeriod(periodCode, year, month);
        }

        private List<Sale> GetUnpostedSales(string periodCode)
        {
            return SqlHelper.Query(_db,
                @"SELECT * FROM sales
                  WHERE period_code = @period AND is_posted = 'N' AND control = 1
                  ORDER BY journal_no",
                r => new Sale
                {
                    Id = SqlHelper.GetInt(r, "id"),
                    DocType = SqlHelper.GetString(r, "doc_type"),
                    JournalNo = SqlHelper.GetString(r, "journal_no"),
                    DocDate = SqlHelper.GetString(r, "doc_date"),
                    TotalValue = SqlHelper.GetLong(r, "total_value"),
                    CashAmount = SqlHelper.GetLong(r, "cash_amount"),
                    NonCash = SqlHelper.GetLong(r, "non_cash"),
                    PeriodCode = SqlHelper.GetString(r, "period_code"),
                    ChangedBy = SqlHelper.GetInt(r, "changed_by")
                },
                SqlHelper.Param("@period", periodCode));
        }

        private List<Purchase> GetUnpostedPurchases(string periodCode, string docType)
        {
            return SqlHelper.Query(_db,
                @"SELECT * FROM purchases
                  WHERE period_code = @period AND is_posted = 'N' AND control = 1
                  AND doc_type = @type ORDER BY journal_no",
                r => new Purchase
                {
                    Id = SqlHelper.GetInt(r, "id"),
                    DocType = SqlHelper.GetString(r, "doc_type"),
                    JournalNo = SqlHelper.GetString(r, "journal_no"),
                    DocDate = SqlHelper.GetString(r, "doc_date"),
                    SubCode = SqlHelper.GetString(r, "sub_code"),
                    TotalValue = SqlHelper.GetLong(r, "total_value"),
                    PeriodCode = SqlHelper.GetString(r, "period_code"),
                    ChangedBy = SqlHelper.GetInt(r, "changed_by")
                },
                SqlHelper.Param("@period", periodCode),
                SqlHelper.Param("@type", docType));
        }

        private void MarkSalePosted(string journalNo)
        {
            SqlHelper.ExecuteNonQuery(_db,
                "UPDATE sales SET is_posted = 'Y' WHERE journal_no = @jnl",
                SqlHelper.Param("@jnl", journalNo));
        }

        private void MarkPurchasePosted(string journalNo)
        {
            SqlHelper.ExecuteNonQuery(_db,
                "UPDATE purchases SET is_posted = 'Y' WHERE journal_no = @jnl",
                SqlHelper.Param("@jnl", journalNo));
        }

        private string GetCashAccountForSale(Sale sale)
        {
            // Default cash account; could be overridden by sale's account_code
            var config = SqlHelper.QuerySingle(_db,
                "SELECT value FROM config WHERE key = 'ACCOUNT_CASH'",
                r => SqlHelper.GetString(r, "value"));

            return string.IsNullOrEmpty(config) ? "1100" : config;
        }

        private void PostCashTransactionToGl(CashTransaction txn, List<CashTransactionLine> lines)
        {
            var entry = new JournalEntry
            {
                JournalNo = txn.JournalNo,
                DocDate = txn.DocDate,
                Remark = txn.Remark ?? txn.Description ?? "",
                PeriodCode = txn.PeriodCode,
                ChangedBy = txn.ChangedBy
            };

            // Cash/Bank account is determined by doc_type
            string mainAccount = GetMainAccountForCashTxn(txn.DocType);

            // For CASH_IN/BANK_IN: debit main (cash/bank), credit contra accounts
            // For CASH_OUT/BANK_OUT: credit main (cash/bank), debit contra accounts
            bool isInflow = txn.DocType == "CASH_IN" || txn.DocType == "BANK_IN";

            if (isInflow)
            {
                entry.Lines.Add(new JournalLine
                {
                    AccountCode = mainAccount,
                    Debit = txn.TotalValue,
                    Remark = "Cash/Bank receipt"
                });
            }
            else
            {
                entry.Lines.Add(new JournalLine
                {
                    AccountCode = mainAccount,
                    Credit = txn.TotalValue,
                    Remark = "Cash/Bank disbursement"
                });
            }

            // Contra lines from cash_transaction_lines
            foreach (var line in lines)
            {
                if (isInflow)
                {
                    entry.Lines.Add(new JournalLine
                    {
                        AccountCode = line.AccountCode,
                        SubCode = line.SubCode,
                        Credit = line.Value,
                        Remark = line.Remark
                    });
                }
                else
                {
                    entry.Lines.Add(new JournalLine
                    {
                        AccountCode = line.AccountCode,
                        SubCode = line.SubCode,
                        Debit = line.Value,
                        Remark = line.Remark
                    });
                }
            }

            _accountingService.ValidateBalance(entry);
            // Post GL lines directly (the entry is already in cash_transactions, not memorial)
            PostGlLinesDirectly(entry);
        }

        private void PostGlLinesDirectly(JournalEntry entry)
        {
            var glRepo = new GlDetailRepository(_db);
            var balRepo = new AccountBalanceRepository(_db);

            foreach (var line in entry.Lines)
            {
                glRepo.Insert(new GlDetail
                {
                    AccountCode = line.AccountCode,
                    SubCode = line.SubCode ?? "",
                    JournalNo = entry.JournalNo,
                    Remark = line.Remark ?? "",
                    DocDate = entry.DocDate,
                    Debit = line.Debit,
                    Credit = line.Credit,
                    PeriodCode = entry.PeriodCode
                });

                if (line.Debit > 0)
                {
                    balRepo.AddDebit(line.AccountCode, entry.PeriodCode, line.Debit);
                }
                if (line.Credit > 0)
                {
                    balRepo.AddCredit(line.AccountCode, entry.PeriodCode, line.Credit);
                }
            }
        }

        private string GetMainAccountForCashTxn(string docType)
        {
            switch (docType)
            {
                case "CASH_IN":
                case "CASH_OUT":
                    var cashAcc = SqlHelper.QuerySingle(_db,
                        "SELECT value FROM config WHERE key = 'ACCOUNT_CASH'",
                        r => SqlHelper.GetString(r, "value"));
                    return string.IsNullOrEmpty(cashAcc) ? "1100" : cashAcc;

                case "BANK_IN":
                case "BANK_OUT":
                    var bankAcc = SqlHelper.QuerySingle(_db,
                        "SELECT value FROM config WHERE key = 'ACCOUNT_BANK'",
                        r => SqlHelper.GetString(r, "value"));
                    return string.IsNullOrEmpty(bankAcc) ? "1200" : bankAcc;

                default:
                    return "1100";
            }
        }

        private static string GetNextPeriod(string periodCode)
        {
            int year = int.Parse(periodCode.Substring(0, 4));
            int month = int.Parse(periodCode.Substring(4, 2));

            month++;
            if (month > 12)
            {
                month = 1;
                year++;
            }

            return year.ToString() + month.ToString("D2");
        }
    }
}
