using Microsoft.Data.Sqlite;

namespace Kasir.Data.Migrations
{
    public class Migration_003 : IMigration
    {
        public int Version { get { return 3; } }
        public string Description { get { return "Seed 'Barang Tanpa Kode' reserved product (code '1', dept 100)"; } }

        public void Up(SqliteConnection db)
        {
            using (var cmd = db.CreateCommand())
            {
                cmd.CommandText = @"
                    INSERT OR IGNORE INTO products
                      (product_code, dept_code, name, status, unit, price, buying_price, cost_price)
                    VALUES
                      ('1', '100', 'Barang Tanpa Kode', 'A', 'PCS', 0, 0, 0);";
                cmd.ExecuteNonQuery();
            }
        }
    }
}
