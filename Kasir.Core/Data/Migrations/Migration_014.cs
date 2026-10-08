using Microsoft.Data.Sqlite;

namespace Kasir.Data.Migrations
{
    /// <summary>
    /// WP-02 CloudSync push. Seeds the WatermarkPusher config keys on an existing DB
    /// (Schema.sql seeds them on a fresh one; INSERT OR IGNORE keeps any value), and
    /// records where the stock ledger switched to x100 qty:
    /// stock_ledger_x100_from_id = the first POS stock_movements.id written by this
    /// build. POS rows below it with a non-legacy journal (not GHIST-/GSMRY-) carry
    /// plain-unit qty from before the fix. They are NOT rewritten here (owner decision,
    /// see RUN-REPORT WP-02); the marker lets a later normalisation or a dashboard view
    /// tell the two scales apart. Ids >= 5,000,000,000 are dashboard-originated (WP-04)
    /// and excluded from the max.
    /// </summary>
    public class Migration_014 : IMigration
    {
        public int Version { get { return 14; } }
        public string Description { get { return "CloudSync push watermarks + x100 stock ledger cutover marker"; } }

        public void Up(SqliteConnection db)
        {
            using (var cmd = db.CreateCommand())
            {
                cmd.CommandText = @"
                    INSERT OR IGNORE INTO config (key, value, description) VALUES
                      ('cloud_push_wm_stock_movements', '0', 'Last pushed stock_movements.id'),
                      ('cloud_push_wm_shifts', '0', 'Last pushed shifts.id');

                    INSERT OR IGNORE INTO config (key, value, description)
                    SELECT 'stock_ledger_x100_from_id',
                           CAST(COALESCE(MAX(id), 0) + 1 AS TEXT),
                           'First POS stock_movements.id written with qty x100 (WP-02); older non-legacy POS rows are plain units'
                    FROM stock_movements
                    WHERE id < 5000000000;";
                cmd.ExecuteNonQuery();
            }
        }
    }
}
