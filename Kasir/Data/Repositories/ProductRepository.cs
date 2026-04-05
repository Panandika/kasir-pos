using System.Collections.Generic;
using System.Data.SQLite;
using Kasir.Models;

namespace Kasir.Data.Repositories
{
    public class ProductRepository
    {
        private readonly SQLiteConnection _db;

        public ProductRepository(SQLiteConnection db)
        {
            _db = db;
        }

        public Product GetByCode(string productCode)
        {
            return SqlHelper.QuerySingle(_db,
                "SELECT * FROM products WHERE product_code = @code",
                MapProduct,
                SqlHelper.Param("@code", productCode));
        }

        public Product GetByBarcode(string barcode)
        {
            return SqlHelper.QuerySingle(_db,
                "SELECT * FROM products WHERE barcode = @barcode",
                MapProduct,
                SqlHelper.Param("@barcode", barcode));
        }

        public Product GetById(int id)
        {
            return SqlHelper.QuerySingle(_db,
                "SELECT * FROM products WHERE id = @id",
                MapProduct,
                SqlHelper.Param("@id", id));
        }

        public List<Product> GetAll(int limit, int offset)
        {
            return SqlHelper.Query(_db,
                "SELECT * FROM products WHERE status = 'A' ORDER BY name LIMIT @limit OFFSET @offset",
                MapProduct,
                SqlHelper.Param("@limit", limit),
                SqlHelper.Param("@offset", offset));
        }

        public List<Product> GetAllActive()
        {
            return SqlHelper.Query(_db,
                "SELECT * FROM products WHERE status = 'A' ORDER BY name",
                MapProduct);
        }

        public List<Product> SearchByCodePrefix(string prefix, int limit)
        {
            if (string.IsNullOrWhiteSpace(prefix))
            {
                return new List<Product>();
            }

            // Exact code match first
            var exact = GetByCode(prefix);
            if (exact != null)
            {
                return new List<Product> { exact };
            }

            // Exact barcode match
            var byBarcode = GetByBarcode(prefix);
            if (byBarcode != null)
            {
                return new List<Product> { byBarcode };
            }

            // Prefix search on product_code and barcode
            string likePrefix = prefix + "%";
            return SqlHelper.Query(_db,
                @"SELECT * FROM products
                  WHERE (product_code LIKE @q OR barcode LIKE @q)
                  AND status = 'A'
                  ORDER BY product_code
                  LIMIT @limit",
                MapProduct,
                SqlHelper.Param("@q", likePrefix),
                SqlHelper.Param("@limit", limit));
        }

        public List<Product> SearchByName(string query, int limit)
        {
            if (string.IsNullOrWhiteSpace(query) || query.Length < 2)
            {
                return new List<Product>();
            }

            // Try FTS5 on name
            try
            {
                string ftsQuery = query.Replace("'", "''").Replace("\"", "\"\"") + "*";
                var ftsResults = SqlHelper.Query(_db,
                    @"SELECT p.* FROM products_fts f
                      JOIN products p ON p.id = f.rowid
                      WHERE products_fts MATCH @q
                      LIMIT @limit",
                    MapProduct,
                    SqlHelper.Param("@q", ftsQuery),
                    SqlHelper.Param("@limit", limit));

                if (ftsResults.Count > 0)
                {
                    return ftsResults;
                }
            }
            catch
            {
                // FTS5 not available — fall through to LIKE
            }

            // LIKE fallback on name only
            string likeQuery = "%" + query + "%";
            return SqlHelper.Query(_db,
                @"SELECT * FROM products
                  WHERE name LIKE @q AND status = 'A'
                  ORDER BY name
                  LIMIT @limit",
                MapProduct,
                SqlHelper.Param("@q", likeQuery),
                SqlHelper.Param("@limit", limit));
        }

