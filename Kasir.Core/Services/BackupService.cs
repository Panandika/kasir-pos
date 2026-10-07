using System;
using System.IO;
using Microsoft.Data.Sqlite;

namespace Kasir.Services
{
    /// <summary>
    /// Online backup of the live register database. kasir.db runs in WAL mode, so recent
    /// commits sit in kasir.db-wal until a checkpoint and a plain File.Copy of kasir.db
    /// misses them (or copies a torn page set while the POS is writing). This uses SQLite's
    /// backup API, which reads a consistent snapshot through a normal connection.
    /// </summary>
    public static class BackupService
    {
        /// <summary>
        /// Writes a consistent, self-contained (non-WAL) copy of <paramref name="sourceDbPath"/>
        /// to <paramref name="destPath"/>. The copy is built in a temp file next to the
        /// destination, verified with PRAGMA integrity_check, then renamed into place, so a
        /// failed backup never leaves a file with the final name.
        /// </summary>
        /// <exception cref="FileNotFoundException">The source database does not exist.</exception>
        /// <exception cref="IOException">The destination already exists.</exception>
        public static void BackupTo(string sourceDbPath, string destPath)
        {
            if (!File.Exists(sourceDbPath))
                throw new FileNotFoundException("Database tidak ditemukan", sourceDbPath);
            if (File.Exists(destPath))
                throw new IOException("File backup sudah ada: " + destPath);

            string dir = Path.GetDirectoryName(Path.GetFullPath(destPath));
            string tempPath = Path.Combine(dir, Path.GetFileName(destPath) + ".tmp-" + Guid.NewGuid().ToString("N"));

            try
            {
                // Mode=ReadWrite never creates the file; Pooling=False releases the handles
                // on Dispose so the temp file can be renamed/deleted (Windows locks open files).
                using (var src = new SqliteConnection($"Data Source={sourceDbPath};Mode=ReadWrite;Pooling=False"))
                using (var dst = new SqliteConnection($"Data Source={tempPath};Pooling=False"))
                {
                    src.Open();
                    dst.Open();
                    src.BackupDatabase(dst);

                    // The backup carries the source's WAL flag; a backup file must stand alone.
                    Exec(dst, "PRAGMA journal_mode=DELETE;");

                    string check = Scalar(dst, "PRAGMA integrity_check;");
                    if (!string.Equals(check, "ok", StringComparison.OrdinalIgnoreCase))
                        throw new InvalidDataException("Backup gagal verifikasi (integrity_check): " + check);
                }

                // Throws IOException if the destination appeared meanwhile; never overwrites.
                File.Move(tempPath, destPath, overwrite: false);
            }
            finally
            {
                DeleteQuietly(tempPath);
                DeleteQuietly(tempPath + "-journal");
                DeleteQuietly(tempPath + "-wal");
                DeleteQuietly(tempPath + "-shm");
            }
        }

        private static void Exec(SqliteConnection c, string sql)
        {
            using var cmd = c.CreateCommand();
            cmd.CommandText = sql;
            cmd.ExecuteNonQuery();
        }

        private static string Scalar(SqliteConnection c, string sql)
        {
            using var cmd = c.CreateCommand();
            cmd.CommandText = sql;
            return Convert.ToString(cmd.ExecuteScalar());
        }

        private static void DeleteQuietly(string path)
        {
            try { if (File.Exists(path)) File.Delete(path); }
            catch (IOException) { /* best-effort cleanup */ }
            catch (UnauthorizedAccessException) { /* best-effort cleanup */ }
        }
    }
}
