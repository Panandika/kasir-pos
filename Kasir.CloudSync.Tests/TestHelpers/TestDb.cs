using System.IO;
using System.Reflection;
using Microsoft.Data.Sqlite;

namespace Kasir.CloudSync.Tests.TestHelpers
{
    // Re-creates an in-memory SQLite with the current kasir schema (embedded in
    // Kasir.Core). Mirrors Kasir.Core.Tests.TestHelpers.TestDb so the CloudSync
    // tests stay independent of that assembly.
    public static class TestDb
    {
        public static SqliteConnection Create()
        {
            var conn = new SqliteConnection("Data Source=:memory:");
            conn.Open();
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = "PRAGMA foreign_keys=ON;";
                cmd.ExecuteNonQuery();
            }
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = ReadSchema();
                cmd.ExecuteNonQuery();
            }
            return conn;
        }

        private static string ReadSchema()
        {
            var asm = Assembly.Load("Kasir.Core");
            const string name = "Kasir.Data.Schema.sql";
            using var stream = asm.GetManifestResourceStream(name)
                ?? throw new FileNotFoundException($"Embedded {name} missing");
            using var reader = new StreamReader(stream);
            return reader.ReadToEnd();
        }
    }
}
