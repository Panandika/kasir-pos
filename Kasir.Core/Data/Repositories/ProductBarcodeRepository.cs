using System.Collections.Generic;
using Microsoft.Data.Sqlite;
using Kasir.Models;

namespace Kasir.Data.Repositories
{
    public class ProductBarcodeRepository
    {
        private readonly SqliteConnection _db;

        public ProductBarcodeRepository(SqliteConnection db)
        {
            _db = db;
        }

        public ProductBarcode GetByBarcode(string barcode)
        {
            return SqlHelper.QuerySingle(_db,
                "SELECT * FROM product_barcodes WHERE barcode = @barcode",
                MapBarcode,
                SqlHelper.Param("@barcode", barcode));
        }

        public List<ProductBarcode> GetByProductCode(string productCode)
        {
            return SqlHelper.Query(_db,
                "SELECT * FROM product_barcodes WHERE product_code = @code ORDER BY barcode",
                MapBarcode,
                SqlHelper.Param("@code", productCode));
        }

        public int Insert(ProductBarcode barcode)
        {
            SqlHelper.ExecuteNonQuery(_db,
                @"INSERT INTO product_barcodes (product_code, barcode, product_name, qty_per_scan, price_override, customer_code)
                  VALUES (@code, @barcode, @name, @qty, @price, @customer)",
                SqlHelper.Param("@code", barcode.ProductCode),
                SqlHelper.Param("@barcode", barcode.Barcode),
                SqlHelper.Param("@name", barcode.ProductName),
                SqlHelper.Param("@qty", barcode.QtyPerScan),
                SqlHelper.Param("@price", barcode.PriceOverride),
                SqlHelper.Param("@customer", barcode.CustomerCode));

            return (int)SqlHelper.LastInsertRowId(_db);
        }

        public void Delete(int id)
        {
            SqlHelper.ExecuteNonQuery(_db,
                "DELETE FROM product_barcodes WHERE id = @id",
                SqlHelper.Param("@id", id));
        }

        private static ProductBarcode MapBarcode(SqliteDataReader reader)
        {
            return new ProductBarcode
            {
                Id = SqlHelper.GetInt(reader, "id"),
                ProductCode = SqlHelper.GetString(reader, "product_code"),
                Barcode = SqlHelper.GetString(reader, "barcode"),
                ProductName = SqlHelper.GetString(reader, "product_name"),
                QtyPerScan = SqlHelper.GetInt(reader, "qty_per_scan"),
                PriceOverride = SqlHelper.GetLong(reader, "price_override"),
                CustomerCode = SqlHelper.GetString(reader, "customer_code")
            };
        }
    }
}
