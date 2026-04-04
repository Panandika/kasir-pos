namespace Kasir.Migration
{
    public static class FieldMappings
    {
        // Migration order (respects FK dependencies)
        public static readonly string[] MigrationOrder = new[]
        {
            "roles", "users", "fiscal_periods", "accounts", "account_balances",
            "account_config", "departments", "subsidiaries", "products",
            "product_barcodes", "members", "discounts", "credit_cards",
            "locations", "counters", "config",
            // Transactions
            "sales", "sale_items", "purchases", "purchase_items",
            "orders", "order_items", "payables_register",
            "memorial_journals", "memorial_journal_lines",
            "cash_transactions", "cash_transaction_lines",
            "stock_movements", "price_history"
        };

        // DBF source paths (relative to data root)
        public static class DbfPaths
        {
            // Master data
            public const string Roles = "MST\\LEVEL.DBF";
            public const string Users = "MST\\PASS.DBF";
            public const string Departments = "MST\\DEPT.DBF";
            public const string Vendors = "MST\\VENDOR.DBF";
            public const string Products = "MST\\GOODS.DBF";
            public const string Barcodes = "MST\\BCD.DBF";
            public const string Members = "MST\\MEMBER.DBF";
            public const string Discounts = "MST\\DISC.DBF";
            public const string CreditCards = "MST\\CRDIT.DBF";
            public const string Locations = "MST\\KMS.DBF";
            public const string Accounts = "MST\\PERKIRA.DBF";
            public const string Counters = "MST\\COUNTER.DBF";

            // Transaction data
            public const string SaleHeaders = "TRS\\TRHDR.DBF";
            public const string SaleDetails = "TRS\\TRDTL.DBF";
            public const string PurchaseOrders = "TRS\\POHDR.DBF";
            public const string PurchaseOrderDetails = "TRS\\PODTL.DBF";
            public const string Purchases = "TRS\\RECHDR.DBF";
            public const string PurchaseDetails = "TRS\\RECDTL.DBF";
            public const string Payables = "TRS\\HUTANG.DBF";
            public const string StockHistory = "TRS\\GHIST.DBF";
            public const string PriceHistory = "TRS\\GPRICE.DBF";
            public const string Payments = "TRS\\PAYHDR.DBF";
            public const string Banks = "MST\\BANK.DBF";
        }

        // Product field mappings: DBF column → SQLite column
        public static class ProductFields
        {
            public const string Code = "INV";         // → product_code
            public const string Name = "NAME";        // → name
            public const string Dept = "DEPT";        // → dept_code
            public const string Price = "PRICE";      // → price (× 100)
            public const string BuyPrice = "BUY";     // → buying_price (× 100)
            public const string CostPrice = "COST";   // → cost_price (× 100)
            public const string Price2 = "PRICE2";    // → price2 (× 100)
            public const string Price3 = "PRICE3";    // → price3 (× 100)
            public const string Price4 = "PRICE4";    // → price4 (× 100)
            public const string Break2 = "BREAK2";    // → qty_break2
            public const string Break3 = "BREAK3";    // → qty_break3
            public const string Disc = "DISC";        // → disc_pct (× 100)
            public const string VatFlag = "PPN";      // → vat_flag
            public const string Status = "STATUS";    // → status
            public const string Vendor = "SUB";       // → vendor_code
        }

        // Vendor/Subsidiary field mappings
        public static class VendorFields
        {
            public const string Code = "SUB";         // → sub_code
            public const string Name = "NAME";        // → name
            public const string Address = "ADDR";     // → address
            public const string City = "CITY";        // → city
            public const string Phone = "PHONE";      // → phone
            public const string Contact = "CONTACT";  // → contact_person
            public const string CreditLimit = "LIMIT"; // → credit_limit (× 100)
            public const string Npwp = "NPWP";        // → npwp
        }

        // Payables field mappings
        public static class PayablesFields
        {
            public const string JournalNo = "NOJNL";   // → journal_no
            public const string DocDate = "DATE";       // → doc_date
            public const string AccountCode = "ACC";    // → account_code
            public const string SubCode = "SUB";        // → sub_code
            public const string Value = "VAL";          // → value (× 100)
            public const string DueDate = "DUEDATE";    // → due_date
            public const string Direction = "DK";       // → direction
            public const string Payment = "PAYMENT";    // → payment_amount (× 100)
            public const string IsPaid = "BAYAR";       // → is_paid
            public const string GrossAmount = "BRUTO";  // → gross_amount (× 100)
        }

        // Account field mappings
        public static class AccountFields
        {
            public const string Code = "KDAC";          // → account_code
            public const string Name = "NMAC";          // → account_name
            public const string Parent = "INDUK";       // → parent_code
            public const string IsDetail = "DETIL";     // → is_detail
            public const string Level = "LEVEL";        // → level
            public const string Group = "GROUP";        // → account_group
            public const string NormalBal = "DK";       // → normal_balance
        }

        // Expected record counts for validation
        public static class ExpectedCounts
        {
            public const int Products = 24457;
            public const int Vendors = 754;
            public const int Departments = 194;
            public const int Users = 9;
            public const int Payables = 80767;
            public const int StockHistory = 334570;
            public const int PriceHistory = 76216;
        }
    }
}
