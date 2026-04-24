CREATE TABLE IF NOT EXISTS product_barcodes (
    barcode         TEXT        PRIMARY KEY,
    product_code    TEXT        NOT NULL,
    product_name    TEXT,
    qty_per_scan    BIGINT      NOT NULL DEFAULT 1,
    price_override  BIGINT,
    customer_code   TEXT
);
CREATE INDEX IF NOT EXISTS idx_product_barcodes_product ON product_barcodes (product_code);
