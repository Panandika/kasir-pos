using Microsoft.Data.Sqlite;

namespace Kasir.Data.Migrations
{
    /// <summary>
    /// Index purchase_items.order_ref: PO → BPB → invoice matching sums linked lines by
    /// order_ref on every receipt/invoice save and F3 lookup. Idempotent (IF NOT EXISTS).
    /// </summary>
    public class Migration_009 : IMigration
    {
        public int Version { get { return 9; } }
        public string Description { get { return "Index purchase_items.order_ref"; } }

        public void Up(SqliteConnection db)
        {
            using (var cmd = db.CreateCommand())
            {
                cmd.CommandText = "CREATE INDEX IF NOT EXISTS idx_purchase_items_order_ref ON purchase_items(order_ref, product_code)";
                cmd.ExecuteNonQuery();
            }
        }
    }
}
