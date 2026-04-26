using Microsoft.Data.Sqlite;

namespace Kasir.Data.Migrations
{
    /// <summary>
    /// Drop barcode column from products and the product_barcodes table entirely.
    /// FTS5 cannot ALTER its columns — drop and recreate the products_fts virtual
    /// table and its 3 sync triggers without the barcode column. Each step is
    /// wrapped in try/catch so the migration is idempotent on already-migrated
    /// databases (or fresh DBs created from the post-migration Schema.sql).
    /// </summary>
    public class Migration_005 : IMigration
    {
        public int Version { get { return 5; } }
        public string Description { get { return "Drop barcode column + product_barcodes table; rebuild FTS5"; } }

        public void Up(SqliteConnection db)
        {
            string[] steps = new string[]
            {
                // 1. Drop FTS sync triggers (they reference NEW.barcode / OLD.barcode)
                "DROP TRIGGER IF EXISTS products_fts_ai",
                "DROP TRIGGER IF EXISTS products_fts_au",
                "DROP TRIGGER IF EXISTS products_fts_ad",

                // 2. Drop the secondary barcodes table (and its indexes go with it)
                "DROP TABLE IF EXISTS product_barcodes",

                // 3. Drop the dedicated barcode index on products before dropping the column
                "DROP INDEX IF EXISTS idx_products_barcode",

                // 4. Drop the products sync UPDATE trigger — it references OLD.barcode/NEW.barcode
                //    which would block the column drop. Recreated below without barcode.
                "DROP TRIGGER IF EXISTS trg_products_sync_u",

                // 5. Drop the v_product_lookup view — it references p.barcode. Recreated below.
                "DROP VIEW IF EXISTS v_product_lookup",

                // 6. Drop the barcode column on products (SQLite 3.43.2 supports DROP COLUMN)
                "ALTER TABLE products DROP COLUMN barcode",

                // 5. Drop the FTS5 virtual table (cannot be ALTERed)
                "DROP TABLE IF EXISTS products_fts",

                // 6. Recreate products_fts without barcode
                @"CREATE VIRTUAL TABLE products_fts USING fts5(
                    product_code,
                    name,
                    content='products',
                    content_rowid='id',
                    tokenize='unicode61 remove_diacritics 2'
                )",

                // 7. Recreate the 3 sync triggers without barcode
                @"CREATE TRIGGER products_fts_ai AFTER INSERT ON products BEGIN
                    INSERT INTO products_fts(rowid, product_code, name)
                    VALUES (new.id, new.product_code, new.name);
                END",

                @"CREATE TRIGGER products_fts_ad AFTER DELETE ON products BEGIN
                    INSERT INTO products_fts(products_fts, rowid, product_code, name)
                    VALUES ('delete', old.id, old.product_code, old.name);
                END",

                @"CREATE TRIGGER products_fts_au AFTER UPDATE ON products BEGIN
                    INSERT INTO products_fts(products_fts, rowid, product_code, name)
                    VALUES ('delete', old.id, old.product_code, old.name);
                    INSERT INTO products_fts(rowid, product_code, name)
                    VALUES (new.id, new.product_code, new.name);
                END",

                // 8. Repopulate FTS index from products
                "INSERT INTO products_fts(products_fts) VALUES('rebuild')",

                // 9. Recreate trg_products_sync_u without OLD.barcode/NEW.barcode comparison
                @"CREATE TRIGGER trg_products_sync_u AFTER UPDATE ON products
                WHEN OLD.name != NEW.name
                  OR OLD.price != NEW.price
                  OR OLD.price1 != NEW.price1
                  OR OLD.price2 != NEW.price2
                  OR OLD.price3 != NEW.price3
                  OR OLD.price4 != NEW.price4
                  OR OLD.buying_price != NEW.buying_price
                  OR OLD.cost_price != NEW.cost_price
                  OR OLD.dept_code != NEW.dept_code
                  OR OLD.vendor_code != NEW.vendor_code
                  OR OLD.status != NEW.status
                  OR OLD.disc_pct != NEW.disc_pct
                  OR OLD.qty_break2 != NEW.qty_break2
                  OR OLD.qty_break3 != NEW.qty_break3
                  OR OLD.open_price != NEW.open_price
                  OR OLD.is_consignment != NEW.is_consignment
                  OR OLD.vat_flag != NEW.vat_flag
                BEGIN
                    INSERT INTO sync_queue(register_id, table_name, record_key, operation)
                    VALUES (COALESCE((SELECT value FROM config WHERE key='register_id'), 'unknown'),
                            'products', NEW.product_code, 'U');
                END",

                // 10. Recreate v_product_lookup without barcode column
                @"CREATE VIEW v_product_lookup AS
                SELECT
                    p.id,
                    p.product_code,
                    p.name,
                    p.price,
                    p.price1,
                    p.price2,
                    p.price3,
                    p.unit,
                    p.unit1,
                    p.conversion1,
                    p.qty_break2,
                    p.qty_break3,
                    p.open_price,
                    p.is_consignment,
                    p.vat_flag,
                    p.cost_price,
                    p.status
                FROM products p
                WHERE p.status = 'A'"
            };

            using (var cmd = db.CreateCommand())
            {
                foreach (string sql in steps)
                {
                    try
                    {
                        cmd.CommandText = sql;
                        cmd.ExecuteNonQuery();
                    }
                    catch (SqliteException)
                    {
                        // Step already applied (idempotent on re-run / fresh DB)
                    }
                }
            }
        }
    }
}
