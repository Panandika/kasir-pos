using System.Data.SQLite;
using System.IO;
using System.Reflection;

namespace Kasir.Tests.TestHelpers
{
    public static class TestDb
    {
        public static SQLiteConnection Create()
        {
            var conn = new SQLiteConnection("Data Source=:memory:;Version=3;");
            conn.Open();

            // Configure pragmas
            using (var cmd = new SQLiteCommand(conn))
            {
                cmd.CommandText = "PRAGMA foreign_keys=ON;";
                cmd.ExecuteNonQuery();
            }

            // Try to load FTS5 extension
            try
            {
                conn.EnableExtensions(true);
                conn.LoadExtension("SQLite.Interop.dll", "sqlite3_fts5_init");
            }
            catch
            {
                // FTS5 may not be available in test environment
            }

            // Execute schema
            string schema = ReadSchema();
            using (var cmd = new SQLiteCommand(conn))
            {
                cmd.CommandText = schema;
                cmd.ExecuteNonQuery();
            }

            return conn;
        }

        private static string ReadSchema()
        {
            // Read from the Kasir assembly's embedded resource
            var kasirAssembly = Assembly.Load("Kasir");
            string resourceName = "Kasir.Data.Schema.sql";

            using (var stream = kasirAssembly.GetManifestResourceStream(resourceName))
            {
                if (stream == null)
                {
                    throw new FileNotFoundException(
                        "Could not find embedded schema. Ensure Kasir project is built first.");
                }

                using (var reader = new StreamReader(stream))
                {
                    return reader.ReadToEnd();
                }
            }
        }
    }
}
