CREATE TABLE IF NOT EXISTS memorial_journals (
    journal_no      TEXT        PRIMARY KEY,
    id              INTEGER     NOT NULL,
    doc_type        TEXT        NOT NULL,
    doc_date        TEXT        NOT NULL,
    ref             TEXT,
    ref_no          TEXT,
    remark          TEXT,
    group_code      TEXT,
    control         INTEGER     NOT NULL DEFAULT 1,
    print_count     INTEGER     NOT NULL DEFAULT 0,
    approved_by     INTEGER,
    period_code     TEXT        NOT NULL,
    register_id     TEXT,
    legacy_source   TEXT,
    changed_by      INTEGER,
    changed_at      TIMESTAMPTZ
);
CREATE INDEX IF NOT EXISTS idx_memorial_period ON memorial_journals (period_code);
