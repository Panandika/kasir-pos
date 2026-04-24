CREATE TABLE IF NOT EXISTS sale_items (
    id              BIGINT      PRIMARY KEY,
    journal_no      TEXT        NOT NULL,
    order_ref       TEXT,
    account_code    TEXT,
    sub_code        TEXT,
    product_code    TEXT        NOT NULL,
    remark          TEXT,
    quantity        BIGINT      NOT NULL DEFAULT 0,
    qty_box         BIGINT      NOT NULL DEFAULT 0,
    value           BIGINT      NOT NULL DEFAULT 0,
    cogs            BIGINT      NOT NULL DEFAULT 0,
    group_code      TEXT,
    disc_pct        INTEGER     NOT NULL DEFAULT 0,
    unit_price      BIGINT      NOT NULL DEFAULT 0,
    inv_price       BIGINT      NOT NULL DEFAULT 0,
    point_value     BIGINT      NOT NULL DEFAULT 0,
    qty_order       BIGINT      NOT NULL DEFAULT 0,
    disc_value      BIGINT      NOT NULL DEFAULT 0
);
CREATE INDEX IF NOT EXISTS idx_sale_items_journal ON sale_items (journal_no);
CREATE INDEX IF NOT EXISTS idx_sale_items_product ON sale_items (product_code);
-- FK enabled after Phase C initial load completes (constraints.sql).
