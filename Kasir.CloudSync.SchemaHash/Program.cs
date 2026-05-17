// schema-hash
//
// Computes a deterministic SHA-256 over TableMappings.All:
//   sorted table names + for each, sorted (column_name, ColumnKind) pairs
// Used in CI to fail the build on undeclared schema drift.
//
// Usage:
//   dotnet run --project Kasir.CloudSync.SchemaHash -- compute
//   dotnet run --project Kasir.CloudSync.SchemaHash -- verify [hash-file-path]
//
// PRD story: US-P2-3

using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Kasir.CloudSync.Generation;
using Kasir.CloudSync.Mappers;

namespace Kasir.CloudSync.SchemaHash;

internal static class Program
{
    private const string DefaultHashFileRelativeToRepo =
        "Kasir.CloudSync/schema-hash.txt";

    public static int Main(string[] args)
    {
        if (args.Length == 0)
        {
            Console.Error.WriteLine("usage: schema-hash <compute|verify> [hash-file-path]");
            return 2;
        }

        switch (args[0])
        {
            case "compute":
                Console.WriteLine(ComputeHash());
                return 0;

            case "verify":
                return Verify(args.Length > 1 ? args[1] : null);

            default:
                Console.Error.WriteLine($"unknown subcommand: {args[0]}");
                return 2;
        }
    }

    private static string ComputeHash()
    {
        var sb = new StringBuilder();
        foreach (var tableName in TableMappings.All.Keys.OrderBy(k => k, StringComparer.Ordinal))
        {
            var mapping = TableMappings.Get(tableName);
            sb.Append(tableName).Append('\n');
            foreach (var col in mapping.Columns.OrderBy(c => c.Name, StringComparer.Ordinal))
            {
                sb.Append("  ").Append(col.Name).Append(':').Append(col.Kind).Append('\n');
            }
        }
        using var sha = SHA256.Create();
        var bytes = sha.ComputeHash(Encoding.UTF8.GetBytes(sb.ToString()));
        var hex = new StringBuilder(bytes.Length * 2);
        foreach (var b in bytes) hex.Append(b.ToString("x2"));
        return hex.ToString();
    }

    private static int Verify(string explicitPath)
    {
        var path = explicitPath ?? FindDefaultHashFile();
        if (path == null || !File.Exists(path))
        {
            Console.Error.WriteLine(
                $"ERROR: schema-hash.txt not found at {path ?? "(default location)"}. " +
                "Run `schema-hash compute > Kasir.CloudSync/schema-hash.txt` to seed it.");
            return 3;
        }
        var expected = File.ReadAllText(path).Trim();
        var actual = ComputeHash();
        if (expected != actual)
        {
            Console.Error.WriteLine(
                "ERROR: schema drift detected.\n" +
                $"  expected: {expected}\n" +
                $"  actual:   {actual}\n" +
                "Either revert the schema change or bump SUPPORTED_SCHEMA_VERSION in " +
                "Kasir.CloudSync/Snapshot/SnapshotBuilder.cs AND update Kasir.CloudSync/schema-hash.txt.");
            return 5;
        }
        Console.WriteLine($"OK: schema hash matches ({actual})");
        return 0;
    }

    private static string FindDefaultHashFile()
    {
        var dir = new DirectoryInfo(Directory.GetCurrentDirectory());
        for (int i = 0; i < 6 && dir != null; i++)
        {
            var candidate = Path.Combine(dir.FullName, DefaultHashFileRelativeToRepo);
            if (File.Exists(candidate)) return candidate;
            dir = dir.Parent;
        }
        return null;
    }
}
