using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Data.Sqlite;
using Kasir.Data;
using Kasir.Data.Repositories;
using Kasir.Models;

namespace Kasir.Services
{
    public class AccountingService
    {
        private readonly SqliteConnection _db;
        private readonly GlDetailRepository _glRepo;
        private readonly AccountBalanceRepository _balanceRepo;
        private readonly AccountRepository _accountRepo;
        private readonly FiscalPeriodRepository _periodRepo;
        private readonly CounterRepository _counterRepo;
        private readonly ConfigRepository _configRepo;
        private readonly PurchaseRepository _purchaseRepo;

        public AccountingService(SqliteConnection db)
        {
            _db = db;
            _glRepo = new GlDetailRepository(db);
            _balanceRepo = new AccountBalanceRepository(db);
            _accountRepo = new AccountRepository(db);
            _periodRepo = new FiscalPeriodRepository(db);
            _counterRepo = new CounterRepository(db);
            _configRepo = new ConfigRepository(db);
            _purchaseRepo = new PurchaseRepository(db);
        }

        private string RegisterId => _configRepo.Get("register_id") ?? "01";

        public string CreateJournalEntry(JournalEntry entry)
        {
            ValidateJournalEntry(entry);

            string registerId = RegisterId;
            if (string.IsNullOrEmpty(entry.JournalNo))
            {
                entry.JournalNo = _counterRepo.GetNext("UMH", registerId);
            }

            using (var txn = _db.BeginTransaction())
            {
                try
                {
                    // Insert memorial journal header
                    SqlHelper.ExecuteNonQuery(_db,
                        @"INSERT INTO memorial_journals (doc_type, journal_no, doc_date, remark,
                          control, period_code, register_id, changed_by, changed_at)
                          VALUES ('MEMORIAL', @jnl, @date, @remark, 1, @period, @reg, @changedBy,
                          datetime('now','localtime'))",
                        SqlHelper.Param("@jnl", entry.JournalNo),
                        SqlHelper.Param("@date", entry.DocDate),
                        SqlHelper.Param("@remark", entry.Remark ?? ""),
                        SqlHelper.Param("@period", entry.PeriodCode),
                        SqlHelper.Param("@reg", registerId),
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

            // Debit each tender to its own account (F16). Change is only ever given from
            // cash, so the cash portion is net of change; card and voucher tenders go to
            // their own accounts and no longer inflate cash.
            long cashPortion = sale.CashAmount - sale.ChangeAmount;
            long cardPortion = sale.NonCash;
            long voucherPortion = sale.VoucherAmount;
            long tenderSum = cashPortion + cardPortion + voucherPortion;

            if (cashPortion >= 0 && tenderSum == sale.TotalValue)
            {
                // Proper tender breakdown — post each tender to its own account.
                if (cashPortion > 0)
                {
                    entry.Lines.Add(new JournalLine
                    {
                        AccountCode = cashAccountCode,
                        Debit = cashPortion,
                        Remark = "Cash sale"
                    });
                }
                if (cardPortion > 0)
                {
                    entry.Lines.Add(new JournalLine
                    {
                        AccountCode = GetCardClearingAccount(),
                        Debit = cardPortion,
                        Remark = "Card tender"
                    });
                }
                if (voucherPortion > 0)
                {
                    entry.Lines.Add(new JournalLine
                    {
                        AccountCode = GetVoucherAccount(),
                        Debit = voucherPortion,
                        Remark = "Voucher tender"
                    });
                }
            }
            else
            {
                // Legacy / incomplete tender data (e.g. migrated sales whose cash_amount /
                // non_cash / voucher_amount do not reconcile to total_value) — fall back to
                // the original single cash debit so batch posting is never blocked by a
                // historical row. New sales with proper tender data take the split path above.
                entry.Lines.Add(new JournalLine
                {
                    AccountCode = cashAccountCode,
                    Debit = sale.TotalValue,
                    Remark = "Cash sale"
                });
            }

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

        // Goods receipt (BPB): stock is in, the supplier has not billed yet.
        // Dr Inventory / Cr GRNI at the receipt's line value.
        public void PostReceiptJournal(Purchase receipt, List<PurchaseItem> items)
        {
            long value = items.Sum(i => i.Value);
            if (value == 0) return;

            var entry = new JournalEntry
            {
                JournalNo = receipt.JournalNo,
                DocDate = receipt.DocDate,
                Remark = "Goods receipt " + receipt.JournalNo,
                PeriodCode = receipt.PeriodCode,
                ChangedBy = receipt.ChangedBy
            };

            entry.Lines.Add(new JournalLine
            {
                AccountCode = GetInventoryAccount(),
                SubCode = receipt.SubCode,
                Debit = value,
                Remark = "Goods received"
            });
            entry.Lines.Add(new JournalLine
            {
                AccountCode = GetGrniAccount(),
                SubCode = receipt.SubCode,
                Credit = value,
                Remark = "Received not invoiced"
            });

            ValidateJournalEntry(entry);
            PostGlLines(entry);
        }

        // Purchase invoice (MSK). Lines billing a BPB clear GRNI at the BPB's value; the rest
        // of the invoice total (unlinked / PO lines, price differences, header discount, VAT)
        // goes to inventory — a credit when the invoice comes in below the BPB value.
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

            long grni = GetReceiptValueBilled(purchase);
            long inventory = purchase.TotalValue - grni;

            if (grni > 0)
            {
                entry.Lines.Add(new JournalLine
                {
                    AccountCode = GetGrniAccount(),
                    SubCode = purchase.SubCode,
                    Debit = grni,
                    Remark = "Clear received not invoiced"
                });
            }

            if (inventory != 0)
            {
                entry.Lines.Add(new JournalLine
                {
                    AccountCode = GetInventoryAccount(),
                    SubCode = purchase.SubCode,
                    Debit = inventory > 0 ? inventory : 0,
                    Credit = inventory < 0 ? -inventory : 0,
                    Remark = grni > 0 ? "Purchase inventory / price difference" : "Purchase inventory"
                });
            }

            // A zero-value invoice (all bonus goods) has no AP; it only reverses the BPB value.
            if (purchase.TotalValue < 0)
                throw new InvalidOperationException("Purchase invoice total cannot be negative: " + purchase.JournalNo);
            if (purchase.TotalValue > 0)
            {
                entry.Lines.Add(new JournalLine
                {
                    AccountCode = GetPayablesAccount(),
                    SubCode = purchase.SubCode,
                    Credit = purchase.TotalValue,
                    Remark = "Accounts payable"
                });
            }

            ValidateJournalEntry(entry);
            PostGlLines(entry);
        }

        // Value at which the invoice's BPB-linked lines were received — what the receipt put
        // into GRNI. Uses the BPB's weighted value per product (a BPB may carry the same
        // product at several prices, e.g. a Rp 0 bonus line). Earlier invoices on the same BPB
        // (lower id) are counted first, so the billing that completes a product clears exactly
        // the remaining value and no rounding residue is left in GRNI.
        private long GetReceiptValueBilled(Purchase invoice)
        {
            long total = 0;
            var billedByLink = _purchaseRepo.GetItems(invoice.JournalNo)
                .Where(i => !string.IsNullOrEmpty(i.OrderRef))
                .GroupBy(i => (i.OrderRef, i.ProductCode));

            foreach (var link in billedByLink)
            {
                var receipt = _purchaseRepo.GetByJournalNo(link.Key.OrderRef);
                if (receipt?.DocType != "RECEIPT") continue;

                var received = _purchaseRepo.GetItems(link.Key.OrderRef)
                    .Where(i => i.ProductCode == link.Key.ProductCode).ToList();
                long receivedQty = received.Sum(i => (long)i.Quantity);
                long receivedValue = received.Sum(i => i.Value);
                if (receivedQty <= 0) continue;

                long before = SqlHelper.ExecuteScalar<long>(_db,
                    @"SELECT COALESCE(SUM(pi.quantity), 0) FROM purchase_items pi
                      JOIN purchases p ON p.journal_no = pi.journal_no
                      WHERE pi.order_ref = @ref AND pi.product_code = @product
                        AND p.doc_type = 'PURCHASE' AND p.control != 3 AND p.id < @id",
                    SqlHelper.Param("@ref", link.Key.OrderRef),
                    SqlHelper.Param("@product", link.Key.ProductCode),
                    SqlHelper.Param("@id", invoice.Id));
                long after = before + link.Sum(i => (long)i.Quantity);

                total += ValueUpTo(after, receivedQty, receivedValue) - ValueUpTo(before, receivedQty, receivedValue);
            }
            return total;
        }

        private static long ValueUpTo(long qty, long receivedQty, long receivedValue)
        {
            return qty >= receivedQty ? receivedValue : receivedValue * qty / receivedQty;
        }

        // Stock-out (usage/damage/loss) or opname: the net value that left (or entered)
        // stock on this document's movements is booked against the stock-adjustment account.
        // Returns false when there is no stock value to book (e.g. migrated legacy documents,
        // which have no per-document movements, or items at zero cost).
        public bool PostStockAdjustmentJournal(StockAdjustment adjustment)
        {
            long net = SqlHelper.ExecuteScalar<long>(_db,
                "SELECT COALESCE(SUM(val_in), 0) - COALESCE(SUM(val_out), 0) FROM stock_movements WHERE journal_no = @jnl",
                SqlHelper.Param("@jnl", adjustment.JournalNo));
            if (net == 0) return false;

            var entry = new JournalEntry
            {
                JournalNo = adjustment.JournalNo,
                DocDate = adjustment.DocDate,
                Remark = "Stock adjustment " + adjustment.DocType + " " + adjustment.JournalNo,
                PeriodCode = adjustment.PeriodCode,
                ChangedBy = adjustment.ChangedBy
            };

            long amount = Math.Abs(net);
            bool loss = net < 0;
            entry.Lines.Add(new JournalLine
            {
                AccountCode = GetStockAdjustmentAccount(),
                Debit = loss ? amount : 0,
                Credit = loss ? 0 : amount,
                Remark = loss ? "Stock loss / usage" : "Stock surplus"
            });
            entry.Lines.Add(new JournalLine
            {
                AccountCode = GetInventoryAccount(),
                Debit = loss ? 0 : amount,
                Credit = loss ? amount : 0,
                Remark = "Inventory adjustment"
            });

            ValidateJournalEntry(entry);
            PostGlLines(entry);
            return true;
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

        // No defaults: required config only once a BPB / stock adjustment is actually posted.
        private string GetGrniAccount()
        {
            return GetConfigAccount("GRNI", null);
        }

        private string GetStockAdjustmentAccount()
        {
            return GetConfigAccount("STOCK_ADJUSTMENT", null);
        }

        // Resolves a GL account from config (key "ACCOUNT_<key>"), falling back to
        // defaultCode. FAIL-CLOSED: throws a clear, actionable error if the mapping is
        // unset (null default) or resolves to a code that does not exist in the chart of
        // accounts, rather than silently posting to a bogus account (F16). Callers only
        // hit the card/voucher accounts when a card/voucher sale is actually posted.
        private string GetConfigAccount(string key, string defaultCode)
        {
            var config = SqlHelper.QuerySingle(_db,
                "SELECT value FROM config WHERE key = @key",
                r => SqlHelper.GetString(r, "value"),
                SqlHelper.Param("@key", "ACCOUNT_" + key));

            string code = string.IsNullOrEmpty(config) ? defaultCode : config;

            if (string.IsNullOrEmpty(code))
            {
                throw new InvalidOperationException(string.Format(
                    "GL account not configured: set config key 'ACCOUNT_{0}' to a valid account code before posting.",
                    key));
            }
            if (_accountRepo.GetByCode(code) == null)
            {
                throw new InvalidOperationException(string.Format(
                    "GL account 'ACCOUNT_{0}' = '{1}' does not exist in the chart of accounts. Fix the config.",
                    key, code));
            }
            return code;
        }

        // Card and voucher tender accounts have NO default — they are required config when
        // a card/voucher sale is posted, and fail-closed if unset (F16).
        private string GetCardClearingAccount()
        {
            return GetConfigAccount("CARD_CLEARING", null);
        }

        private string GetVoucherAccount()
        {
            return GetConfigAccount("VOUCHER", null);
        }
    }
}
