using System.Collections.Generic;
using System.Data.SQLite;
using Kasir.Models;

namespace Kasir.Data.Repositories
{
    public class OrderRepository
    {
        private readonly SQLiteConnection _db;

        public OrderRepository(SQLiteConnection db)
        {
            _db = db;
        }

        public int Insert(Order order, List<OrderItem> items)
        {
            using (var txn = _db.BeginTransaction())
            {
                try
                {
                    SqlHelper.ExecuteNonQuery(_db,
                        @"INSERT INTO orders (doc_type, journal_no, doc_date, account_code, sub_code,
                          ref_no, remark, warehouse, disc_pct, total_value, due_date, control,
                          period_code, register_id, changed_by, changed_at)
                          VALUES (@type, @jnl, @date, @acc, @sub, @ref, @remark, @wh, @disc,
                          @total, @due, @control, @period, @reg, @changedBy, datetime('now','localtime'))",
                        SqlHelper.Param("@type", order.DocType ?? "PURCHASE_ORDER"),
                        SqlHelper.Param("@jnl", order.JournalNo),
                        SqlHelper.Param("@date", order.DocDate),
                        SqlHelper.Param("@acc", order.AccountCode ?? ""),
                        SqlHelper.Param("@sub", order.SubCode ?? ""),
                        SqlHelper.Param("@ref", order.RefNo ?? ""),
                        SqlHelper.Param("@remark", order.Remark ?? ""),
                        SqlHelper.Param("@wh", order.Warehouse ?? ""),
                        SqlHelper.Param("@disc", order.DiscPct),
                        SqlHelper.Param("@total", order.TotalValue),
                        SqlHelper.Param("@due", order.DueDate ?? ""),
                        SqlHelper.Param("@control", order.Control),
                        SqlHelper.Param("@period", order.PeriodCode),
                        SqlHelper.Param("@reg", order.RegisterId ?? "01"),
                        SqlHelper.Param("@changedBy", order.ChangedBy));

                    foreach (var item in items)
                    {
                        SqlHelper.ExecuteNonQuery(_db,
                            @"INSERT INTO order_items (journal_no, product_code, remark, quantity, value, unit_price, unit)
                              VALUES (@jnl, @product, @remark, @qty, @val, @price, @unit)",
                            SqlHelper.Param("@jnl", order.JournalNo),
                            SqlHelper.Param("@product", item.ProductCode),
                            SqlHelper.Param("@remark", item.Remark ?? ""),
                            SqlHelper.Param("@qty", item.Quantity),
                            SqlHelper.Param("@val", item.Value),
                            SqlHelper.Param("@price", item.UnitPrice),
                            SqlHelper.Param("@unit", item.Unit ?? ""));
                    }

                    txn.Commit();
                    return (int)_db.LastInsertRowId;
                }
                catch { txn.Rollback(); throw; }
            }
        }

        public Order GetByJournalNo(string journalNo)
        {
            return SqlHelper.QuerySingle(_db,
                "SELECT * FROM orders WHERE journal_no = @jnl",
                MapOrder, SqlHelper.Param("@jnl", journalNo));
        }

        public List<Order> GetOpenOrders(string vendorCode)
        {
            return SqlHelper.Query(_db,
                "SELECT * FROM orders WHERE sub_code = @sub AND control = 1 AND doc_type = 'PURCHASE_ORDER' ORDER BY doc_date DESC",
                MapOrder, SqlHelper.Param("@sub", vendorCode));
        }

        public List<OrderItem> GetItems(string journalNo)
        {
            return SqlHelper.Query(_db,
                "SELECT * FROM order_items WHERE journal_no = @jnl ORDER BY id",
                MapOrderItem, SqlHelper.Param("@jnl", journalNo));
        }

        private static Order MapOrder(SQLiteDataReader r)
        {
            return new Order
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
                TotalValue = SqlHelper.GetLong(r, "total_value"),
                DueDate = SqlHelper.GetString(r, "due_date"),
                Control = SqlHelper.GetInt(r, "control"),
                PeriodCode = SqlHelper.GetString(r, "period_code"),
                RegisterId = SqlHelper.GetString(r, "register_id"),
                ChangedBy = SqlHelper.GetInt(r, "changed_by"),
                ChangedAt = SqlHelper.GetString(r, "changed_at")
            };
        }

        private static OrderItem MapOrderItem(SQLiteDataReader r)
        {
            return new OrderItem
            {
                Id = SqlHelper.GetInt(r, "id"),
                JournalNo = SqlHelper.GetString(r, "journal_no"),
                ProductCode = SqlHelper.GetString(r, "product_code"),
                Remark = SqlHelper.GetString(r, "remark"),
                Quantity = SqlHelper.GetInt(r, "quantity"),
                Value = SqlHelper.GetLong(r, "value"),
                UnitPrice = SqlHelper.GetInt(r, "unit_price"),
                Unit = SqlHelper.GetString(r, "unit")
            };
        }
    }
}
