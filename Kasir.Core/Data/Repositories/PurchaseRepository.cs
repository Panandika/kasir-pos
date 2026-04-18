using System.Collections.Generic;
using Microsoft.Data.Sqlite;
using Kasir.Models;

namespace Kasir.Data.Repositories
{
    public class PurchaseRepository
    {
        private readonly SqliteConnection _db;

        public PurchaseRepository(SqliteConnection db)
        {
            _db = db;
        }

        public int Insert(Purchase purchase, List<PurchaseItem> items)
        {
            using (var txn = _db.BeginTransaction())
            {
                try
                {
                    SqlHelper.ExecuteNonQuery(_db,
                        @"INSERT INTO purchases (doc_type, journal_no, doc_date, account_code, sub_code,
                          ref_no, remark, warehouse, disc_pct, disc2, vat_flag, gross_amount,
                          total_disc, vat_amount, total_value, due_date, control,
                          period_code, register_id, changed_by, changed_at)
                          VALUES (@type, @jnl, @date, @acc, @sub, @ref, @remark, @wh, @disc, @disc2,
                          @vat, @gross, @totalDisc, @vatAmt, @total, @due, @control,
                          @period, @reg, @changedBy, datetime('now','localtime'))",
                        SqlHelper.Param("@type", purchase.DocType),
                        SqlHelper.Param("@jnl", purchase.JournalNo),
                        SqlHelper.Param("@date", purchase.DocDate),
                        SqlHelper.Param("@acc", purchase.AccountCode ?? ""),
                        SqlHelper.Param("@sub", purchase.SubCode ?? ""),
                        SqlHelper.Param("@ref", purchase.RefNo ?? ""),
                        SqlHelper.Param("@remark", purchase.Remark ?? ""),
                        SqlHelper.Param("@wh", purchase.Warehouse ?? ""),
                        SqlHelper.Param("@disc", purchase.DiscPct),
                        SqlHelper.Param("@disc2", purchase.Disc2Pct),
                        SqlHelper.Param("@vat", purchase.VatFlag ?? "N"),
                        SqlHelper.Param("@gross", purchase.GrossAmount),
                        SqlHelper.Param("@totalDisc", purchase.TotalDisc),
                        SqlHelper.Param("@vatAmt", purchase.VatAmount),
                        SqlHelper.Param("@total", purchase.TotalValue),
                        SqlHelper.Param("@due", purchase.DueDate ?? ""),
                        SqlHelper.Param("@control", purchase.Control),
                        SqlHelper.Param("@period", purchase.PeriodCode),
                        SqlHelper.Param("@reg", purchase.RegisterId ?? "01"),
                        SqlHelper.Param("@changedBy", purchase.ChangedBy));

                    foreach (var item in items)
                    {
                        SqlHelper.ExecuteNonQuery(_db,
                            @"INSERT INTO purchase_items (journal_no, product_code, remark, quantity,
                              value, unit_price, disc_pct, disc_value)
                              VALUES (@jnl, @product, @remark, @qty, @val, @price, @disc, @discVal)",
                            SqlHelper.Param("@jnl", purchase.JournalNo),
                            SqlHelper.Param("@product", item.ProductCode),
                            SqlHelper.Param("@remark", item.Remark ?? ""),
                            SqlHelper.Param("@qty", item.Quantity),
                            SqlHelper.Param("@val", item.Value),
                            SqlHelper.Param("@price", item.UnitPrice),
                            SqlHelper.Param("@disc", item.DiscPct),
                            SqlHelper.Param("@discVal", item.DiscValue));
                    }

                    txn.Commit();
                    return (int)SqlHelper.LastInsertRowId(_db);
                }
                catch { txn.Rollback(); throw; }
            }
        }

        public Purchase GetByJournalNo(string journalNo)
        {
            return SqlHelper.QuerySingle(_db,
                "SELECT * FROM purchases WHERE journal_no = @jnl",
                MapPurchase, SqlHelper.Param("@jnl", journalNo));
        }

        public List<Purchase> GetByDateRange(string dateFrom, string dateTo, string docType)
        {
            return SqlHelper.Query(_db,
                @"SELECT * FROM purchases WHERE doc_date >= @from AND doc_date <= @to
                  AND doc_type = @type AND control != 3 ORDER BY journal_no",
                MapPurchase,
                SqlHelper.Param("@from", dateFrom),
                SqlHelper.Param("@to", dateTo),
                SqlHelper.Param("@type", docType));
        }

        public List<PurchaseItem> GetItems(string journalNo)
        {
            return SqlHelper.Query(_db,
                "SELECT * FROM purchase_items WHERE journal_no = @jnl ORDER BY id",
                MapPurchaseItem, SqlHelper.Param("@jnl", journalNo));
        }

        private static Purchase MapPurchase(SqliteDataReader r)
        {
            return new Purchase
            {
                Id = SqlHelper.GetInt(r, "id"),
                DocType = SqlHelper.GetString(r, "doc_type"),
                JournalNo = SqlHelper.GetString(r, "journal_no"),
                DocDate = SqlHelper.GetString(r, "doc_date"),
                AccountCode = SqlHelper.GetString(r, "account_code"),
                SubCode = SqlHelper.GetString(r, "sub_code"),
                RefNo = SqlHelper.GetString(r, "ref_no"),
                Remark = SqlHelper.GetString(r, "remark"),
                Warehouse = SqlHelper.GetString(r, "warehouse"),
                DiscPct = SqlHelper.GetInt(r, "disc_pct"),
                Disc2Pct = SqlHelper.GetInt(r, "disc2_pct"),
                VatFlag = SqlHelper.GetString(r, "vat_flag"),
                GrossAmount = SqlHelper.GetLong(r, "gross_amount"),
                TotalDisc = SqlHelper.GetLong(r, "total_disc"),
                VatAmount = SqlHelper.GetLong(r, "vat_amount"),
                TotalValue = SqlHelper.GetLong(r, "total_value"),
                DueDate = SqlHelper.GetString(r, "due_date"),
                Terms = SqlHelper.GetInt(r, "terms"),
                Control = SqlHelper.GetInt(r, "control"),
                PeriodCode = SqlHelper.GetString(r, "period_code"),
                RegisterId = SqlHelper.GetString(r, "register_id"),
                ChangedBy = SqlHelper.GetInt(r, "changed_by"),
                ChangedAt = SqlHelper.GetString(r, "changed_at")
            };
        }

        private static PurchaseItem MapPurchaseItem(SqliteDataReader r)
        {
            return new PurchaseItem
            {
                Id = SqlHelper.GetInt(r, "id"),
                JournalNo = SqlHelper.GetString(r, "journal_no"),
                ProductCode = SqlHelper.GetString(r, "product_code"),
                Remark = SqlHelper.GetString(r, "remark"),
                Quantity = SqlHelper.GetInt(r, "quantity"),
                Value = SqlHelper.GetLong(r, "value"),
                UnitPrice = SqlHelper.GetInt(r, "unit_price"),
                DiscPct = SqlHelper.GetInt(r, "disc_pct"),
                DiscValue = SqlHelper.GetLong(r, "disc_value"),
                Unit = SqlHelper.GetString(r, "unit")
            };
        }
    }
}
