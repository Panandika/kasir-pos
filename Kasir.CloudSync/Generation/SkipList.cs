using System.Collections.Generic;

namespace Kasir.CloudSync.Generation
{
    // Tables in Kasir.Core/Data/Schema.sql that are intentionally NOT mirrored
    // to Supabase. Outside the SyncedTables set in Kasir.Core.Sync.SyncConfig,
    // any table appearing here is a deliberate exclusion with a documented
    // rationale rather than an oversight.
    public static class SkipList
    {
        public static readonly IReadOnlyDictionary<string, string> Excluded =
            new Dictionary<string, string>
            {
                // Bookkeeping for the sync mechanism itself; mirroring would create
                // an infinite recursion of mirroring-state-about-mirroring.
                { "sync_queue", "Sync mechanism state; mirrored only via cloud_synced bookkeeping inside it." },
                { "sync_log",   "Local audit trail of sync operations. Aggregate metrics are exposed via the health endpoint instead." },

                // Per-register operational tables that have no cross-register meaning.
                { "config",     "Local register configuration (HMAC keys, register ID). MUST stay local." },
                { "counters",   "Per-register monotonic doc-number counters. Global uniqueness comes from the register-prefixed format already." },
                { "audit_log",  "Local audit log; large + per-register; not useful in the cloud aggregate." },
                { "users",      "Local auth state including bcrypt hashes. Must NOT leave the register; auth is per-register only." },

                // FTS5 virtual tables — SQLite-only feature; cloud search uses
                // pg_trgm instead (see Sql/products.sql).
                { "products_fts",        "FTS5 virtual; replaced by pg_trgm + GIN index on products.search_text in cloud." },
                { "products_fts_data",   "FTS5 internal." },
                { "products_fts_idx",    "FTS5 internal." },
                { "products_fts_docsize","FTS5 internal." },
                { "products_fts_config", "FTS5 internal." },
                { "products_fts_content","FTS5 internal." }
            };

        // Columns of mirrored tables that are deliberately NOT mapped, keyed
        // "table.column". Every other POS column of a mapped table must be in its
        // TableMapping (enforced by MirrorCoverageTests).
        public static readonly IReadOnlyDictionary<string, string> ExcludedColumns =
            new Dictionary<string, string>
            {
                // Natural-key tables: the cloud PK is the business code; the SQLite
                // rowid is register-local and means nothing in the aggregate.
                { "accounts.id",      "Local rowid; cloud keys on account_code." },
                { "credit_cards.id",  "Local rowid; cloud keys on card_code." },
                { "departments.id",   "Local rowid; cloud keys on dept_code." },
                { "inactive_sale_log.id", "Local rowid; cloud keys on (register_id, product_code, sale_date)." },
                { "locations.id",     "Local rowid; cloud keys on location_code." },
                { "members.id",       "Local rowid; cloud keys on member_code." },
                { "products.id",      "Local rowid; cloud keys on product_code." },
                { "subsidiaries.id",  "Local rowid; cloud keys on sub_code." },

                // Legacy per-line fields the POS never writes (PurchaseRepository) and the
                // legacy DBF sync never fills; the purchases header carries the codes.
                { "purchase_items.account_code",  "Legacy per-line GL code; header purchases.account_code is used." },
                { "purchase_items.sub_code",      "Legacy per-line vendor code; header purchases.sub_code is used." },
                { "purchase_items.group_code",    "Legacy per-line group code; header purchases.group_code is used." },
                { "purchase_items.customer_code", "INVCUST customer identifier on a purchase line; never written, customer-identifying, not needed for purchasing reports." },
                { "purchase_items.qty1",          "Legacy MSD-only split quantity; never written." },
                { "purchase_items.qty2",          "Legacy MSD-only split quantity; never written." },
                { "purchase_items.roll",          "Legacy RMD/BPD roll count; never written." }
            };
    }
}
