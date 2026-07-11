using System.Collections.Generic;
using Microsoft.Data.Sqlite;
using Kasir.Models;

namespace Kasir.Data.Repositories
{
    public class SaleRepository
    {
        private readonly SqliteConnection _db;

        public SaleRepository(SqliteConnection db)
        {
            _db = db;
        }

        public int Insert(Sale sale, List<SaleItem> items)
        {
            using (var txn = _db.BeginTransaction())
            {
                try
                {
                    int saleId = InsertWithoutTransaction(sale, items);
                    txn.Commit();
                    return saleId;
                }
                catch
                {
                    txn.Rollback();
                    throw;
                }
            }
        }

        public int InsertWithoutTransaction(Sale sale, List<SaleItem> items)
        {
            SqlHelper.ExecuteNonQuery(_db,
                @"INSERT INTO sales (doc_type, journal_no, doc_date, account_code, sub_code,
                  member_code, point_value, card_code, cashier, disc_pct, disc2_pct, shift,
                  payment_amount, cash_amount, non_cash, total_value, change_amount, total_disc,
                  card_type, gross_amount, voucher_amount, credit_amount, control,
                  period_code, register_id, changed_by, changed_at)
                  VALUES (@docType, @journalNo, @docDate, @acc, @sub,
                  @member, @points, @card, @cashier, @disc, @disc2, @shift,
                  @payment, @cash, @nonCash, @total, @change, @totalDisc,
                  @cardType, @gross, @voucher, @credit, @control,
                  @period, @register, @changedBy, datetime('now','localtime'))",
                SqlHelper.Param("@docType", sale.DocType ?? "SALE"),
                SqlHelper.Param("@journalNo", sale.JournalNo),
                SqlHelper.Param("@docDate", sale.DocDate),
                SqlHelper.Param("@acc", sale.AccountCode ?? ""),
                SqlHelper.Param("@sub", sale.SubCode ?? ""),
                SqlHelper.Param("@member", sale.MemberCode ?? ""),
                SqlHelper.Param("@points", sale.PointValue),
                SqlHelper.Param("@card", sale.CardCode ?? ""),
                SqlHelper.Param("@cashier", sale.Cashier ?? ""),
                SqlHelper.Param("@disc", sale.DiscPct),
                SqlHelper.Param("@disc2", sale.Disc2Pct),
                SqlHelper.Param("@shift", sale.Shift ?? "1"),
                SqlHelper.Param("@payment", sale.PaymentAmount),
                SqlHelper.Param("@cash", sale.CashAmount),
                SqlHelper.Param("@nonCash", sale.NonCash),
                SqlHelper.Param("@total", sale.TotalValue),
                SqlHelper.Param("@change", sale.ChangeAmount),
                SqlHelper.Param("@totalDisc", sale.TotalDisc),
                SqlHelper.Param("@cardType", sale.CardType ?? ""),
                SqlHelper.Param("@gross", sale.GrossAmount),
                SqlHelper.Param("@voucher", sale.VoucherAmount),
                SqlHelper.Param("@credit", sale.CreditAmount),
                SqlHelper.Param("@control", sale.Control),
                SqlHelper.Param("@period", sale.PeriodCode),
                SqlHelper.Param("@register", sale.RegisterId ?? "01"),
                SqlHelper.Param("@changedBy", sale.ChangedBy));

            int saleId = (int)SqlHelper.LastInsertRowId(_db);

            foreach (var item in items)
            {
                item.JournalNo = sale.JournalNo;
                InsertItem(item);
            }

            return saleId;
        }

