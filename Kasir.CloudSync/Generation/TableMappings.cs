using System.Collections.Generic;

namespace Kasir.CloudSync.Generation
{
    // Registry of every TableMapping shipped to Supabase. Adding a new table
    // = adding one entry here + one Sql/{table}.sql DDL + (if applicable)
    // SyncedTables in Kasir.Core.SyncConfig.
    //
    // Per-column kind selection follows the schema-drift map in
    // plans/cloud-sync-and-local-api.md section 3:
    //   INTEGER money (×100 cents) -> BigintMoney
    //   INTEGER quantities (×100)  -> BigintQty
    //   INTEGER booleans/flags     -> Int (kept as 0/1; Postgres column is INTEGER)
    //   INTEGER ids/codes/percents -> Int
    //   TEXT (codes, names, ISO dates) -> Text or TimestampTz
    public static class TableMappings
    {
        private static ColumnMapping Pk(string n) => new ColumnMapping(n, ColumnKind.Text, isPrimaryKey: true);
        private static ColumnMapping IntPk(string n) => new ColumnMapping(n, ColumnKind.Int, isPrimaryKey: true);
        private static ColumnMapping T(string n) => new ColumnMapping(n, ColumnKind.Text);
        private static ColumnMapping M(string n) => new ColumnMapping(n, ColumnKind.BigintMoney);
        private static ColumnMapping Q(string n) => new ColumnMapping(n, ColumnKind.BigintQty);
        private static ColumnMapping I(string n) => new ColumnMapping(n, ColumnKind.Int);
        private static ColumnMapping Ts(string n) => new ColumnMapping(n, ColumnKind.TimestampTz);

        public static readonly TableMapping Products = new TableMapping(
            "products",
            new[]
            {
                Pk("product_code"),
                T("name"), T("barcode"), T("dept_code"), T("account_code"),
                T("category_code"), T("type_sub"), T("product_type"),
                T("unit"), T("unit1"), T("unit2"),
                T("status"), T("vendor_code"), T("alt_vendor"), T("location"), T("shelf_location"),
                T("is_consignment"), T("open_price"),
                T("luxury_tax_flag"), T("vat_flag"),
                M("price"), M("price1"), M("price2"), M("price3"),
                M("price4"), M("buying_price"),
                M("cost_price"), M("lowest_cost"), M("profit"),
                I("disc_pct"), I("margin_pct"),
                Q("qty_min"), Q("qty_max"), Q("qty_order"),
                Q("factor"), Q("conversion1"), Q("conversion2"),
                Q("qty_break2"), Q("qty_break3"),
                Ts("changed_at"), I("changed_by")
            });

        public static readonly TableMapping Departments = new TableMapping(
            "departments",
            new[]
            {
                Pk("dept_code"),
                T("name"),
                I("changed_by"),
                Ts("changed_at")
            });

        public static readonly TableMapping Subsidiaries = new TableMapping(
            "subsidiaries",
            new[]
            {
                Pk("sub_code"),
                T("name"), T("account_code"), T("contact_person"),
                M("credit_limit"),
                T("tax_name"), T("tax_addr1"), T("tax_addr2"),
                T("address"), T("city"), T("country"), T("npwp"),
                T("remark"), T("remark2"), T("remark3"),
                M("max_value"), I("commission_pct"),
                M("last_balance"), M("total_in"), M("total_out"),
                T("accum_account"), T("group_code"), T("disc_account"),
                T("phone"), T("fax"), T("giro_account"),
                I("disc_pct"), T("cash_account"), T("alt_account"),
                T("status"),
                M("discount1"), I("disc1_pct"), I("disc2_pct"),
                T("bank_name"), T("bank_holder"), T("bank_account_no"), T("bank_branch"),
                Ts("changed_at"), I("changed_by")
            });

        public static readonly TableMapping Sales = new TableMapping(
            "sales",
            new[]
            {
                new ColumnMapping("journal_no", ColumnKind.Text, isPrimaryKey: true),
                I("id"),
                T("doc_type"), T("doc_date"),
                T("account_code"), T("sub_code"), T("member_code"),
                M("point_value"),
                T("card_code"), T("group1"), T("ap_journal"),
                T("tax_invoice"), Ts("tax_inv_date"),
                T("ref_no"), T("remark"), T("sales_code"),
                Ts("due_date"), T("cashier"),
                I("disc_pct"), I("disc2_pct"),
                T("warehouse"), T("shift"),
                M("payment_amount"), T("vat_flag"),
                M("cash_amount"), M("non_cash"), M("total_value"),
                M("vat_amount"), M("change_amount"), M("total_disc"),
                T("card_type"),
                M("gross_amount"), M("voucher_amount"), M("credit_amount"),
                T("is_posted"), T("is_paid"),
                T("group_code"), T("cc_account"), T("cc_number"),
                T("ref2"), T("alt_sub"),
                I("commission_pct"), I("control"),
                I("print_count"), I("is_printed"),
                I("approved_by"),
                T("period_code"), T("register_id"), T("legacy_source"),
                I("changed_by"), Ts("changed_at")
            });

