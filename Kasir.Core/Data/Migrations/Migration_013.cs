using Microsoft.Data.Sqlite;

namespace Kasir.Data.Migrations
{
    /// <summary>
    /// PR-K4 category quick keys: six open-price, non-stock marker products the cashier
    /// sells by typed price instead of code "1". margin_pct (x100, default 25.00%) drives
    /// the estimated COGS and is the per-category setting. INSERT OR IGNORE: an existing
    /// row (and any margin configured on it) is never changed.
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
                       open_price, margin_pct)
                    VALUES
                      ('AL', '42',  'ALAT LISTRIK', 'A', 'PCS', 0, 0, 0, 'Y', 2500),
                      ('AT', '10',  'ALAT TULIS',   'A', 'PCS', 0, 0, 0, 'Y', 2500),
                      ('PR', '100', 'PERABOT',      'A', 'PCS', 0, 0, 0, 'Y', 2500),
                      ('PL', '22',  'PLASTIK',      'A', 'PCS', 0, 0, 0, 'Y', 2500),
                      ('MY', '44',  'MAINAN',       'A', 'PCS', 0, 0, 0, 'Y', 2500),
                      ('LL', '100', 'LAIN-LAIN',    'A', 'PCS', 0, 0, 0, 'Y', 2500);";
                cmd.ExecuteNonQuery();
            }
        }
    }
}
