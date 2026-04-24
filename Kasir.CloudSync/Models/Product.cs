using System;

namespace Kasir.CloudSync.Models
{
    // DTO mirroring the subset of kasir-pos/Kasir.Core/Data/Schema.sql products
    // table columns that are shipped to Supabase. Types match the Postgres DDL
    // in Kasir.CloudSync/Sql/products.sql:
    //   - INTEGER money × 100 (cents) -> BIGINT
    //   - INTEGER boolean-ish -> kept as TEXT ('A'/'I'/'D' status and 'N'/'Y' flags)
    //     because the source schema stores them as TEXT, not INTEGER
    //   - TEXT ISO date -> DateTimeOffset? (UTC)
    //   - TEXT PK -> TEXT (product_code)
    public class Product
    {
        public string ProductCode { get; set; }
        public string Name { get; set; }
        public string Barcode { get; set; }
        public string DeptCode { get; set; }
        public string AccountCode { get; set; }
        public string CategoryCode { get; set; }
        public string Unit { get; set; }
        public string Unit1 { get; set; }
        public string Unit2 { get; set; }
        public string Status { get; set; }
        public string VendorCode { get; set; }
        public string Location { get; set; }
        public string IsConsignment { get; set; }
        public string OpenPrice { get; set; }

        public long Price { get; set; }
        public long Price1 { get; set; }
        public long Price2 { get; set; }
        public long Price3 { get; set; }
        public long Price4 { get; set; }
        public long BuyingPrice { get; set; }

        public long QtyMin { get; set; }
        public long QtyMax { get; set; }
        public long QtyOrder { get; set; }
        public long Factor { get; set; }
        public long Conversion1 { get; set; }
        public long Conversion2 { get; set; }
        public long QtyBreak2 { get; set; }
        public long QtyBreak3 { get; set; }

        public DateTimeOffset? ChangedAt { get; set; }
        public int? ChangedBy { get; set; }
    }
}
