-- Phase C follow-up — strict FK constraints in the Postgres mirror.
-- Applied by InitialLoader.RunAsync() AFTER the bulk load + parity check
-- succeeds. With session_replication_role temporarily flipped to 'origin'
-- before this script runs, any orphan that the data-quality scanner missed
-- will fail loudly here rather than silently corrupting the mirror.
--
-- Idempotent: every constraint uses ADD CONSTRAINT IF NOT EXISTS-style
-- patterns (Postgres requires DROP/ADD or a check; we use a DO block).
-- Skipping a constraint that already exists keeps re-runs cheap.

-- Helper: only add the FK if it does not already exist.
DO $$
BEGIN
    -- product_barcodes -> products
    IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'fk_product_barcodes_product') THEN
        ALTER TABLE product_barcodes
            ADD CONSTRAINT fk_product_barcodes_product
            FOREIGN KEY (product_code) REFERENCES products (product_code) ON DELETE RESTRICT;
    END IF;

    -- sale_items -> sales
    IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'fk_sale_items_journal') THEN
        ALTER TABLE sale_items
            ADD CONSTRAINT fk_sale_items_journal
            FOREIGN KEY (journal_no) REFERENCES sales (journal_no) ON DELETE RESTRICT;
    END IF;

    -- sale_items -> products
    IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'fk_sale_items_product') THEN
        ALTER TABLE sale_items
            ADD CONSTRAINT fk_sale_items_product
            FOREIGN KEY (product_code) REFERENCES products (product_code) ON DELETE RESTRICT;
    END IF;

    -- stock_movements -> products
    IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'fk_stock_movements_product') THEN
        ALTER TABLE stock_movements
            ADD CONSTRAINT fk_stock_movements_product
            FOREIGN KEY (product_code) REFERENCES products (product_code) ON DELETE RESTRICT;
    END IF;

    -- purchases -> subsidiaries (sub_code is NOT NULL on the source)
    IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'fk_purchases_sub') THEN
        ALTER TABLE purchases
            ADD CONSTRAINT fk_purchases_sub
            FOREIGN KEY (sub_code) REFERENCES subsidiaries (sub_code) ON DELETE RESTRICT;
    END IF;

    -- orders -> subsidiaries (sub_code is NOT NULL on the source)
    IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'fk_orders_sub') THEN
        ALTER TABLE orders
            ADD CONSTRAINT fk_orders_sub
            FOREIGN KEY (sub_code) REFERENCES subsidiaries (sub_code) ON DELETE RESTRICT;
    END IF;
END $$;

-- Constraints intentionally NOT added (and why):
--   sales.sub_code            -> subsidiaries  : column is OFTEN empty
--                                                 (anonymous walk-in customer);
--                                                 strict FK would block legacy
--                                                 anonymous-sale rows.
--   stock_movements.vendor_code -> subsidiaries: same — frequently '' default.
--   discounts.product_code     -> products      : product_code is '' for
--                                                 dept-level discounts; not
--                                                 a real FK.
--   accounts.parent_code        -> accounts(self): self-referencing on a TEXT
--                                                 natural key; root rows have
--                                                 NULL/'' parent. Add later
--                                                 only if reporting needs it.
