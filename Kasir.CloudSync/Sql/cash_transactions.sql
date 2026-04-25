CREATE TABLE IF NOT EXISTS cash_transactions (
    journal_no      TEXT        PRIMARY KEY,
    id              INTEGER     NOT NULL,
    doc_type        TEXT        NOT NULL,
    doc_date        TEXT        NOT NULL,
    sub_code        TEXT,
    ref             TEXT,
    remark          TEXT,
    total_value     BIGINT      NOT NULL DEFAULT 0,
    is_posted       TEXT,
    group_code      TEXT,
    description     TEXT,
    control         INTEGER     NOT NULL DEFAULT 1,
    print_count     INTEGER     NOT NULL DEFAULT 0,
    approved_by     INTEGER,
    period_code     TEXT        NOT NULL,
    register_id     TEXT,
    legacy_source   TEXT,
    changed_by      INTEGER,
    changed_at      TIMESTAMPTZ
);
CREATE INDEX IF NOT EXISTS idx_cash_doc_date ON cash_transactions (doc_date);
CREATE INDEX IF NOT EXISTS idx_cash_period ON cash_transactions (period_code);
