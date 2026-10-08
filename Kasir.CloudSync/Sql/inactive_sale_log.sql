-- Postgres DDL for the inactive_sale_log mirror table (PR-K3, dashboard).
-- One row per inactive product (status 'I') per day it was scanned at a till.
-- Natural key (register_id, product_code, sale_date): the local id is a per-register
-- rowid that restarts after a re-commission, so it is not mirrored.
-- sale_date / created_at stay TEXT in the POS local formats ('YYYY-MM-DD', 'YYYY-MM-DD HH:MM:SS').
-- Mirror-only: never restored into register snapshots (TableMapping.RestoreToRegister).
-- The authoritative Supabase DDL (RLS, grants) ships as a sinar-makmur-dashboard migration.

CREATE TABLE IF NOT EXISTS inactive_sale_log (
    register_id     TEXT        NOT NULL,
    product_code    TEXT        NOT NULL,
    sale_date       TEXT        NOT NULL,
    created_at      TEXT        NOT NULL,
    PRIMARY KEY (register_id, product_code, sale_date)
);
CREATE INDEX IF NOT EXISTS idx_inactive_sale_log_date ON inactive_sale_log (sale_date);
CREATE INDEX IF NOT EXISTS idx_inactive_sale_log_product ON inactive_sale_log (product_code);
