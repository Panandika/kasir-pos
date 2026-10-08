using Microsoft.Data.Sqlite;

namespace Kasir.Data.Repositories
{
    // PR-K3: one row per inactive product per day it was scanned at the till.
    public class InactiveSaleLogRepository
    {
        private readonly SqliteConnection _db;

        public InactiveSaleLogRepository(SqliteConnection db)
        {
            _db = db;
        }

        // UNIQUE(product_code, sale_date) + OR IGNORE: repeat scans the same day are no-ops
        // (and, inserting nothing, queue nothing for sync).
        public void LogOnce(string productCode, string saleDate, string registerId)
        {
            SqlHelper.ExecuteNonQuery(_db,
                @"INSERT OR IGNORE INTO inactive_sale_log (register_id, product_code, sale_date)
                  VALUES (@reg, @code, @date)",
                SqlHelper.Param("@reg", registerId ?? ""),
                SqlHelper.Param("@code", productCode),
                SqlHelper.Param("@date", saleDate));
        }
    }
}
