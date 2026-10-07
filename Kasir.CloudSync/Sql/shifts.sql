-- Postgres DDL for the shifts mirror table (dashboard shift reports).
-- shifts.id is a per-register SQLite rowid, so the cloud key is (register_id, id).
-- opened_at / closed_at stay TEXT in the POS local-time format ('YYYY-MM-DD HH:MM:SS').
-- Mirror-only: never restored into register snapshots (TableMapping.RestoreToRegister).

CREATE TABLE IF NOT EXISTS shifts (
    id              INTEGER     NOT NULL,
    register_id     TEXT        NOT NULL,
    shift_number    TEXT        NOT NULL DEFAULT '1',
    cashier_id      INTEGER     NOT NULL,
    opened_at       TEXT        NOT NULL,
    closed_at       TEXT,
    opening_cash    BIGINT      NOT NULL DEFAULT 0,
    closing_cash    BIGINT,
    expected_cash   BIGINT,
    cash_variance   BIGINT,
    status          TEXT        NOT NULL DEFAULT 'O',
    PRIMARY KEY (register_id, id)
);
CREATE INDEX IF NOT EXISTS idx_shifts_opened_at ON shifts (opened_at);
