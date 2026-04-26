# Open Questions

## user-review-1 — 2026-04-26 (RESOLVED)

- [x] **SQLite DROP COLUMN support** — `SELECT sqlite_version()` returns **3.43.2**. DROP COLUMN syntax (introduced 3.35.0) is supported natively. **No table-rebuild fallback needed.** Migration_005 can use `ALTER TABLE products DROP COLUMN barcode;` directly.

- [x] **FTS5 rebuild strategy** — `products_fts` is `content='products'` virtual FTS5 table indexing `(product_code, barcode, name)`. ALTER on FTS5 virtual tables is **not supported**; only path is drop + recreate. Migration_005 procedure:
  1. `DROP TRIGGER` for the 3 sync triggers (insert/update/delete on products → products_fts)
  2. `DROP TABLE products_fts`
  3. `ALTER TABLE products DROP COLUMN barcode`
  4. `CREATE VIRTUAL TABLE products_fts USING fts5(product_code, name, content='products', content_rowid='id', tokenize='unicode61 remove_diacritics 2')`
  5. Recreate the 3 triggers without `barcode` column
  6. `INSERT INTO products_fts(products_fts) VALUES('rebuild')` to repopulate (24,560 rows)
  All in one transaction (Migration runner already wraps each migration in BEGIN/COMMIT).

- [x] **Stock data source for ProductView grid** — Use `v_stock_position` view (already exists) which computes `qty_current = qty_last + qty_in - qty_out` per (account_code, sub_code, product_code) for the current period. Field mapping for the 5 rows:
  - **Maximum** = `products.qty_max`
  - **Minimum** = `products.qty_min`
  - **Ideal** = `products.qty_order`
  - **Awal** = `stock_register.qty_last` (period opening balance)
  - **Sekarang** = `v_stock_position.qty_current`
  
  Gudang vs Toko = filter by `location_code` (NOT `sub_code` — `sub_code` is the vendor/customer secondary key). The seeded `locations` table has exactly 2 rows: `T`=TOKO, `G`=GUDANG. Production data: 25,144 stock_movements rows are all `T`; zero rows for `G` (single-store operation historically). Render both columns in the form anyway (zero is fine, future-proofs for warehouse usage). Query: `SELECT location_code, SUM(qty_in - qty_out) FROM stock_movements WHERE product_code=? AND period_code=? GROUP BY location_code`. Awal per location from `stock_register.qty_last` keyed by `(account_code, sub_code, product_code, period_code)` — confirm with user/executor which account_code represents the Toko/Gudang stock account when stock_register starts being populated.

- [x] **PaymentWindow TextBox names** — Confirmed via grep on `PaymentWindow.axaml`:
  - `TxtCash`, `TxtCard`, `TxtVoucher` — match plan assumption
  - Plus `BtnOk`, `BtnCancel`, `LblTotal`, `LblChange`, `CboCardType` (not in scope)
  Apply NumericInputBehavior to TxtCash/TxtCard/TxtVoucher.

- [x] **Migration version numbering** — Existing migrations: `Migration_002.cs`, `Migration_003.cs`, `Migration_004.cs`. **Migration_005 IS the sequential next** — there is no gap. Planner's note about "skipped 003/004" was incorrect. Use `Migration_005` as planned.
