CREATE TABLE IF NOT EXISTS discounts (
    id             BIGINT      PRIMARY KEY,
    product_code   TEXT        NOT NULL DEFAULT '',
    dept_code      TEXT        NOT NULL DEFAULT '',
    sub_code       TEXT,
    date_start     TIMESTAMPTZ,
    date_end       TIMESTAMPTZ,
    time_start     TEXT,
    time_end       TEXT,
    disc_pct       INTEGER     NOT NULL DEFAULT 0,
    disc1_pct      INTEGER     NOT NULL DEFAULT 0,
    disc2_pct      INTEGER     NOT NULL DEFAULT 0,
    disc3_pct      INTEGER     NOT NULL DEFAULT 0,
    disc_amount    BIGINT      NOT NULL DEFAULT 0,
    value          BIGINT      NOT NULL DEFAULT 0,
    value1         BIGINT      NOT NULL DEFAULT 0,
    value2         BIGINT      NOT NULL DEFAULT 0,
    value3         BIGINT      NOT NULL DEFAULT 0,
    min_qty        BIGINT      NOT NULL DEFAULT 0,
    max_qty        BIGINT      NOT NULL DEFAULT 0,
    price_override BIGINT      NOT NULL DEFAULT 0,
    description    TEXT,
    priority       INTEGER     NOT NULL DEFAULT 0,
    is_active      INTEGER     NOT NULL DEFAULT 1,
    changed_by     INTEGER,
    changed_at     TIMESTAMPTZ
);
CREATE INDEX IF NOT EXISTS idx_discounts_product ON discounts (product_code);
CREATE INDEX IF NOT EXISTS idx_discounts_dept ON discounts (dept_code);
CREATE INDEX IF NOT EXISTS idx_discounts_active ON discounts (is_active, priority);
