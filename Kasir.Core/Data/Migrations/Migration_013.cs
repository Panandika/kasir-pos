using Microsoft.Data.Sqlite;

namespace Kasir.Data.Migrations
{
    /// <summary>
    /// PR-K4 category quick keys: six open-price, non-stock marker products the cashier
    /// sells by typed price, alongside code "1". Like code "1" they carry no cost (COGS 0 =
    /// unknown cost, owner decision 2026-10-09); the row exists for the name and the
    /// department, so sales-by-category works. INSERT OR IGNORE: an existing row is never
    /// changed.
    /// Also creates inactive_sale_log (Migration_012's table) if missing; see the SQL.
    /// Departments follow the store's chart: 42 ALAT LISTRIK, 10 ALAT TULIS, 22 PLASTIK,
    /// 44 MAINAN; PERABOT has no department of its own and goes to 100 DLL with LAIN-LAIN.
    /// </summary>
    public class Migration_013 : IMigration
    {
        public int Version { get { return 13; } }
        public string Description { get { return "Seed category quick-key products (AL/AT/PR/PL/MY/LL)"; } }

        public void Up(SqliteConnection db)
        {
            using (var cmd = db.CreateCommand())
            {
                cmd.CommandText = @"
                    INSERT OR IGNORE INTO products
                      (product_code, dept_code, name, status, unit, price, buying_price, cost_price,
                       open_price)
                    VALUES
                      ('AL', '42',  'ALAT LISTRIK', 'A', 'PCS', 0, 0, 0, 'Y'),
                      ('AT', '10',  'ALAT TULIS',   'A', 'PCS', 0, 0, 0, 'Y'),
                      ('PR', '100', 'PERABOT',      'A', 'PCS', 0, 0, 0, 'Y'),
                      ('PL', '22',  'PLASTIK',      'A', 'PCS', 0, 0, 0, 'Y'),
                      ('MY', '44',  'MAINAN',       'A', 'PCS', 0, 0, 0, 'Y'),
                      ('LL', '100', 'LAIN-LAIN',    'A', 'PCS', 0, 0, 0, 'Y');

                    -- Migration_012's effect (PR-K3), idempotent: a register that reaches
                    -- schema 13 from a build without 012 would otherwise never get it,
                    -- because the runner only applies versions above schema_version.
                    CREATE TABLE IF NOT EXISTS inactive_sale_log (
                        id              INTEGER PRIMARY KEY,
                        register_id     TEXT    NOT NULL DEFAULT '',
                        product_code    TEXT    NOT NULL,
                        sale_date       TEXT    NOT NULL,
                        created_at      TEXT    NOT NULL DEFAULT (datetime('now','localtime')),
                        UNIQUE(product_code, sale_date)
                    );";
                cmd.ExecuteNonQuery();
            }
        }
    }
}
