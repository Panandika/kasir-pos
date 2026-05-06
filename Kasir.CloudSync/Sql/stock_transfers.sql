CREATE TABLE IF NOT EXISTS stock_transfers (
    journal_no      TEXT        PRIMARY KEY,
    id              INTEGER     NOT NULL,
    doc_type        TEXT        NOT NULL,
    doc_date        TEXT        NOT NULL,
    dest_account    TEXT,
    dest_sub        TEXT,
    src_account     TEXT,
    src_sub         TEXT,
    ref             TEXT,
    remark          TEXT,
    control         INTEGER     NOT NULL DEFAULT 1,
    print_count     INTEGER     NOT NULL DEFAULT 0,
    period_code     TEXT        NOT NULL,
    register_id     TEXT,
    legacy_source   TEXT,
    changed_by      INTEGER,
    changed_at      TIMESTAMPTZ
);
CREATE INDEX IF NOT EXISTS idx_stock_transfers_period ON stock_transfers (period_code);