        public static readonly TableMapping SaleItems = new TableMapping(
            "sale_items",
            new[]
            {
                IntPk("id"),
                T("journal_no"), T("order_ref"),
                T("account_code"), T("sub_code"), T("product_code"),
                T("remark"),
                Q("quantity"), Q("qty_box"),
                M("value"), M("cogs"),
                T("group_code"),
                I("disc_pct"),
                M("unit_price"), M("inv_price"),
                M("point_value"),
                Q("qty_order"),
                M("disc_value")
            });

        public static readonly TableMapping StockMovements = new TableMapping(
            "stock_movements",
            new[]
            {
                IntPk("id"),
                T("product_code"), T("vendor_code"), T("dept_code"),
                T("location_code"), T("account_code"), T("sub_code"),
                T("journal_no"), T("movement_type"),
                T("doc_date"), T("period_code"),
                Q("qty_in"), Q("qty_out"),
                M("val_in"), M("val_out"),
                M("cost_price"),
                I("is_posted"), I("is_archived"),
                I("changed_by"), Ts("changed_at"),
                Ts("created_at")
            });

        public static readonly TableMapping ProductBarcodes = new TableMapping(
            "product_barcodes",
            new[]
            {
                new ColumnMapping("barcode", ColumnKind.Text, isPrimaryKey: true),
                T("product_code"), T("product_name"),
                Q("qty_per_scan"),
                M("price_override"),
                T("customer_code")
            });

        public static readonly TableMapping Locations = new TableMapping(
            "locations",
            new[]
            {
                Pk("location_code"),
                T("name"), T("remark"),
                I("changed_by"), Ts("changed_at")
            });

        public static readonly TableMapping CreditCards = new TableMapping(
            "credit_cards",
            new[]
            {
                Pk("card_code"),
                T("name"), T("account_code"),
                I("fee_pct"), M("min_value"),
                I("changed_by"), Ts("changed_at")
            });

        public static readonly TableMapping Accounts = new TableMapping(
            "accounts",
            new[]
            {
                Pk("account_code"),
                T("account_name"), T("parent_code"),
                I("is_detail"), I("level"), I("account_group"),
                T("normal_balance"), T("verify_flag"),
                I("changed_by"), Ts("changed_at")
            });

        public static readonly TableMapping Members = new TableMapping(
            "members",
            new[]
            {
                Pk("member_code"),
                T("name"),
                Ts("join_date"), Ts("birthday"),
                T("status"),
                M("opening_balance"),
                T("address"), T("city"),
                T("phone"), T("fax"), T("remark"), T("religion"),
                I("changed_by"), Ts("changed_at")
            });

        public static readonly TableMapping Discounts = new TableMapping(
            "discounts",
            new[]
            {
                IntPk("id"),
                T("product_code"), T("dept_code"), T("sub_code"),
                Ts("date_start"), Ts("date_end"),
                T("time_start"), T("time_end"),
                I("disc_pct"), I("disc1_pct"), I("disc2_pct"), I("disc3_pct"),
                M("disc_amount"),
                M("value"), M("value1"), M("value2"), M("value3"),
                Q("min_qty"), Q("max_qty"),
                M("price_override"),
                T("description"),
                I("priority"), I("is_active"),
                I("changed_by"), Ts("changed_at")
            });

        public static readonly TableMapping DiscountPartners = new TableMapping(
            "discount_partners",
            new[]
            {
                IntPk("id"),
                T("account_code"), T("sub_code"),
                I("disc_pct"),
                I("changed_by"), Ts("changed_at")
            });

