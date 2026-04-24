-- ============================================================
-- Migration 001: sync_queue table recreation
-- ============================================================
-- Purpose:
--   1. Add cloud_synced / cloud_synced_at columns for cloud mirror bookkeeping
--   2. Expand table_name CHECK constraint from 15 to 17 tables
--      (adds discount_partners, credit_cards per SyncConfig.SyncedTables)
--   3. Rebuild drain index + new cloud-drain partial index
--
-- Applies to:
--   All 4 databases: 3 registers + hub (sync_queue is LOCAL PER REGISTER)
--
-- Deploy order:
--   Schema first (this script) -> verify columns exist -> deploy updated Kasir.Core binary
--
-- Pre-condition (run on each database BEFORE this script):
--   SELECT COUNT(*) FROM sync_queue WHERE status IN ('pending','failed');
--   -- Must return 0. 'failed' rows must be manually resolved (retried or deleted)
--   -- before migration proceeds. Document disposition in plans/phase-a-preflight-results.md.
--
-- Idempotency:
--   Not idempotent by design. Running twice will fail at "CREATE TABLE sync_queue_new"
--   because the table already exists. Use `DROP TABLE IF EXISTS sync_queue_new;`
--   prologue only if resuming an interrupted migration.
--
-- Rollback:
--   SQLite does not support ALTER TABLE DROP COLUMN. Rollback requires another
--   full recreation. Take a backup copy of kasir.db before running this.
-- ============================================================

BEGIN TRANSACTION;

CREATE TABLE sync_queue_new (
    id              INTEGER PRIMARY KEY,  -- monotonic, local per-register (matches original)
    register_id     TEXT    NOT NULL,
    table_name      TEXT    NOT NULL CHECK(table_name IN (
                        'products', 'product_barcodes',
                        'sales', 'purchases',
                        'cash_transactions',
                        'memorial_journals',
                        'orders',
                        'stock_transfers',
                        'stock_adjustments',
                        'members', 'subsidiaries',
                        'departments', 'discounts',
                        'accounts', 'locations',
                        'discount_partners', 'credit_cards'
                    )),
    record_key      TEXT    NOT NULL,
    operation       TEXT    NOT NULL CHECK(operation IN ('I','U','D')),
    payload         TEXT    CHECK(payload IS NULL OR json_valid(payload)),
    created_at      TEXT    NOT NULL DEFAULT (datetime('now')),
    synced_at       TEXT,
    status          TEXT    NOT NULL DEFAULT 'pending'
                           CHECK(status IN ('pending','synced','failed')),
    retry_count     INTEGER NOT NULL DEFAULT 0,
    last_error      TEXT,
    -- New columns for cloud mirror bookkeeping (orthogonal to LAN sync)
    cloud_synced    INTEGER NOT NULL DEFAULT 0,
    cloud_synced_at TEXT
);

-- Copy all existing rows. Cloud bookkeeping starts at 0/NULL for every row,
-- which means after migration the cloud worker will consider all rows as
-- "not yet cloud-synced" and will attempt to ship them. This is intentional
-- for first-run catch-up; subsequent syncs are incremental.
INSERT INTO sync_queue_new (
    id, register_id, table_name, record_key, operation, payload,
    created_at, synced_at, status, retry_count, last_error,
    cloud_synced, cloud_synced_at
)
SELECT
    id, register_id, table_name, record_key, operation, payload,
    created_at, synced_at, status, retry_count, last_error,
    0, NULL
FROM sync_queue;

DROP TABLE sync_queue;
ALTER TABLE sync_queue_new RENAME TO sync_queue;

-- Drain index for LAN sync (matches original: composite on status+id, partial on pending)
CREATE INDEX idx_sync_queue_drain
    ON sync_queue(status, id)
    WHERE status = 'pending';

-- New drain index for cloud sync: reader picks rows where LAN-synced AND not-yet-cloud-synced
CREATE INDEX idx_sync_queue_cloud_drain
    ON sync_queue(cloud_synced, id)
    WHERE cloud_synced = 0;

COMMIT;

-- ============================================================
-- Post-migration verification (run manually after COMMIT)
-- ============================================================
--
-- 1. Column list:
--      PRAGMA table_info(sync_queue);
--    Expected 13 columns: id, register_id, table_name, record_key, operation,
--    payload, created_at, synced_at, status, retry_count, last_error,
--    cloud_synced, cloud_synced_at
--
-- 2. Row count preserved:
--      SELECT COUNT(*) FROM sync_queue;
--    Must match the count recorded before migration.
--
-- 3. CHECK constraint accepts new tables (smoke test on a throwaway register):
--      INSERT INTO sync_queue (register_id, table_name, record_key, operation)
--      VALUES ('TEST', 'discount_partners', 'X', 'I');
--      INSERT INTO sync_queue (register_id, table_name, record_key, operation)
--      VALUES ('TEST', 'credit_cards', 'X', 'I');
--    Both should succeed. Then:
--      DELETE FROM sync_queue WHERE register_id = 'TEST';
--
-- 4. CHECK constraint rejects bogus tables:
--      INSERT INTO sync_queue (register_id, table_name, record_key, operation)
--      VALUES ('TEST', 'not_a_real_table', 'X', 'I');
--    Must fail with CHECK constraint violation.
--
-- 5. Indexes in place:
--      SELECT name FROM sqlite_master WHERE type = 'index' AND tbl_name = 'sync_queue';
--    Expected at minimum: idx_sync_queue_drain, idx_sync_queue_cloud_drain.
