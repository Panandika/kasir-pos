using Microsoft.Data.Sqlite;
using System.IO;
using System.Reflection;

namespace Kasir.Tests.TestHelpers
{
    public static class TestDb
    {
        public static SqliteConnection Create()
        {
            var conn = new SqliteConnection("Data Source=:memory:");
            conn.Open();

            // Configure pragmas
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = "PRAGMA foreign_keys=ON;";
                cmd.ExecuteNonQuery();
            }

            // FTS5 is compiled into SQLitePCLRaw.bundle_e_sqlite3 — no extension loading needed

            // Execute schema
            string schema = ReadSchema();
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = schema;
                cmd.ExecuteNonQuery();
            }

            return conn;
        }

        private static string ReadSchema()
        {
            // Read from the Kasir.Core assembly's embedded resource
            var kasirAssembly = Assembly.Load("Kasir.Core");
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
