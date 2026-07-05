-- Postgres DDL for the purchase_items mirror table.
-- Lines under purchases.journal_no (RECDTL.DBF / RTNDTL.DBF in legacy DBF source).
-- INTEGER money (x100 cents) -> BIGINT, INTEGER qty (x100) -> BIGINT.

CREATE TABLE IF NOT EXISTS purchase_items (
    id              BIGSERIAL   PRIMARY KEY,
    journal_no      TEXT        NOT NULL,
    product_code    TEXT        NOT NULL,
    remark          TEXT        DEFAULT '',
    quantity        BIGINT      NOT NULL DEFAULT 0,
    value           BIGINT      NOT NULL DEFAULT 0,
    unit_price      BIGINT      NOT NULL DEFAULT 0,
    disc_pct        INTEGER     NOT NULL DEFAULT 0,
    disc_value      BIGINT      NOT NULL DEFAULT 0,
    cogs            BIGINT      NOT NULL DEFAULT 0,
    legacy_source   TEXT
);

CREATE INDEX IF NOT EXISTS idx_purchase_items_journal ON purchase_items (journal_no);
CREATE INDEX IF NOT EXISTS idx_purchase_items_product ON purchase_items (product_code);
-- FK enabled after initial load completes (constraints.sql).
