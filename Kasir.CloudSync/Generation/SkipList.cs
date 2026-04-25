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
                { "shifts",     "Per-register shift bookkeeping; aggregated metrics live in sales." },
                { "users",      "Local auth state including bcrypt hashes. Must NOT leave the register; auth is per-register only." },

                // Child rows of Phase B mappings; covered transitively when their
                // parent (sales/purchases/etc.) is shipped via Mappers
                // (sale_items already registered as a TableMapping). Listed here
                // for tables we explicitly chose not to mirror separately.
                { "purchase_items",  "Child rows of purchases. Will be added to mirror when needed; not in SyncedTables (no trigger)." },

                // FTS5 virtual tables — SQLite-only feature; cloud search uses
                // pg_trgm instead (see Sql/products.sql).
                { "products_fts",        "FTS5 virtual; replaced by pg_trgm + GIN index on products.search_text in cloud." },
                { "products_fts_data",   "FTS5 internal." },
                { "products_fts_idx",    "FTS5 internal." },
                { "products_fts_docsize","FTS5 internal." },
                { "products_fts_config", "FTS5 internal." },
                { "products_fts_content","FTS5 internal." }
            };
    }
}
