CREATE TABLE IF NOT EXISTS members (
    member_code     TEXT        PRIMARY KEY,
    name            TEXT        NOT NULL,
    join_date       TIMESTAMPTZ,
    birthday        TIMESTAMPTZ,
    status          TEXT        NOT NULL DEFAULT 'A',
    opening_balance BIGINT      NOT NULL DEFAULT 0,
    address         TEXT,
    city            TEXT,
    phone           TEXT,
    fax             TEXT,
    remark          TEXT,
    religion        TEXT,
    changed_by      INTEGER,
    changed_at      TIMESTAMPTZ
);
