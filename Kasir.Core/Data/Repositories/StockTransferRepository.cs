using System.Collections.Generic;
using System.Data.SQLite;
using Kasir.Models;

namespace Kasir.Data.Repositories
{
    public class StockTransferRepository
    {
        private readonly SQLiteConnection _db;

        public StockTransferRepository(SQLiteConnection db)
        {
            _db = db;
        }

        public int Insert(StockTransfer header, List<StockTransferItem> items)
        {
            using (var txn = _db.BeginTransaction())
            {
                try
                {
                    SqlHelper.ExecuteNonQuery(_db,
                        @"INSERT INTO stock_transfers (doc_type, journal_no, doc_date, from_location,
                          to_location, remark, control, period_code, register_id, changed_by, changed_at)
                          VALUES (@type, @jnl, @date, @from, @to, @remark, @control,
                          @period, @reg, @changedBy, datetime('now','localtime'))",
                        SqlHelper.Param("@type", header.DocType ?? "TRANSFER"),
                        SqlHelper.Param("@jnl", header.JournalNo),
                        SqlHelper.Param("@date", header.DocDate),
                        SqlHelper.Param("@from", header.FromLocation),
                        SqlHelper.Param("@to", header.ToLocation),
                        SqlHelper.Param("@remark", header.Remark ?? ""),
                        SqlHelper.Param("@control", header.Control),
                        SqlHelper.Param("@period", header.PeriodCode),
                        SqlHelper.Param("@reg", header.RegisterId ?? "01"),
                        SqlHelper.Param("@changedBy", header.ChangedBy));

                    foreach (var item in items)
                    {
                        SqlHelper.ExecuteNonQuery(_db,
                            @"INSERT INTO stock_transfer_items (journal_no, product_code, quantity, cost_price, value)
                              VALUES (@jnl, @product, @qty, @cost, @val)",
                            SqlHelper.Param("@jnl", header.JournalNo),
                            SqlHelper.Param("@product", item.ProductCode),
                            SqlHelper.Param("@qty", item.Quantity),
                            SqlHelper.Param("@cost", item.CostPrice),
                            SqlHelper.Param("@val", item.Value));
                    }

                    txn.Commit();
                    return (int)_db.LastInsertRowId;
                }
                catch { txn.Rollback(); throw; }
            }
        }

        public List<StockTransfer> GetByDateRange(string from, string to)
        {
            return SqlHelper.Query(_db,
                "SELECT * FROM stock_transfers WHERE doc_date >= @from AND doc_date <= @to ORDER BY journal_no",
                r => new StockTransfer
                {
                    Id = SqlHelper.GetInt(r, "id"),
                    JournalNo = SqlHelper.GetString(r, "journal_no"),
                    DocDate = SqlHelper.GetString(r, "doc_date"),
                    FromLocation = SqlHelper.GetString(r, "from_location"),
                    ToLocation = SqlHelper.GetString(r, "to_location"),
                    Remark = SqlHelper.GetString(r, "remark"),
                    Control = SqlHelper.GetInt(r, "control")
                },
                SqlHelper.Param("@from", from), SqlHelper.Param("@to", to));
        }
    }
}
