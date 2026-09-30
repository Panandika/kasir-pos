using System.IO;
using Microsoft.Data.Sqlite;

namespace Kasir.Data.Migrations
{
    /// <summary>
    /// Seed the chart of accounts from the legacy RASIO GL (acc_0226.dbf) plus the combined
    /// accounts the POS posts to, and fill any unset ACCOUNT_* settings. The original DBF
    /// migration imported PERKIRA.DBF (transaction types), not the GL chart, so live
    /// databases had no accounts and posting failed closed. Idempotent: existing accounts
    /// and configured settings are never changed.
    /// </summary>
    public class Migration_010 : IMigration
    {
        public int Version { get { return 10; } }
        public string Description { get { return "Seed chart of accounts and GL account settings"; } }

        public void Up(SqliteConnection db)
        {
            using (var cmd = db.CreateCommand())
            {
                cmd.CommandText = ReadSeed();
                cmd.ExecuteNonQuery();
            }
        }

        public static string ReadSeed()
        {
            var assembly = typeof(Migration_010).Assembly;
            using (var stream = assembly.GetManifestResourceStream("Kasir.Data.Seeds.chart_of_accounts.sql"))
            {
                if (stream == null)
                    throw new FileNotFoundException("Embedded resource Kasir.Data.Seeds.chart_of_accounts.sql not found");
                using (var reader = new StreamReader(stream))
                    return reader.ReadToEnd();
            }
        }
    }
}
