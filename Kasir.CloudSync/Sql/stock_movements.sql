CREATE TABLE IF NOT EXISTS stock_movements (
    id              BIGINT      PRIMARY KEY,
    product_code    TEXT        NOT NULL,
    vendor_code     TEXT,
    dept_code       TEXT,
    location_code   TEXT,
    account_code    TEXT,
    sub_code        TEXT,
    journal_no      TEXT        NOT NULL,
    movement_type   TEXT        NOT NULL,
    doc_date        TEXT        NOT NULL,
    period_code     TEXT        NOT NULL,
    qty_in          BIGINT      NOT NULL DEFAULT 0,
    qty_out         BIGINT      NOT NULL DEFAULT 0,
    val_in          BIGINT      NOT NULL DEFAULT 0,
    val_out         BIGINT      NOT NULL DEFAULT 0,
    cost_price      BIGINT      NOT NULL DEFAULT 0,
    is_posted       INTEGER     NOT NULL DEFAULT 0,
    is_archived     INTEGER     NOT NULL DEFAULT 0,
    changed_by      INTEGER,
    changed_at      TIMESTAMPTZ,
    created_at      TIMESTAMPTZ
);
CREATE INDEX IF NOT EXISTS idx_stock_movements_product ON stock_movements (product_code);
CREATE INDEX IF NOT EXISTS idx_stock_movements_period ON stock_movements (period_code);
CREATE INDEX IF NOT EXISTS idx_stock_movements_journal ON stock_movements (journal_no);
