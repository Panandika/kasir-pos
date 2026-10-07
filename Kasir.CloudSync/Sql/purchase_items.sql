-- Postgres DDL for the purchase_items mirror table.
-- Lines under purchases.journal_no (RECDTL.DBF / RTNDTL.DBF in legacy DBF source).
-- INTEGER money (x100 cents) -> BIGINT, INTEGER qty (x100) -> BIGINT.
-- Legacy per-line codes (account_code, sub_code, group_code, customer_code, qty1,
-- qty2, roll) are not mirrored: see SkipList.ExcludedColumns.
-- legacy_source is cloud-only (set by the legacy DBF sync).

CREATE TABLE IF NOT EXISTS purchase_items (
    id              BIGSERIAL   PRIMARY KEY,
    journal_no      TEXT        NOT NULL,
    order_ref       TEXT        DEFAULT '',
    product_code    TEXT        NOT NULL,
    remark          TEXT        DEFAULT '',
    quantity        BIGINT      NOT NULL DEFAULT 0,
    qty_order       BIGINT      NOT NULL DEFAULT 0,
    value           BIGINT      NOT NULL DEFAULT 0,
    unit_price      BIGINT      NOT NULL DEFAULT 0,
    inv_price       BIGINT      NOT NULL DEFAULT 0,
    cogs            BIGINT      NOT NULL DEFAULT 0,
    disc_pct        INTEGER     NOT NULL DEFAULT 0,
    disc2_pct       INTEGER     NOT NULL DEFAULT 0,
    disc_amount     BIGINT      NOT NULL DEFAULT 0,
    disc_value      BIGINT      NOT NULL DEFAULT 0,
    legacy_source   TEXT
);

-- Upgrade path for mirrors created from the earlier 11-column DDL.
ALTER TABLE purchase_items ADD COLUMN IF NOT EXISTS order_ref   TEXT    DEFAULT '';
ALTER TABLE purchase_items ADD COLUMN IF NOT EXISTS qty_order   BIGINT  NOT NULL DEFAULT 0;
ALTER TABLE purchase_items ADD COLUMN IF NOT EXISTS inv_price   BIGINT  NOT NULL DEFAULT 0;
ALTER TABLE purchase_items ADD COLUMN IF NOT EXISTS disc2_pct   INTEGER NOT NULL DEFAULT 0;
ALTER TABLE purchase_items ADD COLUMN IF NOT EXISTS disc_amount BIGINT  NOT NULL DEFAULT 0;

CREATE INDEX IF NOT EXISTS idx_purchase_items_journal ON purchase_items (journal_no);
CREATE INDEX IF NOT EXISTS idx_purchase_items_product ON purchase_items (product_code);
-- FK enabled after initial load completes (constraints.sql).
