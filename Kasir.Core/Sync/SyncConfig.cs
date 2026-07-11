using System.Collections.Generic;

namespace Kasir.Sync
{
    public static class SyncConfig
    {
        public const int MaxBatchSize = 100;
        public const int MaxFileSizeBytes = 1024 * 1024; // 1MB
        public const int MaxInboxFiles = 50;
        public const int SchemaVersion = 2;

        // Max transport retries before a queue row is parked as terminal 'dead'.
        // Below the cap, failed rows are retried by the next Push (F05/F14).
        public const int MaxRetries = 5;

        public static readonly HashSet<string> SyncedTables = new HashSet<string>
        {
            "products",
            "departments",
            "subsidiaries",
            "members",
            "discounts",
            "discount_partners",
            "accounts",
            "locations",
            "credit_cards",
            "sales",
            "purchases",
            "cash_transactions",
            "memorial_journals",
            "orders",
            "stock_transfers",
            "stock_adjustments"
        };

        // Parent transaction table -> its child detail table (linked by journal_no). The
        // child rows are bundled into the parent's sync event so they replicate together
        // (F25). The child table names double as the pull-side whitelist.
        public static readonly Dictionary<string, string> ChildTables = new Dictionary<string, string>
        {
            { "sales", "sale_items" },
            { "purchases", "purchase_items" },
            { "orders", "order_items" },
            { "stock_adjustments", "stock_adjustment_items" },
            { "memorial_journals", "memorial_journal_lines" }
        };

        public static string GetOutboxPath(string hubSharePath)
        {
            return System.IO.Path.Combine(hubSharePath, "outbox");
        }

        public static string GetArchivePath(string hubSharePath)
        {
            return System.IO.Path.Combine(hubSharePath, "archive");
        }

        public static string GetAckPath(string hubSharePath)
        {
            return System.IO.Path.Combine(hubSharePath, "ack");
        }
    }
}
