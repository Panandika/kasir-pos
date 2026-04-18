using System.Collections.Generic;
using Microsoft.Data.Sqlite;
using Kasir.Models;

namespace Kasir.Data.Repositories
{
    public class StockMovementRepository
    {
        private readonly SqliteConnection _db;

        public StockMovementRepository(SqliteConnection db)
        {
            _db = db;
        }

        public int Insert(StockMovement m)
        {
            SqlHelper.ExecuteNonQuery(_db,
                @"INSERT INTO stock_movements (product_code, vendor_code, dept_code, location_code,
                  account_code, sub_code, journal_no, movement_type, doc_date, period_code,
                  qty_in, qty_out, val_in, val_out, cost_price, is_posted, changed_by, changed_at)
                  VALUES (@product, @vendor, @dept, @loc, @acc, @sub, @jnl, @type, @date, @period,
                  @qtyIn, @qtyOut, @valIn, @valOut, @cost, 0, @changedBy, datetime('now','localtime'))",
                SqlHelper.Param("@product", m.ProductCode),
                SqlHelper.Param("@vendor", m.VendorCode ?? ""),
                SqlHelper.Param("@dept", m.DeptCode ?? ""),
                SqlHelper.Param("@loc", m.LocationCode ?? ""),
                SqlHelper.Param("@acc", m.AccountCode ?? ""),
                SqlHelper.Param("@sub", m.SubCode ?? ""),
                SqlHelper.Param("@jnl", m.JournalNo),
                SqlHelper.Param("@type", m.MovementType),
                SqlHelper.Param("@date", m.DocDate),
                SqlHelper.Param("@period", m.PeriodCode),
                SqlHelper.Param("@qtyIn", m.QtyIn),
                SqlHelper.Param("@qtyOut", m.QtyOut),
                SqlHelper.Param("@valIn", m.ValIn),
                SqlHelper.Param("@valOut", m.ValOut),
                SqlHelper.Param("@cost", m.CostPrice),
                SqlHelper.Param("@changedBy", m.ChangedBy));

            return (int)SqlHelper.LastInsertRowId(_db);
        }

        public int GetStockOnHand(string productCode)
        {
            return SqlHelper.ExecuteScalar<int>(_db,
                "SELECT COALESCE(SUM(qty_in) - SUM(qty_out), 0) FROM stock_movements WHERE product_code = @code",
                SqlHelper.Param("@code", productCode));
        }

        public int GetStockOnHandByLocation(string productCode, string locationCode)
        {
            return SqlHelper.ExecuteScalar<int>(_db,
                @"SELECT COALESCE(SUM(qty_in) - SUM(qty_out), 0) FROM stock_movements
                  WHERE product_code = @code AND location_code = @loc",
                SqlHelper.Param("@code", productCode),
                SqlHelper.Param("@loc", locationCode));
        }

        public List<StockMovement> GetPurchaseMovements(string productCode)
        {
            return SqlHelper.Query(_db,
                @"SELECT * FROM stock_movements
                  WHERE product_code = @code AND movement_type = 'PURCHASE' AND qty_in > 0
                  ORDER BY doc_date ASC, id ASC",
                MapMovement,
                SqlHelper.Param("@code", productCode));
        }

        public List<StockMovement> GetByProduct(string productCode, string dateFrom, string dateTo)
        {
            return SqlHelper.Query(_db,
                @"SELECT * FROM stock_movements
                  WHERE product_code = @code AND doc_date >= @from AND doc_date <= @to
                  ORDER BY doc_date ASC, id ASC",
                MapMovement,
                SqlHelper.Param("@code", productCode),
                SqlHelper.Param("@from", dateFrom),
                SqlHelper.Param("@to", dateTo));
        }

        public List<StockMovement> GetByJournal(string journalNo)
        {
            return SqlHelper.Query(_db,
                "SELECT * FROM stock_movements WHERE journal_no = @jnl ORDER BY id",
                MapMovement,
                SqlHelper.Param("@jnl", journalNo));
        }

        private static StockMovement MapMovement(SqliteDataReader reader)
        {
            return new StockMovement
            {
                Id = SqlHelper.GetInt(reader, "id"),
                ProductCode = SqlHelper.GetString(reader, "product_code"),
                VendorCode = SqlHelper.GetString(reader, "vendor_code"),
                DeptCode = SqlHelper.GetString(reader, "dept_code"),
                LocationCode = SqlHelper.GetString(reader, "location_code"),
                AccountCode = SqlHelper.GetString(reader, "account_code"),
                SubCode = SqlHelper.GetString(reader, "sub_code"),
                JournalNo = SqlHelper.GetString(reader, "journal_no"),
                MovementType = SqlHelper.GetString(reader, "movement_type"),
                DocDate = SqlHelper.GetString(reader, "doc_date"),
                PeriodCode = SqlHelper.GetString(reader, "period_code"),
                QtyIn = SqlHelper.GetInt(reader, "qty_in"),
                QtyOut = SqlHelper.GetInt(reader, "qty_out"),
                ValIn = SqlHelper.GetLong(reader, "val_in"),
                ValOut = SqlHelper.GetLong(reader, "val_out"),
                CostPrice = SqlHelper.GetInt(reader, "cost_price"),
                IsPosted = SqlHelper.GetInt(reader, "is_posted"),
                ChangedBy = SqlHelper.GetInt(reader, "changed_by"),
                ChangedAt = SqlHelper.GetString(reader, "changed_at")
            };
        }
    }
}
