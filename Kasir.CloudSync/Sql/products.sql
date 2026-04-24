-- Postgres DDL for the Supabase products mirror table.
-- Column types follow the schema-drift map in plans/cloud-sync-and-local-api.md:
--   INTEGER money (x100 cents) -> BIGINT
--   INTEGER qty fields (x100 etc.) -> BIGINT
--   TEXT flags and codes -> TEXT
--   TEXT ISO timestamps -> TIMESTAMPTZ
--
-- product_code is the natural PK (globally unique in the source).

CREATE TABLE IF NOT EXISTS products (
    product_code    TEXT        PRIMARY KEY,
    name            TEXT        NOT NULL,
    barcode         TEXT,
    dept_code       TEXT,
    account_code    TEXT,
    category_code   TEXT,

    unit            TEXT,
    unit1           TEXT,
    unit2           TEXT,
    status          TEXT        NOT NULL DEFAULT 'A'
                                CHECK (status IN ('A','I','D')),
    vendor_code     TEXT,
    location        TEXT,
    is_consignment  TEXT        DEFAULT 'N' CHECK (is_consignment IN ('Y','N')),
    open_price      TEXT        DEFAULT 'N' CHECK (open_price IN ('Y','N')),

    price           BIGINT      NOT NULL DEFAULT 0,
    price1          BIGINT      NOT NULL DEFAULT 0,
    price2          BIGINT      NOT NULL DEFAULT 0,
    price3          BIGINT      NOT NULL DEFAULT 0,
    price4          BIGINT      NOT NULL DEFAULT 0,
    buying_price    BIGINT      NOT NULL DEFAULT 0,

    qty_min         BIGINT      NOT NULL DEFAULT 0,
    qty_max         BIGINT      NOT NULL DEFAULT 0,
    qty_order       BIGINT      NOT NULL DEFAULT 0,
    factor          BIGINT      NOT NULL DEFAULT 1000,
    conversion1     BIGINT      NOT NULL DEFAULT 100,
    conversion2     BIGINT      NOT NULL DEFAULT 100,
    qty_break2      BIGINT      NOT NULL DEFAULT 0,
    qty_break3      BIGINT      NOT NULL DEFAULT 0,

    changed_at      TIMESTAMPTZ,
    changed_by      INTEGER
);

-- pg_trgm search per Phase E of the plan. Index build cost is paid on UPSERT,
-- dashboard queries get sub-5ms lookups on 24K rows.
CREATE EXTENSION IF NOT EXISTS pg_trgm;

-- Generated search column concatenates the three fields a clerk searches by.
-- STORED so the GIN index can use it directly.
ALTER TABLE products
    ADD COLUMN IF NOT EXISTS search_text TEXT GENERATED ALWAYS AS (
        coalesce(product_code, '') || ' ' ||
        coalesce(name, '')         || ' ' ||
        coalesce(barcode, '')
    ) STORED;

CREATE INDEX IF NOT EXISTS idx_products_search_trgm
    ON products USING gin (search_text gin_trgm_ops);

-- Conventional lookups
CREATE INDEX IF NOT EXISTS idx_products_dept ON products (dept_code);
CREATE INDEX IF NOT EXISTS idx_products_vendor ON products (vendor_code);
CREATE INDEX IF NOT EXISTS idx_products_barcode ON products (barcode);
