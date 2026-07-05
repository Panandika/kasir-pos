-- Postgres DDL for the price_history mirror table.
-- Append-mostly log of cost/price changes per product, keyed by (product_code, doc_date).
-- INTEGER money (x100 cents) -> BIGINT.

CREATE TABLE IF NOT EXISTS price_history (
    product_code    TEXT        NOT NULL,
    doc_date        TEXT        NOT NULL,
    old_date        TEXT,
    sub_code        TEXT,
    value           BIGINT      NOT NULL DEFAULT 0,
    old_value       BIGINT      NOT NULL DEFAULT 0,
    period_code     TEXT        NOT NULL,
    legacy_source   TEXT,
    register_id     TEXT,
    PRIMARY KEY (product_code, doc_date, period_code)
);

CREATE INDEX IF NOT EXISTS idx_price_history_product ON price_history (product_code);
CREATE INDEX IF NOT EXISTS idx_price_history_period ON price_history (period_code);
