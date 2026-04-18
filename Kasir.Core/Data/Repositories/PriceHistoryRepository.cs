using System.Collections.Generic;
using System.Data.SQLite;
using Kasir.Models;

namespace Kasir.Data.Repositories
{
    public class PriceHistoryRepository
    {
        private readonly SQLiteConnection _db;

        public PriceHistoryRepository(SQLiteConnection db)
        {
            _db = db;
        }

        public void Insert(PriceHistory entry)
        {
            SqlHelper.ExecuteNonQuery(_db,
                @"INSERT INTO price_history (product_code, doc_date, quantity, value,
                  old_date, old_quantity, old_value, journal_no, period_code)
                  VALUES (@product, @date, @qty, @val, @oldDate, @oldQty, @oldVal, @jnl, @period)",
                SqlHelper.Param("@product", entry.ProductCode),
                SqlHelper.Param("@date", entry.DocDate),
                SqlHelper.Param("@qty", entry.Quantity),
                SqlHelper.Param("@val", entry.Value),
                SqlHelper.Param("@oldDate", entry.OldDate),
                SqlHelper.Param("@oldQty", entry.OldQuantity),
                SqlHelper.Param("@oldVal", entry.OldValue),
                SqlHelper.Param("@jnl", entry.JournalNo),
                SqlHelper.Param("@period", entry.PeriodCode));
        }

        public List<PriceHistory> GetByProduct(string productCode)
        {
            return SqlHelper.Query(_db,
                "SELECT * FROM price_history WHERE product_code = @code ORDER BY doc_date DESC",
                MapEntry,
                SqlHelper.Param("@code", productCode));
        }

        private static PriceHistory MapEntry(SQLiteDataReader reader)
        {
            return new PriceHistory
            {
                Id = SqlHelper.GetInt(reader, "id"),
                ProductCode = SqlHelper.GetString(reader, "product_code"),
                DocDate = SqlHelper.GetString(reader, "doc_date"),
                Quantity = SqlHelper.GetInt(reader, "quantity"),
                Value = SqlHelper.GetLong(reader, "value"),
                OldDate = SqlHelper.GetString(reader, "old_date"),
                OldQuantity = SqlHelper.GetInt(reader, "old_quantity"),
                OldValue = SqlHelper.GetLong(reader, "old_value"),
                JournalNo = SqlHelper.GetString(reader, "journal_no"),
                PeriodCode = SqlHelper.GetString(reader, "period_code")
            };
        }
    }
}
