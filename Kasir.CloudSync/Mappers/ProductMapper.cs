using System.Collections.Generic;
using Microsoft.Data.Sqlite;
using Kasir.CloudSync.Models;

namespace Kasir.CloudSync.Mappers
{
    // Maps a single SQLite products row to a cloud Product DTO. Type conversions:
    //   - INTEGER money -> long (BIGINT in Postgres; Rp 21.5M = 2.15e9 > INT32)
    //   - INTEGER qty fields -> long (same reason; exceeds INT32 for big counts)
    //   - TEXT flags (status 'A'/'I'/'D', is_consignment 'N'/'Y') kept as TEXT —
    //     source schema stores them as TEXT, not 0/1
    //   - TEXT ISO date -> DateTimeOffset via DateParser allow-list
    // NULL handling: GetString returns null, GetInt64 returns 0 (kasir.db
    // schema gives INTEGER columns a DEFAULT 0 so NULL shouldn't occur in
    // practice; the mapper is defensive anyway).
    public static class ProductMapper
    {
        public static Product FromReader(SqliteDataReader r, out List<string> warnings)
        {
            warnings = new List<string>();
            var p = new Product
            {
                ProductCode = GetString(r, "product_code"),
                Name = GetString(r, "name"),
                Barcode = GetString(r, "barcode"),
                DeptCode = GetString(r, "dept_code"),
                AccountCode = GetString(r, "account_code"),
                CategoryCode = GetString(r, "category_code"),
                Unit = GetString(r, "unit"),
                Unit1 = GetString(r, "unit1"),
                Unit2 = GetString(r, "unit2"),
                Status = GetString(r, "status"),
                VendorCode = GetString(r, "vendor_code"),
                Location = GetString(r, "location"),
                IsConsignment = GetString(r, "is_consignment"),
                OpenPrice = GetString(r, "open_price"),
                Price = GetLong(r, "price"),
                Price1 = GetLong(r, "price1"),
                Price2 = GetLong(r, "price2"),
                Price3 = GetLong(r, "price3"),
                Price4 = GetLong(r, "price4"),
                BuyingPrice = GetLong(r, "buying_price"),
                QtyMin = GetLong(r, "qty_min"),
                QtyMax = GetLong(r, "qty_max"),
                QtyOrder = GetLong(r, "qty_order"),
                Factor = GetLong(r, "factor"),
                Conversion1 = GetLong(r, "conversion1"),
                Conversion2 = GetLong(r, "conversion2"),
                QtyBreak2 = GetLong(r, "qty_break2"),
                QtyBreak3 = GetLong(r, "qty_break3")
            };

            int changedByOrdinal = FindOrdinal(r, "changed_by");
            if (changedByOrdinal >= 0 && !r.IsDBNull(changedByOrdinal))
            {
                p.ChangedBy = r.GetInt32(changedByOrdinal);
            }

            string changedAtRaw = GetString(r, "changed_at");
            p.ChangedAt = DateParser.TryParseIso(changedAtRaw, out bool dateWarning);
            if (dateWarning)
            {
                warnings.Add($"changed_at unparseable for product_code={p.ProductCode}: {changedAtRaw}");
            }

            return p;
        }

        public static Product FromDictionary(IDictionary<string, object> data, out List<string> warnings)
        {
            warnings = new List<string>();
            var p = new Product
            {
                ProductCode = DictString(data, "product_code"),
                Name = DictString(data, "name"),
                Barcode = DictString(data, "barcode"),
                DeptCode = DictString(data, "dept_code"),
                AccountCode = DictString(data, "account_code"),
                CategoryCode = DictString(data, "category_code"),
                Unit = DictString(data, "unit"),
                Unit1 = DictString(data, "unit1"),
                Unit2 = DictString(data, "unit2"),
                Status = DictString(data, "status"),
                VendorCode = DictString(data, "vendor_code"),
                Location = DictString(data, "location"),
                IsConsignment = DictString(data, "is_consignment"),
                OpenPrice = DictString(data, "open_price"),
                Price = DictLong(data, "price"),
                Price1 = DictLong(data, "price1"),
                Price2 = DictLong(data, "price2"),
                Price3 = DictLong(data, "price3"),
                Price4 = DictLong(data, "price4"),
                BuyingPrice = DictLong(data, "buying_price"),
                QtyMin = DictLong(data, "qty_min"),
                QtyMax = DictLong(data, "qty_max"),
                QtyOrder = DictLong(data, "qty_order"),
                Factor = DictLong(data, "factor"),
                Conversion1 = DictLong(data, "conversion1"),
                Conversion2 = DictLong(data, "conversion2"),
                QtyBreak2 = DictLong(data, "qty_break2"),
                QtyBreak3 = DictLong(data, "qty_break3")
            };

            if (data.TryGetValue("changed_by", out var cb) && cb != null && !(cb is System.DBNull))
            {
                p.ChangedBy = System.Convert.ToInt32(cb);
            }

            string changedAtRaw = DictString(data, "changed_at");
            p.ChangedAt = DateParser.TryParseIso(changedAtRaw, out bool dateWarning);
            if (dateWarning)
            {
                warnings.Add($"changed_at unparseable for product_code={p.ProductCode}: {changedAtRaw}");
            }

            return p;
        }

        private static int FindOrdinal(SqliteDataReader r, string column)
        {
            for (int i = 0; i < r.FieldCount; i++)
            {
                if (string.Equals(r.GetName(i), column, System.StringComparison.OrdinalIgnoreCase))
                    return i;
            }
            return -1;
        }

        private static string GetString(SqliteDataReader r, string column)
        {
            int o = FindOrdinal(r, column);
            if (o < 0) return null;
            return r.IsDBNull(o) ? null : r.GetString(o);
        }

        private static long GetLong(SqliteDataReader r, string column)
        {
            int o = FindOrdinal(r, column);
            if (o < 0) return 0L;
            return r.IsDBNull(o) ? 0L : r.GetInt64(o);
        }

        private static string DictString(IDictionary<string, object> d, string key)
        {
            if (!d.TryGetValue(key, out var v) || v == null || v is System.DBNull) return null;
            return v.ToString();
        }

        private static long DictLong(IDictionary<string, object> d, string key)
        {
            if (!d.TryGetValue(key, out var v) || v == null || v is System.DBNull) return 0L;
            return System.Convert.ToInt64(v);
        }
    }
}
