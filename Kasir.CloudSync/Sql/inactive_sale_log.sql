-- Postgres DDL for the inactive_sale_log mirror table (PR-K3, dashboard).
-- One row per inactive product (status 'I') per day it was scanned at a till.
-- id is a per-register SQLite rowid, so the cloud key is (register_id, id).
-- sale_date / created_at stay TEXT in the POS local formats ('YYYY-MM-DD', 'YYYY-MM-DD HH:MM:SS').
-- Mirror-only: never restored into register snapshots (TableMapping.RestoreToRegister).

CREATE TABLE IF NOT EXISTS inactive_sale_log (
    id              INTEGER     NOT NULL,
    register_id     TEXT        NOT NULL DEFAULT '',
    product_code    TEXT        NOT NULL,
    sale_date       TEXT        NOT NULL,
    created_at      TEXT        NOT NULL,
    PRIMARY KEY (register_id, id)
);
CREATE INDEX IF NOT EXISTS idx_inactive_sale_log_date ON inactive_sale_log (sale_date);
CREATE INDEX IF NOT EXISTS idx_inactive_sale_log_product ON inactive_sale_log (product_code);
