CREATE TABLE IF NOT EXISTS credit_cards (
    card_code      TEXT        PRIMARY KEY,
    name           TEXT        NOT NULL,
    account_code   TEXT,
    fee_pct        INTEGER     NOT NULL DEFAULT 0,
    min_value      BIGINT      NOT NULL DEFAULT 0,
    card_type      TEXT        DEFAULT 'C',
    changed_by     INTEGER,
    changed_at     TIMESTAMPTZ
);
