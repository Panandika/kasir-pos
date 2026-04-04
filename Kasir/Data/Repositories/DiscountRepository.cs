using System.Collections.Generic;
using System.Data.SQLite;
using Kasir.Models;

namespace Kasir.Data.Repositories
{
    public class DiscountRepository
    {
        private readonly SQLiteConnection _db;

        public DiscountRepository(SQLiteConnection db)
        {
            _db = db;
        }

        public List<Discount> GetActiveForProduct(string productCode, string deptCode, string dateIso)
        {
            return SqlHelper.Query(_db,
                @"SELECT * FROM discounts
                  WHERE is_active = 1
                  AND (product_code = @product OR dept_code = @dept OR (product_code = '' AND dept_code = ''))
                  AND (date_start IS NULL OR date_start = '' OR date_start <= @date)
                  AND (date_end IS NULL OR date_end = '' OR date_end >= @date)
                  ORDER BY priority DESC",
                MapDiscount,
                SqlHelper.Param("@product", productCode ?? ""),
                SqlHelper.Param("@dept", deptCode ?? ""),
                SqlHelper.Param("@date", dateIso));
        }

        public List<Discount> GetAll()
        {
            return SqlHelper.Query(_db,
                "SELECT * FROM discounts ORDER BY product_code, priority DESC",
                MapDiscount);
        }

        private static Discount MapDiscount(SQLiteDataReader reader)
        {
            return new Discount
            {
                Id = SqlHelper.GetInt(reader, "id"),
                ProductCode = SqlHelper.GetString(reader, "product_code"),
                DeptCode = SqlHelper.GetString(reader, "dept_code"),
                SubCode = SqlHelper.GetString(reader, "sub_code"),
                DiscPct = SqlHelper.GetInt(reader, "disc_pct"),
                Disc2Pct = SqlHelper.GetInt(reader, "disc2_pct"),
                DateStart = SqlHelper.GetString(reader, "date_start"),
                DateEnd = SqlHelper.GetString(reader, "date_end"),
                MinQty = SqlHelper.GetInt(reader, "min_qty"),
                MaxQty = SqlHelper.GetInt(reader, "max_qty"),
                PriceOverride = SqlHelper.GetInt(reader, "price_override"),
                Description = SqlHelper.GetString(reader, "description"),
                Priority = SqlHelper.GetInt(reader, "priority"),
                IsActive = SqlHelper.GetInt(reader, "is_active")
            };
        }
    }
}
