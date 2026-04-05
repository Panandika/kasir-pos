using System.Collections.Generic;

namespace Kasir.Sync
{
    public static class SyncConfig
    {
        public const int MaxBatchSize = 100;
        public const int MaxFileSizeBytes = 1024 * 1024; // 1MB
        public const int MaxInboxFiles = 50;
        public const int SchemaVersion = 2;

        public static readonly HashSet<string> SyncedTables = new HashSet<string>
        {
            "products",
            "product_barcodes",
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
