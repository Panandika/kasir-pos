CREATE TABLE IF NOT EXISTS stock_adjustments (
    journal_no      TEXT        PRIMARY KEY,
    id              INTEGER     NOT NULL,
    doc_type        TEXT        NOT NULL,
    doc_date        TEXT        NOT NULL,
    location_code   TEXT,
    remark          TEXT,
    total_value     BIGINT      NOT NULL DEFAULT 0,
    is_posted       TEXT,
    control         INTEGER     NOT NULL DEFAULT 1,
    print_count     INTEGER     NOT NULL DEFAULT 0,
    approved_by     INTEGER,
    period_code     TEXT        NOT NULL,
    register_id     TEXT,
    legacy_source   TEXT,
    changed_by      INTEGER,
    changed_at      TIMESTAMPTZ
);
CREATE INDEX IF NOT EXISTS idx_stock_adj_period ON stock_adjustments (period_code);
