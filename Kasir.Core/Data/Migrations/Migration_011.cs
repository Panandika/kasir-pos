using Microsoft.Data.Sqlite;

namespace Kasir.Data.Migrations
{
    /// <summary>
    /// Card and voucher tender accounts (legacy RASIO chart): debit card and QRIS clear
    /// through 112.002 PIUTANG DEBIT CARD until the bank settles, credit cards through
    /// 112.003 PIUTANG CREDIT CARD, vouchers to 610.018 VOUCHER. Only fills settings that
    /// are missing or blank; a configured value is never overwritten.
    /// </summary>
    public class Migration_011 : IMigration
    {
        public int Version { get { return 11; } }
        public string Description { get { return "Card and voucher GL account settings"; } }

        private static readonly string[][] Settings =
        {
            new[] { "ACCOUNT_CARD_CLEARING", "112.002", "Piutang kartu debit / QRIS" },
            new[] { "ACCOUNT_CARD_CLEARING_CREDIT", "112.003", "Piutang kartu kredit" },
            new[] { "ACCOUNT_VOUCHER", "610.018", "Voucher" },
        };

        public void Up(SqliteConnection db)
        {
            foreach (var setting in Settings)
            {
                using (var cmd = db.CreateCommand())
                {
                    cmd.CommandText =
                        @"INSERT INTO config (key, value, description) VALUES (@key, @value, @desc)
                          ON CONFLICT(key) DO UPDATE SET value = excluded.value
                          WHERE config.value IS NULL OR config.value = ''";
                    cmd.Parameters.AddWithValue("@key", setting[0]);
                    cmd.Parameters.AddWithValue("@value", setting[1]);
                    cmd.Parameters.AddWithValue("@desc", setting[2]);
                    cmd.ExecuteNonQuery();
                }
            }
        }
    }
}
