# Initial Load — Phase C US-C2

One-shot bulk loader that ships every row of every registered TableMapping
from the local SQLite to Supabase Postgres. Run **once** when first
provisioning the cloud mirror, before steady-state outbox sync starts.

## Pre-requisites

- Phase A Gate A0.1 passed (TLS to Supabase confirmed)
- Phase A Gate A0.3 applied to the source `kasir.db` (sync_queue has
  cloud_synced columns; not strictly required for the loader, but the
  steady-state worker will not start without it)
- Supabase project provisioned in Singapore region with empty target tables
- All `Kasir.CloudSync/Sql/*.sql` DDL files applied to Supabase

## Apply the DDL once

```bash
# All DDL in dependency-safe order (parent tables first):
for t in departments accounts locations credit_cards subsidiaries members \
         products product_barcodes discounts discount_partners \
         purchases sales sale_items \
         cash_transactions memorial_journals orders \
         stock_transfers stock_adjustments stock_movements; do
    psql "$KASIR_CLOUDSYNC_SUPABASE" -f "Kasir.CloudSync/Sql/$t.sql"
done
```

## Run the loader

```bash
export KASIR_CLOUDSYNC_SUPABASE="Host=db.PROJECT.supabase.co;Port=5432;Database=postgres;Username=postgres;Password=...;SslMode=Require"
export KASIR_CLOUDSYNC_DBPATH="/path/to/kasir.db"

cd kasir-pos
dotnet run --project Kasir.CloudSync -- --initial-load
```

Exit codes:
- `0` — every table loaded with row-count parity
- `1` — at least one parity mismatch (see logs)
- `64` — missing `KASIR_CLOUDSYNC_SUPABASE` or `KASIR_CLOUDSYNC_DBPATH`

## What the loader does

1. **`SET session_replication_role = replica`** — disables FK enforcement
   on the Postgres session so child rows can land before parents on the
   wire.
2. **For each table in `InitialLoader.LoadOrder`:**
   - Read all rows from local SQLite via `RowMapper.FromReader` (applies
     the same type conversions as the steady-state worker)
   - `TRUNCATE {table} CASCADE` on Postgres (idempotent re-runs)
   - Ship in batches of 1,000 via parameterised `INSERT ... ON CONFLICT
     DO UPDATE` (same `UpsertSqlBuilder` used by `GenericSink`)
   - Record SQLite row count vs Postgres row count
3. **`SET session_replication_role = origin`** — re-enables FK
   enforcement.
4. **Parity report** — log per-table SQLite vs Postgres row counts; exit
   1 if any mismatch.

## Scale notes

- Source `kasir.db` is ~55 MB / 343K rows total
- Loader keeps all rows for one table in memory before the upsert phase;
  the largest table (`stock_movements`, ~334K rows) fits comfortably
- Expected wall-clock: 5–15 minutes against a free-tier Supabase project
  on a normal home internet connection
- For a 5–10x speed-up, swap the parameterised UPSERT for `NpgsqlBinaryImporter`
  (COPY) — defer until proven necessary

## After the load

Verify the parity report shows 0 mismatches, then start the steady-state
worker. The first tick will pick up any rows that were modified between
load start and worker start (cloud_synced=0 from the Gate A0.3 migration)
and ship them via the normal outbox path.

## Re-running the loader

The loader truncates each table before loading, so re-runs are safe but
will lose any rows that were upserted by the steady-state worker between
loads. **Do not re-run the loader once the steady-state worker has
started.**