        public static readonly TableMapping Purchases = new TableMapping(
            "purchases",
            new[]
            {
                new ColumnMapping("journal_no", ColumnKind.Text, isPrimaryKey: true),
                I("id"),
                T("doc_type"), T("doc_date"),
                T("account_code"), T("sub_code"), T("group1"),
                T("tax_invoice1"), T("tax_invoice"), Ts("tax_inv_date"),
                T("ref_no"), T("remark"),
                T("sales_code"), Ts("due_date"),
                I("disc_pct"), M("disc2"),
                T("warehouse"),
                I("commission_pct"),
                T("vat_flag"),
                M("total_value"), M("vat_amount"),
                T("delivery_note"), T("ref2"), T("alt_sub"),
                M("total_disc"),
                T("expedition"), T("packaging"), T("doc_subtype"),
                M("gross_amount"),
                T("is_posted"), T("is_paid"), T("group_code"),
                I("pph_pct"), M("val1"),
                I("control"), I("print_count"), I("is_printed"),
                I("approved_by"),
                T("period_code"), T("register_id"), T("legacy_source"),
                I("changed_by"), Ts("changed_at")
            });

        public static readonly TableMapping CashTransactions = new TableMapping(
            "cash_transactions",
            new[]
            {
                new ColumnMapping("journal_no", ColumnKind.Text, isPrimaryKey: true),
                I("id"),
                T("doc_type"), T("doc_date"),
                T("sub_code"), T("ref"), T("remark"),
                M("total_value"),
                T("is_posted"),
                T("group_code"), T("description"),
                I("control"), I("print_count"),
                I("approved_by"),
                T("period_code"), T("register_id"), T("legacy_source"),
                I("changed_by"), Ts("changed_at")
            });

        public static readonly TableMapping MemorialJournals = new TableMapping(
            "memorial_journals",
            new[]
            {
                new ColumnMapping("journal_no", ColumnKind.Text, isPrimaryKey: true),
                I("id"),
                T("doc_type"), T("doc_date"),
                T("ref"), T("ref_no"), T("remark"), T("group_code"),
                I("control"), I("print_count"),
                I("approved_by"),
                T("period_code"), T("register_id"), T("legacy_source"),
                I("changed_by"), Ts("changed_at")
            });

        public static readonly TableMapping Orders = new TableMapping(
            "orders",
            new[]
            {
                new ColumnMapping("journal_no", ColumnKind.Text, isPrimaryKey: true),
                I("id"),
                T("doc_type"), T("doc_date"),
                T("account_code"), T("sub_code"), T("group1"),
                T("remark"),
                M("total_value"),
                I("disc_pct"),
                Ts("due_date"),
                T("is_posted"), T("vat_flag"),
                I("luxury_tax_pct"), I("pph_pct"),
                M("val1"),
                T("sales_code"), I("order_seq"),
                I("control"), I("print_count"),
                I("approved_by"),
                T("period_code"), T("register_id"), T("legacy_source"),
                I("changed_by"), Ts("changed_at")
            });

        public static readonly TableMapping StockTransfers = new TableMapping(
            "stock_transfers",
            new[]
            {
                new ColumnMapping("journal_no", ColumnKind.Text, isPrimaryKey: true),
                I("id"),
                T("doc_type"), T("doc_date"),
                T("dest_account"), T("dest_sub"),
                T("src_account"), T("src_sub"),
                T("ref"), T("remark"),
                I("control"), I("print_count"),
                T("period_code"), T("register_id"), T("legacy_source"),
                I("changed_by"), Ts("changed_at")
            });

        public static readonly TableMapping StockAdjustments = new TableMapping(
            "stock_adjustments",
            new[]
            {
                new ColumnMapping("journal_no", ColumnKind.Text, isPrimaryKey: true),
                I("id"),
                T("doc_type"), T("doc_date"),
                T("location_code"), T("remark"),
                M("total_value"),
                T("is_posted"),
                I("control"), I("print_count"),
                I("approved_by"),
                T("period_code"), T("register_id"), T("legacy_source"),
                I("changed_by"), Ts("changed_at")
            });

        public static IReadOnlyDictionary<string, TableMapping> All { get; } =
            new Dictionary<string, TableMapping>
            {
                { "products", Products },
                { "product_barcodes", ProductBarcodes },
                { "departments", Departments },
                { "subsidiaries", Subsidiaries },
                { "members", Members },
                { "discounts", Discounts },
                { "discount_partners", DiscountPartners },
                { "accounts", Accounts },
                { "locations", Locations },
                { "credit_cards", CreditCards },
                { "sales", Sales },
                { "sale_items", SaleItems },
                { "purchases", Purchases },
                { "cash_transactions", CashTransactions },
                { "memorial_journals", MemorialJournals },
                { "orders", Orders },
                { "stock_transfers", StockTransfers },
                { "stock_adjustments", StockAdjustments },
                { "stock_movements", StockMovements }
            };

        public static TableMapping Get(string tableName)
        {
            return All.TryGetValue(tableName, out var m) ? m : null;
        }
    }
}