        public List<Product> SearchByText(string query, int limit)
        {
            if (string.IsNullOrWhiteSpace(query) || query.Length < 2)
            {
                return new List<Product>();
            }

            // Strategy 1: exact code match
            var exact = GetByCode(query);
            if (exact != null)
            {
                return new List<Product> { exact };
            }

            // Strategy 2: exact barcode match
            var byBarcode = GetByBarcode(query);
            if (byBarcode != null)
            {
                return new List<Product> { byBarcode };
            }

            // Strategy 3: FTS5 search (if available)
            try
            {
                string ftsQuery = query.Replace("'", "''").Replace("\"", "\"\"") + "*";
                var ftsResults = SqlHelper.Query(_db,
                    @"SELECT p.* FROM products_fts f
                      JOIN products p ON p.id = f.rowid
                      WHERE products_fts MATCH @q
                      LIMIT @limit",
                    MapProduct,
                    SqlHelper.Param("@q", ftsQuery),
                    SqlHelper.Param("@limit", limit));

                if (ftsResults.Count > 0)
                {
                    return ftsResults;
                }
            }
            catch
            {
                // FTS5 not available — fall through to LIKE
            }

            // Strategy 4: LIKE fallback
            string likeQuery = "%" + query + "%";
            return SqlHelper.Query(_db,
                @"SELECT * FROM products
                  WHERE (product_code LIKE @q OR barcode LIKE @q OR name LIKE @q)
                  AND status = 'A'
                  ORDER BY name
                  LIMIT @limit",
                MapProduct,
                SqlHelper.Param("@q", likeQuery),
                SqlHelper.Param("@limit", limit));
        }

        public int Insert(Product product)
        {
            SqlHelper.ExecuteNonQuery(_db,
                @"INSERT INTO products (product_code, account_code, dept_code, barcode, name, status,
                  unit, unit1, price, price1, price2, price3, price4, buying_price, vendor_code,
                  qty_break2, qty_break3, open_price, disc_pct, cost_price, vat_flag,
                  luxury_tax_flag, is_consignment, product_type, changed_by, changed_at)
                  VALUES (@code, @acc, @dept, @barcode, @name, @status,
                  @unit, @unit1, @price, @price1, @price2, @price3, @price4, @buying, @vendor,
                  @break2, @break3, @open, @disc, @cost, @vat,
                  @luxury, @consign, @type, @changedBy, datetime('now','localtime'))",
                SqlHelper.Param("@code", product.ProductCode),
                SqlHelper.Param("@acc", product.AccountCode),
                SqlHelper.Param("@dept", product.DeptCode),
                SqlHelper.Param("@barcode", product.Barcode),
                SqlHelper.Param("@name", product.Name),
                SqlHelper.Param("@status", product.Status ?? "A"),
                SqlHelper.Param("@unit", product.Unit),
                SqlHelper.Param("@unit1", product.Unit1),
                SqlHelper.Param("@price", product.Price),
                SqlHelper.Param("@price1", product.Price1),
                SqlHelper.Param("@price2", product.Price2),
                SqlHelper.Param("@price3", product.Price3),
                SqlHelper.Param("@price4", product.Price4),
                SqlHelper.Param("@buying", product.BuyingPrice),
                SqlHelper.Param("@vendor", product.VendorCode),
                SqlHelper.Param("@break2", product.QtyBreak2),
                SqlHelper.Param("@break3", product.QtyBreak3),
                SqlHelper.Param("@open", product.OpenPrice ?? "N"),
                SqlHelper.Param("@disc", product.DiscPct),
                SqlHelper.Param("@cost", product.CostPrice),
                SqlHelper.Param("@vat", product.VatFlag ?? "N"),
                SqlHelper.Param("@luxury", product.LuxuryTaxFlag ?? "N"),
                SqlHelper.Param("@consign", product.IsConsignment ?? "N"),
                SqlHelper.Param("@type", product.ProductType),
                SqlHelper.Param("@changedBy", product.ChangedBy));

            return (int)_db.LastInsertRowId;
        }