        private void InsertItem(SaleItem item)
        {
            SqlHelper.ExecuteNonQuery(_db,
                @"INSERT INTO sale_items (journal_no, product_code, remark, quantity,
                  value, cogs, disc_pct, unit_price, point_value, disc_value)
                  VALUES (@jnl, @product, @remark, @qty,
                  @value, @cogs, @disc, @price, @points, @discValue)",
                SqlHelper.Param("@jnl", item.JournalNo),
                SqlHelper.Param("@product", item.ProductCode),
                SqlHelper.Param("@remark", item.Remark ?? ""),
                SqlHelper.Param("@qty", item.Quantity),
                SqlHelper.Param("@value", item.Value),
                SqlHelper.Param("@cogs", item.Cogs),
                SqlHelper.Param("@disc", item.DiscPct),
                SqlHelper.Param("@price", item.UnitPrice),
                SqlHelper.Param("@points", item.PointValue),
                SqlHelper.Param("@discValue", item.DiscValue));
        }

        public Sale GetByJournalNo(string journalNo)
        {
            return SqlHelper.QuerySingle(_db,
                "SELECT * FROM sales WHERE journal_no = @jnl",
                MapSale,
                SqlHelper.Param("@jnl", journalNo));
        }

        public List<SaleItem> GetItemsByJournalNo(string journalNo)
        {
            return SqlHelper.Query(_db,
                "SELECT * FROM sale_items WHERE journal_no = @jnl ORDER BY id",
                MapSaleItem,
                SqlHelper.Param("@jnl", journalNo));
        }

        public List<Sale> GetByDateRange(string dateFrom, string dateTo)
        {
            return SqlHelper.Query(_db,
                @"SELECT * FROM sales
                  WHERE doc_date >= @from AND doc_date <= @to AND control != 3
                  ORDER BY journal_no",
                MapSale,
                SqlHelper.Param("@from", dateFrom),
                SqlHelper.Param("@to", dateTo));
        }

        public void VoidSale(string journalNo, int userId)
        {
            SqlHelper.ExecuteNonQuery(_db,
                @"UPDATE sales SET control = 3, changed_by = @user,
                  changed_at = datetime('now','localtime')
                  WHERE journal_no = @jnl",
                SqlHelper.Param("@user", userId),
                SqlHelper.Param("@jnl", journalNo));
        }

        public long GetDailyTotal(string date)
        {
            return SqlHelper.ExecuteScalar<long>(_db,
                "SELECT COALESCE(SUM(total_value), 0) FROM sales WHERE doc_date = @date AND control != 3",
                SqlHelper.Param("@date", date));
        }

        /// <summary>
        /// Net cash that should be in the drawer for a given register/shift, scoped to
        /// the shift's open→close time window. Sums (cash_amount - change_amount) for
        /// SALE rows, subtracts the same for SALE_RETURN. Excludes voided rows (control=3)
        /// and edited/replaced rows (control IN (4,5)) to avoid double-counting. Card /
        /// credit / voucher portions are not in cash_amount so do not inflate the result.
        /// </summary>
        /// <param name="openedAt">Shift open timestamp; only sales at/after this count.</param>
        /// <param name="closedAt">Shift close timestamp, or null for an open shift (counts up to now).</param>
        /// <remarks>
        /// The shift number alone (a single char '1'/'2'/…) recurs every day, so filtering
        /// on it without a time window summed ALL historical sales for that shift number
        /// (F03). The open→close window on changed_at (the row's creation time) bounds it
        /// to the current shift.
        /// </remarks>
        public long GetCashSinceShift(string registerId, string shiftNumber, string openedAt, string closedAt = null)
        {
            return SqlHelper.ExecuteScalar<long>(_db,
                @"SELECT COALESCE(SUM(
                      CASE WHEN doc_type = 'SALE'
                           THEN cash_amount - change_amount
                           WHEN doc_type = 'SALE_RETURN'
                           THEN -(cash_amount - change_amount)
                           ELSE 0
                      END
                  ), 0)
                  FROM sales
                  WHERE register_id = @reg
                    AND shift = @shift
                    AND control NOT IN (3, 4, 5)
                    AND changed_at >= @openedAt
                    AND (@closedAt IS NULL OR changed_at <= @closedAt)",
                SqlHelper.Param("@reg", registerId),
                SqlHelper.Param("@shift", shiftNumber),
                SqlHelper.Param("@openedAt", openedAt),
                SqlHelper.Param("@closedAt", closedAt));
        }

        public int GetDailyCount(string date)
        {
            return SqlHelper.ExecuteScalar<int>(_db,
                "SELECT COUNT(*) FROM sales WHERE doc_date = @date AND control != 3",
                SqlHelper.Param("@date", date));
        }

        private static Sale MapSale(SqliteDataReader reader)
        {
            return new Sale
            {
                Id = SqlHelper.GetInt(reader, "id"),
                DocType = SqlHelper.GetString(reader, "doc_type"),
                JournalNo = SqlHelper.GetString(reader, "journal_no"),
                DocDate = SqlHelper.GetString(reader, "doc_date"),
                AccountCode = SqlHelper.GetString(reader, "account_code"),
                SubCode = SqlHelper.GetString(reader, "sub_code"),
                MemberCode = SqlHelper.GetString(reader, "member_code"),
                PointValue = SqlHelper.GetLong(reader, "point_value"),
                CardCode = SqlHelper.GetString(reader, "card_code"),
                Cashier = SqlHelper.GetString(reader, "cashier"),
                DiscPct = SqlHelper.GetInt(reader, "disc_pct"),
                Disc2Pct = SqlHelper.GetInt(reader, "disc2_pct"),
                Shift = SqlHelper.GetString(reader, "shift"),
                PaymentAmount = SqlHelper.GetLong(reader, "payment_amount"),
                CashAmount = SqlHelper.GetLong(reader, "cash_amount"),
                NonCash = SqlHelper.GetLong(reader, "non_cash"),
                TotalValue = SqlHelper.GetLong(reader, "total_value"),
                ChangeAmount = SqlHelper.GetLong(reader, "change_amount"),
                TotalDisc = SqlHelper.GetLong(reader, "total_disc"),
                CardType = SqlHelper.GetString(reader, "card_type"),
                GrossAmount = SqlHelper.GetLong(reader, "gross_amount"),
                VoucherAmount = SqlHelper.GetLong(reader, "voucher_amount"),
                CreditAmount = SqlHelper.GetLong(reader, "credit_amount"),
                Control = SqlHelper.GetInt(reader, "control"),
                PrintCount = SqlHelper.GetInt(reader, "print_count"),
                PeriodCode = SqlHelper.GetString(reader, "period_code"),
                RegisterId = SqlHelper.GetString(reader, "register_id"),
                ChangedBy = SqlHelper.GetInt(reader, "changed_by"),
                ChangedAt = SqlHelper.GetString(reader, "changed_at")
            };
        }

        private static SaleItem MapSaleItem(SqliteDataReader reader)
        {
            return new SaleItem
            {
                Id = SqlHelper.GetInt(reader, "id"),
                JournalNo = SqlHelper.GetString(reader, "journal_no"),
                ProductCode = SqlHelper.GetString(reader, "product_code"),
                Remark = SqlHelper.GetString(reader, "remark"),
                Quantity = SqlHelper.GetInt(reader, "quantity"),
                Value = SqlHelper.GetLong(reader, "value"),
                Cogs = SqlHelper.GetLong(reader, "cogs"),
                DiscPct = SqlHelper.GetInt(reader, "disc_pct"),
                UnitPrice = SqlHelper.GetLong(reader, "unit_price"),
                PointValue = SqlHelper.GetLong(reader, "point_value"),
                DiscValue = SqlHelper.GetLong(reader, "disc_value")
            };
        }
    }
}
