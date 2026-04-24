-- Phase E US-E1 — capacity monitoring helpers.
-- Run once after all other DDL has been applied. Idempotent.

-- View aggregating row counts + on-disk size per Postgres table.
-- The CapacityMonitor C# class runs SELECT * FROM _capacity_summary;
CREATE OR REPLACE VIEW _capacity_summary AS
SELECT
    relname AS table_name,
    n_live_tup AS row_count,
    pg_total_relation_size(relid) AS bytes_on_disk,
    pg_size_pretty(pg_total_relation_size(relid)) AS pretty_size
FROM pg_stat_user_tables
ORDER BY pg_total_relation_size(relid) DESC;

-- Helper view: total DB size (server-side, single row)
CREATE OR REPLACE VIEW _capacity_total AS
SELECT
    pg_database_size(current_database()) AS bytes_total,
    pg_size_pretty(pg_database_size(current_database())) AS pretty_total;

-- pg_trgm verification: confirm extension + index are present after the
-- products.sql DDL ran. Returns 1 row if everything is in place.
CREATE OR REPLACE VIEW _trgm_status AS
SELECT
    EXISTS (SELECT 1 FROM pg_extension WHERE extname = 'pg_trgm') AS extension_installed,
    EXISTS (SELECT 1 FROM pg_indexes WHERE indexname = 'idx_products_search_trgm') AS gin_index_present,
    (SELECT pg_size_pretty(pg_relation_size('idx_products_search_trgm'::regclass))
     WHERE EXISTS (SELECT 1 FROM pg_indexes WHERE indexname = 'idx_products_search_trgm')) AS gin_index_size;

-- Smoke query for the runbook: a partial-match search hitting the GIN index.
-- Document: pg_trgm partial-match similarity threshold defaults to 0.3.
-- For exact prefix on SKU codes, ILIKE 'KLR-01%' is faster.
-- Example:  SELECT product_code, name, similarity(search_text, 'sampo')
--           FROM products
--           WHERE search_text % 'sampo'
--           ORDER BY similarity(search_text, 'sampo') DESC
--           LIMIT 20;
