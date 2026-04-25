# Capacity Monitoring — Phase E US-E1

Supabase free tier caps the database at **500 MB**. The kasir.db today is
~55 MB; the Postgres mirror with indexes will be ~80–100 MB. So we have
~400 MB of headroom — plenty for steady-state, but worth monitoring so
nobody gets a 4 AM "INSERT failed" surprise.

## What's installed

`Sql/E_capacity_monitoring.sql` (apply once on Supabase) creates:

- `_capacity_summary` view — per-table row count + on-disk size (incl.
  indexes), sorted descending so the largest tables are at the top
- `_capacity_total` view — total DB size + human-readable string
- `_trgm_status` view — confirms `pg_trgm` extension + GIN index on
  `products.search_text` are both present (closes Phase E pg_trgm gate)

## Reading capacity

### From the health endpoint
The `/health` endpoint includes `supabase_db_size_mb` when populated by
the worker. Alert thresholds (per `HealthService.cs`):
- > 400 MB → `WARNING DB_GROWING`
- > 475 MB → `CRITICAL DB_NEAR_CEILING`

### From the C# helper
`CapacityMonitor.SampleAsync()` returns `CapacityReport { TotalBytes,
PerTable[{TableName, RowCount, BytesOnDisk}] }`. Use this in any
ad-hoc tooling that needs a programmatic view.

### From psql directly
```sql
-- Whole DB size:
SELECT * FROM _capacity_total;

-- Top 10 tables by disk:
SELECT * FROM _capacity_summary LIMIT 10;

-- pg_trgm health:
SELECT * FROM _trgm_status;
```

## Projection: when will we hit the ceiling?

`CapacityMonitor.ProjectedDaysUntilCeiling(currentMb, mbPerWeek)` returns
days until 500 MB given the observed weekly growth. The runbook records
weekly samples; first projection should be done after 4 weeks of data.

If the projection drops below 90 days, the runbook escalation (see
`RUNBOOK.md`) kicks in:

1. Confirm growth is real, not a transient spike (one-off batch import).
2. Identify the largest tables via `_capacity_summary`. Likely
   candidates for pruning: `stock_movements` (per-period archival),
   `_sync_health` history if we ever start retaining old snapshots.
3. If pruning isn't enough, upgrade to Supabase Pro at ~$25/mo.

## pg_trgm verification

Phase A US-A2 already shipped the GIN index. Run this monthly:

```sql
SELECT * FROM _trgm_status;
```

Expected:
```
 extension_installed | gin_index_present | gin_index_size
---------------------+-------------------+----------------
 t                   | t                 | 8192 KB
```

If `extension_installed = false`, the products.sql DDL didn't run; apply
it. If the GIN is missing but the extension is present, the
`ALTER TABLE products ADD COLUMN search_text` may have happened before
the index — re-run the relevant block.

## Smoke search

Use the **word-similarity operator `<%>`** rather than the full-string
`%` operator. Long product names (e.g. `NIVEA WHT CAR FACIAL FOAM 10`)
have low full-string similarity to a single search word; word-similarity
matches against any word in the field and is the right operator for
typing-as-you-search.

```sql
-- Word-similarity match: finds 'NIVEA' anywhere in the search_text
SELECT product_code, name,
       word_similarity('NIVEA', search_text) AS ws
FROM products
WHERE 'NIVEA' <% search_text       -- uses idx_products_search_trgm
ORDER BY ws DESC
LIMIT 20;
```

Should return matching products in <50 ms even at 24K rows.

**Verified on real data 2026-04-25:** 5 NIVEA-prefixed products returned
with `word_similarity = 1` against the production-cloned dataset.
