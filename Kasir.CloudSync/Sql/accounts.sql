CREATE TABLE IF NOT EXISTS accounts (
    account_code   TEXT        PRIMARY KEY,
    account_name   TEXT        NOT NULL,
    parent_code    TEXT,
    is_detail      INTEGER     NOT NULL DEFAULT 1,
    level          INTEGER     NOT NULL DEFAULT 0,
    account_group  INTEGER     NOT NULL DEFAULT 0,
    normal_balance TEXT        NOT NULL DEFAULT 'D' CHECK (normal_balance IN ('D','K')),
    verify_flag    TEXT,
    changed_by     INTEGER,
    changed_at     TIMESTAMPTZ
);
CREATE INDEX IF NOT EXISTS idx_accounts_parent ON accounts (parent_code);
CREATE INDEX IF NOT EXISTS idx_accounts_group ON accounts (account_group);
