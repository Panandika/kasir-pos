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
                    int id = InsertWithoutTransaction(purchase, items);
                    txn.Commit();
                    return id;
                }
                catch { txn.Rollback(); throw; }
            }
        }

        // Body of Insert without its own transaction, so callers (e.g. PurchasingService)
        // can enlist the purchase + its stock movements + AP entry in one atomic unit (F19).
        public int InsertWithoutTransaction(Purchase purchase, List<PurchaseItem> items)
        {
                    SqlHelper.ExecuteNonQuery(_db,
                        @"INSERT INTO purchases (doc_type, journal_no, doc_date, account_code, sub_code,
                          tax_invoice, delivery_note, tax_inv_date, ref_no, remark, warehouse, disc_pct, disc2_pct, vat_flag, gross_amount,
                          total_disc, vat_amount, total_value, due_date, received_date, terms, control,
                          period_code, register_id, changed_by, changed_at)
                          VALUES (@type, @jnl, @date, @acc, @sub, @taxInvoice, @deliveryNote, @taxInvDate, @ref, @remark, @wh, @disc, @disc2,
                          @vat, @gross, @totalDisc, @vatAmt, @total, @due, @received, @terms, @control,
                          @period, @reg, @changedBy, datetime('now','localtime'))",
                        SqlHelper.Param("@type", purchase.DocType),
                        SqlHelper.Param("@jnl", purchase.JournalNo),
                        SqlHelper.Param("@date", purchase.DocDate),
                        SqlHelper.Param("@acc", purchase.AccountCode ?? ""),
                        SqlHelper.Param("@sub", purchase.SubCode ?? ""),
                        SqlHelper.Param("@taxInvoice", purchase.TaxInvoice ?? ""),
                        SqlHelper.Param("@deliveryNote", purchase.DeliveryNote ?? ""),
                        SqlHelper.Param("@taxInvDate", purchase.TaxInvDate ?? ""),
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
                        SqlHelper.Param("@received", purchase.ReceivedDate ?? ""),
                        SqlHelper.Param("@terms", purchase.Terms),
                        SqlHelper.Param("@control", purchase.Control),
                        SqlHelper.Param("@period", purchase.PeriodCode),
                        SqlHelper.Param("@reg", purchase.RegisterId ?? "01"),
                        SqlHelper.Param("@changedBy", purchase.ChangedBy));

                    foreach (var item in items)
                    {
                        SqlHelper.ExecuteNonQuery(_db,
                            @"INSERT INTO purchase_items (journal_no, order_ref, product_code, remark, quantity,
                              value, unit_price, disc_pct, disc_value, qty_order)
                              VALUES (@jnl, @orderRef, @product, @remark, @qty, @val, @price, @disc, @discVal, @qtyOrder)",
                            SqlHelper.Param("@jnl", purchase.JournalNo),
                            SqlHelper.Param("@product", item.ProductCode),
                            SqlHelper.Param("@remark", item.Remark ?? ""),
                            SqlHelper.Param("@qty", item.Quantity),
                            SqlHelper.Param("@val", item.Value),
                            SqlHelper.Param("@price", item.UnitPrice),
                            SqlHelper.Param("@disc", item.DiscPct),
                            SqlHelper.Param("@discVal", item.DiscValue),
                            SqlHelper.Param("@orderRef", item.OrderRef ?? ""),
                            SqlHelper.Param("@qtyOrder", item.QtyOrder));
                    }

            return (int)SqlHelper.LastInsertRowId(_db);
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

        // Qty per product on non-voided lines whose order_ref points at refNo, limited to
        // the given document types. Used to derive PO receipt and BPB billing progress.
        public Dictionary<string, int> GetLinkedQuantities(string refNo, params string[] docTypes)
        {
            var parameters = new List<SqliteParameter> { SqlHelper.Param("@ref", refNo) };
            var typeParams = new List<string>();
            for (int i = 0; i < docTypes.Length; i++)
            {
                typeParams.Add("@t" + i);
                parameters.Add(SqlHelper.Param("@t" + i, docTypes[i]));
            }

            var rows = SqlHelper.Query(_db,
                @"SELECT pi.product_code, SUM(pi.quantity) AS qty
                  FROM purchase_items pi
                  JOIN purchases p ON p.journal_no = pi.journal_no
                  WHERE pi.order_ref = @ref AND p.control != 3
                    AND p.doc_type IN (" + string.Join(", ", typeParams) + @")
                  GROUP BY pi.product_code",
                r => new KeyValuePair<string, int>(
                    SqlHelper.GetString(r, "product_code"), SqlHelper.GetInt(r, "qty")),
                parameters.ToArray());

            var result = new Dictionary<string, int>();
            foreach (var row in rows) result[row.Key] = row.Value;
            return result;
        }

        public List<Purchase> GetActiveByVendor(string vendorCode, string docType)
        {
            return SqlHelper.Query(_db,
                @"SELECT * FROM purchases WHERE sub_code = @sub AND doc_type = @type AND control != 3
                  ORDER BY doc_date, journal_no",
                MapPurchase,
                SqlHelper.Param("@sub", vendorCode),
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
                TaxInvoice = SqlHelper.GetString(r, "tax_invoice"),
                DeliveryNote = SqlHelper.GetString(r, "delivery_note"),
                TaxInvDate = SqlHelper.GetString(r, "tax_inv_date"),
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
                ReceivedDate = SqlHelper.GetString(r, "received_date"),
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
                UnitPrice = SqlHelper.GetLong(r, "unit_price"),
                DiscPct = SqlHelper.GetInt(r, "disc_pct"),
                DiscValue = SqlHelper.GetLong(r, "disc_value"),
                OrderRef = SqlHelper.GetString(r, "order_ref"),
                QtyOrder = SqlHelper.GetInt(r, "qty_order"),
                Unit = SqlHelper.GetString(r, "unit")
            };
        }
    }
}
