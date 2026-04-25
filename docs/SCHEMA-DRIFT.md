# Schema Drift CI Guard

`scripts/check-schema-drift.sh` (run on every PR by `.github/workflows/schema-drift.yml`)
compares the column lists of mirror tables between:

- `Kasir.Core/Data/Schema.sql` (the SQLite source of truth)
- `Kasir.CloudSync/Sql/{table}.sql` (the Postgres mirror DDL)

If a column appears in one but not the other, the check fails.

## Why this exists

Phase 6 cloud sync mirrors 17 SQLite tables to Supabase Postgres via
runtime metadata (`Kasir.CloudSync/Generation/TableMappings.cs`). When a
SQLite schema change ships without a matching update to the mirror DDL +
TableMapping, mirrored rows silently lose data — the column simply isn't
copied. The drift CI is the trip-wire that catches the omission before
merge.

## How to fix a drift failure

The script prints the diff per table:

```
DRIFT: products
  Columns in SQLite (Schema.sql) but missing from Sql/products.sql:
    - new_field
```

Two options:

1. **Mirror the new column** — Add it to:
   - `Kasir.CloudSync/Sql/products.sql` (Postgres DDL)
   - `Kasir.CloudSync/Generation/TableMappings.cs` (runtime mapping)
2. **Decide it should not mirror** — Add to the per-table exclusion
   allow-list in `scripts/check-schema-drift.sh`
   (`EXCLUDED_PER_TABLE_<table>`) with a one-line code comment explaining
   why (e.g. legacy column scheduled for removal).

## Allow-list semantics

`id` (the SQLite rowid `INTEGER PRIMARY KEY`) is excluded for
`products`, `departments`, and `subsidiaries` because those tables use
their natural business key (`product_code`, `dept_code`, `sub_code`)
as the Postgres primary key. The SQLite rowid is a per-register
artifact and would conflict across registers.

Tables where `id` IS preserved as the Postgres PK (e.g. `sale_items`,
`stock_movements`) keep their `id` column visible to the diff and
therefore must declare it in both files.

## Running locally

```bash
bash scripts/check-schema-drift.sh
```

The script writes per-table OK/DRIFT lines to stdout and a unified
column diff to stderr. Exit 0 = aligned, exit 1 = drift, exit 2 =
tooling error.
