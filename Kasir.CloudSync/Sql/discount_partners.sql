CREATE TABLE IF NOT EXISTS discount_partners (
    id             BIGINT      PRIMARY KEY,
    account_code   TEXT        NOT NULL,
    sub_code       TEXT        NOT NULL,
    disc_pct       INTEGER     NOT NULL DEFAULT 0,
    changed_by     INTEGER,
    changed_at     TIMESTAMPTZ,
    UNIQUE (account_code, sub_code)
);
