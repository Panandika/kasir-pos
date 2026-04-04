using System.Collections.Generic;
using System.Data.SQLite;
using Kasir.Models;

namespace Kasir.Data.Repositories
{
    public class StockAdjustmentRepository
    {
        private readonly SQLiteConnection _db;

        public StockAdjustmentRepository(SQLiteConnection db)
        {
            _db = db;
        }

        public int Insert(StockAdjustment header, List<StockAdjustmentItem> items)
        {
            using (var txn = _db.BeginTransaction())
            {
                try
                {
                    SqlHelper.ExecuteNonQuery(_db,
                        @"INSERT INTO stock_adjustments (doc_type, journal_no, doc_date, location_code,
                          remark, control, period_code, register_id, changed_by, changed_at)
                          VALUES (@type, @jnl, @date, @loc, @remark, @control,
                          @period, @reg, @changedBy, datetime('now','localtime'))",
                        SqlHelper.Param("@type", header.DocType),
                        SqlHelper.Param("@jnl", header.JournalNo),
                        SqlHelper.Param("@date", header.DocDate),
                        SqlHelper.Param("@loc", header.LocationCode ?? ""),
                        SqlHelper.Param("@remark", header.Remark ?? ""),
                        SqlHelper.Param("@control", header.Control),
                        SqlHelper.Param("@period", header.PeriodCode),
                        SqlHelper.Param("@reg", header.RegisterId ?? "01"),
                        SqlHelper.Param("@changedBy", header.ChangedBy));

                    foreach (var item in items)
                    {
                        SqlHelper.ExecuteNonQuery(_db,
                            @"INSERT INTO stock_adjustment_items (journal_no, product_code, quantity, cost_price, value, reason)
                              VALUES (@jnl, @product, @qty, @cost, @val, @reason)",
                            SqlHelper.Param("@jnl", header.JournalNo),
                            SqlHelper.Param("@product", item.ProductCode),
                            SqlHelper.Param("@qty", item.Quantity),
                            SqlHelper.Param("@cost", item.CostPrice),
                            SqlHelper.Param("@val", item.Value),
                            SqlHelper.Param("@reason", item.Reason ?? ""));
                    }

                    txn.Commit();
                    return (int)_db.LastInsertRowId;
                }
                catch { txn.Rollback(); throw; }
            }
        }

        public List<StockAdjustment> GetByDateRange(string from, string to)
        {
            return SqlHelper.Query(_db,
                "SELECT * FROM stock_adjustments WHERE doc_date >= @from AND doc_date <= @to ORDER BY journal_no",
                r => new StockAdjustment
                {
                    Id = SqlHelper.GetInt(r, "id"),
                    DocType = SqlHelper.GetString(r, "doc_type"),
                    JournalNo = SqlHelper.GetString(r, "journal_no"),
                    DocDate = SqlHelper.GetString(r, "doc_date"),
                    LocationCode = SqlHelper.GetString(r, "location_code"),
                    Remark = SqlHelper.GetString(r, "remark"),
                    Control = SqlHelper.GetInt(r, "control")
                },
                SqlHelper.Param("@from", from), SqlHelper.Param("@to", to));
        }
    }
}
