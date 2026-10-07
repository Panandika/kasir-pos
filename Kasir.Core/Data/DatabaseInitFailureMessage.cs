using System;

namespace Kasir.Data
{
    /// <summary>
    /// Operator-facing (Indonesian) text shown when DbConnection.InitializeDatabase fails at
    /// startup, so the POS explains what to do instead of silently closing.
    /// </summary>
    public static class DatabaseInitFailureMessage
    {
        public static string Build(Exception ex, string logPath)
        {
            var corrupt = FindCorrupt(ex);
            if (corrupt != null)
            {
                return "Database kasir rusak dan tidak bisa dibuka.\n\n" +
                       "Jangan hapus file apa pun. Hubungi pemilik / admin toko.\n\n" +
                       "Untuk admin: pindahkan data\\kasir.db ke folder lain, buka aplikasi lagi, " +
                       "lalu pilih \"Daftarkan dari cloud (pair code)\" untuk memulihkan data register ini.\n\n" +
                       "Detail: " + corrupt.Message + "\n" +
                       "Detail tersimpan di " + logPath;
            }

            return "Database kasir gagal dibuka: " + (ex?.Message ?? "(tidak diketahui)") + "\n\n" +
                   "Tutup aplikasi lalu buka lagi. Jika masih gagal, hubungi pemilik / admin toko.\n" +
                   "Detail tersimpan di " + logPath;
        }

        private static DatabaseCorruptException FindCorrupt(Exception ex)
        {
            for (int i = 0; ex != null && i < 10; i++)
            {
                if (ex is DatabaseCorruptException dce) return dce;
                ex = ex is AggregateException agg && agg.InnerExceptions.Count == 1
                    ? agg.InnerExceptions[0]
                    : ex.InnerException;
            }
            return null;
        }
    }
}
