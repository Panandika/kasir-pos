using System;
using Microsoft.Data.Sqlite;

namespace Kasir.Data.Migrations
{
    /// <summary>
    /// Add card_type column to credit_cards master (D=debit, C=credit, Q=QRIS)
    /// so PaymentOverlay can stamp sales.card_type from the selected card
    /// instead of hardcoding "C". Idempotent: re-run on already-migrated DBs
    /// is a no-op (catches only the "duplicate column" SqliteException).
    /// </summary>
    public class Migration_008 : IMigration
    {
        public int Version { get { return 8; } }
        public string Description { get { return "Add card_type to credit_cards"; } }

        public void Up(SqliteConnection db)
        {
            string[] steps = new string[]
            {
                "ALTER TABLE credit_cards ADD COLUMN card_type TEXT DEFAULT 'C'"
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
                    catch (SqliteException ex) when (ex.Message.IndexOf("duplicate column", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        // Column already exists — idempotent re-run; no action needed.
                    }
                }
            }
        }
    }
}