        public void Update(Product product)
        {
            SqlHelper.ExecuteNonQuery(_db,
                @"UPDATE products SET name = @name, barcode = @barcode, dept_code = @dept,
                  price = @price, price1 = @price1, price2 = @price2, price3 = @price3, price4 = @price4,
                  buying_price = @buying, vendor_code = @vendor, qty_break2 = @break2, qty_break3 = @break3,
                  open_price = @open, disc_pct = @disc, cost_price = @cost, status = @status,
                  changed_by = @changedBy, changed_at = datetime('now','localtime')
                  WHERE id = @id",
                SqlHelper.Param("@name", product.Name),
                SqlHelper.Param("@barcode", product.Barcode),
                SqlHelper.Param("@dept", product.DeptCode),
                SqlHelper.Param("@price", product.Price),
                SqlHelper.Param("@price1", product.Price1),
                SqlHelper.Param("@price2", product.Price2),
                SqlHelper.Param("@price3", product.Price3),
                SqlHelper.Param("@price4", product.Price4),
                SqlHelper.Param("@buying", product.BuyingPrice),
                SqlHelper.Param("@vendor", product.VendorCode),
                SqlHelper.Param("@break2", product.QtyBreak2),
                SqlHelper.Param("@break3", product.QtyBreak3),
                SqlHelper.Param("@open", product.OpenPrice),
                SqlHelper.Param("@disc", product.DiscPct),
                SqlHelper.Param("@cost", product.CostPrice),
                SqlHelper.Param("@status", product.Status),
                SqlHelper.Param("@changedBy", product.ChangedBy),
                SqlHelper.Param("@id", product.Id));
        }

        public void Deactivate(int id, int changedBy)
        {
            SqlHelper.ExecuteNonQuery(_db,
                @"UPDATE products SET status = 'I', changed_by = @changedBy,
                  changed_at = datetime('now','localtime')
                  WHERE id = @id",
                SqlHelper.Param("@changedBy", changedBy),
                SqlHelper.Param("@id", id));
        }

        private static Product MapProduct(SQLiteDataReader reader)
        {
            return new Product
            {
                Id = SqlHelper.GetInt(reader, "id"),
                ProductCode = SqlHelper.GetString(reader, "product_code"),
                AccountCode = SqlHelper.GetString(reader, "account_code"),
                DeptCode = SqlHelper.GetString(reader, "dept_code"),
                Barcode = SqlHelper.GetString(reader, "barcode"),
                Name = SqlHelper.GetString(reader, "name"),
                Status = SqlHelper.GetString(reader, "status"),
                Unit = SqlHelper.GetString(reader, "unit"),
                Unit1 = SqlHelper.GetString(reader, "unit1"),
                Price = SqlHelper.GetInt(reader, "price"),
                Price1 = SqlHelper.GetInt(reader, "price1"),
                Price2 = SqlHelper.GetInt(reader, "price2"),
                Price3 = SqlHelper.GetInt(reader, "price3"),
                Price4 = SqlHelper.GetInt(reader, "price4"),
                BuyingPrice = SqlHelper.GetInt(reader, "buying_price"),
                VendorCode = SqlHelper.GetString(reader, "vendor_code"),
                QtyBreak2 = SqlHelper.GetInt(reader, "qty_break2"),
                QtyBreak3 = SqlHelper.GetInt(reader, "qty_break3"),
                OpenPrice = SqlHelper.GetString(reader, "open_price"),
                DiscPct = SqlHelper.GetInt(reader, "disc_pct"),
                CostPrice = SqlHelper.GetInt(reader, "cost_price"),
                VatFlag = SqlHelper.GetString(reader, "vat_flag"),
                LuxuryTaxFlag = SqlHelper.GetString(reader, "luxury_tax_flag"),
                IsConsignment = SqlHelper.GetString(reader, "is_consignment"),
                ProductType = SqlHelper.GetString(reader, "product_type"),
                ChangedBy = SqlHelper.GetInt(reader, "changed_by"),
                ChangedAt = SqlHelper.GetString(reader, "changed_at")
            };
        }
    }
}
