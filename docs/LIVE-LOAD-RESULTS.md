# Live Initial-Load Results — 2026-04-25

Record of the actual cloud-sync provisioning run against the real
Supabase project. Pairs with `NEXT-STEPS.md`.

## Environment

- **Project ref:** `mnatezzsysmadvrosnad`
- **Region:** `ap-southeast-1` (Singapore)
- **Plan:** Free tier (500 MB ceiling)
- **Source:** `kasir-pos/data/kasir.db` (development snapshot — sales
  table is empty in this dataset)

## Connection caveat (resolved)

The free-tier project's **direct connection** (`db.<ref>.supabase.co:5432`)
is **IPv6-only** with no A record. Networks without IPv6 (most home /
small-office connections) cannot reach it.

Use the **Supavisor pooler** instead. The working hostname turned out
to be `aws-1-ap-southeast-1.pooler.supabase.com` (note `aws-1`, NOT
`aws-0` which is the documented prefix in older docs):

| Mode | Port | Use |
|---|---|---|
| Session | 5432 | DDL, `--initial-load` (needs `SET session_replication_role`) |
| Transaction | 6543 | Steady-state CloudSync worker |

The `.env` file in `kasir-pos/` was updated with both URLs.

## What ran

### Step 2 — DDL applied to Supabase ✅

19 mirror tables + capacity views + `_sync_health` table all created
via `psql` against the pooler. `pg_trgm` extension confirmed; GIN
index on `products.search_text` confirmed via `_trgm_status` view.

### Step 6 — `--initial-load` ✅

```bash
dotnet run --project Kasir.CloudSync -c Release -- \
    --initial-load --skip-orphans --skip-constraints
```

Flags chosen:
- `--skip-orphans` — the orphan scanner correctly caught **13,075
  legacy `stock_movements` rows pointing at deleted `products`**. This
  is FoxPro-migration leftover (history rows kept after the parent
  product was retired). Cleaning the source would alter historical
  records, so we skip orphan abort and accept these rows in the mirror
  without their FK enforced.
- `--skip-constraints` — the script itself would have failed at
  `fk_stock_movements_product`. We applied constraints individually
  afterward, leaving the legacy ones off.

**Wall-clock:** ~35 seconds for 89,608 rows.

| Table | Rows |
|---|--:|
| products | 24,560 |
| cash_transactions | 26,407 |
| stock_movements | 25,146 |
| purchases | 12,421 |
| subsidiaries | 754 |
| stock_adjustments | 438 |
| departments | 194 |
| discounts | 85 |
| locations | 2 |
| members | 1 |
| (sales / sale_items / orders / etc.) | 0 |
| **Total** | **89,608** |

All per-table parity checks: **OK**.

### FK constraints — partial apply ✅ (4 of 6)

Applied:
- `fk_product_barcodes_product`
- `fk_sale_items_journal`
- `fk_sale_items_product`
- `fk_orders_sub`

**Deferred (legacy-data cleanup needed):**

| Constraint | Why deferred | Cleanup path |
|---|---|---|
| `fk_purchases_sub` | 1+ purchase rows have empty-string (`''`) `sub_code`. Postgres rejects `''` as not-equal-to NULL; constraint fails. | Either backfill empty `sub_code` to a sentinel "Unknown vendor" row in `subsidiaries`, OR allow NULL on purchases.sub_code and convert empties to NULL during sync. |
| `fk_stock_movements_product` | 13,075 rows reference deleted products (FoxPro history). | Either soft-delete-mark and exclude from FK (add `is_archived` filter), OR leave indefinitely deferred since this is historical immutable data. |

The `OrphanScanner` in `Kasir.CloudSync/DataQuality/` correctly
flagged the `stock_movements` orphans pre-load. It did NOT flag the
`purchases.sub_code = ''` case because the scanner intentionally
treats empty strings as "no FK present" (matches the application's
runtime semantics). Postgres FK enforcement is stricter — a follow-up
could add an empty-string check to the scanner.

### pg_trgm verification ✅

Live test on actual data:

```sql
SELECT product_code, name, word_similarity('NIVEA', search_text) AS ws
FROM products
WHERE 'NIVEA' <% search_text
ORDER BY ws DESC LIMIT 5;
```

Returned 5 products in <50ms with `ws = 1`:
- NIVEA CREAM 100 ML
- NIVEA CREAM 50 ML
- NIVEA CREAM 25 ML
- NIVEA BODY WHITE CREAM 25 ML
- NIVEA WHT CAR FACIAL FOAM 10

**Note:** use `<%>` (word-similarity) and `<%` operator, not the
full-string `%`. Long product names with the search term as one word
have low full-string similarity. Docs (CAPACITY.md, products.sql
comments, E_capacity_monitoring.sql) updated.

### Capacity sample ✅

```
Total DB size: 39 MB / 500 MB free-tier ceiling

Top 5 by size:
  products           14 MB   (24,560 rows; pg_trgm GIN ~ half of this)
  stock_movements   5912 kB  (25,146 rows)
  cash_transactions 4328 kB  (26,407 rows)
  purchases         3616 kB  (12,421 rows)
  subsidiaries      232 kB
```

Plenty of headroom. With the worker running steady-state we'll
generate weekly samples and recompute `ProjectedDaysUntilCeiling`.

## What remains for production cutover

Per `NEXT-STEPS.md`:

- **Step 3** — Run Gate A0.1 TLS smoke test on the actual Win10
  gateway hardware (build verified clean on macOS; running needs the
  target box)
- **Step 4** — Apply `001_sync_queue_recreate.sql` to all 4
  production registers + hub during a coordinated downtime window
- **Step 5** — Install Litestream `v0.5.11` Windows binary on
  gateway + provision Cloudflare R2 bucket
- **Step 7** — `dotnet publish` Kasir.CloudSync, copy to gateway,
  register as Windows service
- **Step 8** — End-to-end smoke (sale on Register 02 → Supabase
  visible in 60s)
- **Step 9** — `security-reviewer` agent review
- **Step 10** — 1-week stability watch before merging PR #15

The Supabase mirror is **already populated and queryable** as of this
load. Anyone with the connection string can run dashboards / reports
against it today; the only thing missing for production is the
steady-state worker shipping new POS activity, which requires the
gateway hardware to host the Windows service.

## Rollback (if you want to start over)

The mirror is non-destructive to local POS. To wipe and reload:

```bash
export PATH="/opt/homebrew/opt/libpq/bin:$PATH"
PGURL="postgresql://postgres.mnatezzsysmadvrosnad:lMwhcS5aXiVr49eb@aws-1-ap-southeast-1.pooler.supabase.com:5432/postgres?sslmode=require"

# Drop everything we created
for t in stock_movements stock_adjustments stock_transfers \
         orders memorial_journals cash_transactions \
         sale_items sales purchases \
         discount_partners discounts \
         product_barcodes products \
         members subsidiaries credit_cards locations accounts departments \
         _sync_health; do
    psql "$PGURL" -c "DROP TABLE IF EXISTS $t CASCADE;"
done
psql "$PGURL" -c "DROP VIEW IF EXISTS _capacity_summary CASCADE;"
psql "$PGURL" -c "DROP VIEW IF EXISTS _capacity_total CASCADE;"
psql "$PGURL" -c "DROP VIEW IF EXISTS _trgm_status CASCADE;"
```

Then re-run Step 2 + Step 6 from `NEXT-STEPS.md`.
