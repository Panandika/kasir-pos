// contracts-check
//
// Reads ../sinar-makmur-dashboard/supabase/functions/_shared/contracts.json
// (path passed via --contracts-path) and verifies the schema version is the
// one this checkout supports. Run as a CI step in kasir-pos.
//
// Once C# DTOs land (BootstrapTokenClient.PairResult, etc.), expand this
// tool to assert each DTO matches the JSON Schema for the corresponding
// response shape. For now: version gate + structural smoke test.
//
// PRD story: US-P1-6

using System;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace Kasir.Tools.ContractsCheck;

internal static class Program
{
    /// <summary>
    /// Bump in lockstep with $version in contracts.json. CI fails if mismatch.
    /// </summary>
    private const int SupportedContractsVersion = 1;

    /// <summary>
    /// Functions the POS client expects to exist with documented shapes.
    /// CI fails if contracts.json drops one.
    /// </summary>
    private static readonly string[] RequiredFunctions =
    {
        "register-pair",
        "snapshot-download",
        "build-snapshot",
        "snapshot-health",
    };

    public static int Main(string[] args)
    {
        string? path = null;
        for (int i = 0; i < args.Length - 1; i++)
        {
            if (args[i] == "--contracts-path")
            {
                path = args[i + 1];
            }
        }

        path ??= FindDefaultContractsPath();
        if (path is null || !File.Exists(path))
        {
            Console.Error.WriteLine(
                "ERROR: contracts.json not found. Pass --contracts-path or place at the default location:");
            Console.Error.WriteLine(
                "  ../sinar-makmur-dashboard/supabase/functions/_shared/contracts.json");
            return 2;
        }

        Console.WriteLine($"Reading {path}");

        JsonDocument doc;
        try
        {
            using var fs = File.OpenRead(path);
            doc = JsonDocument.Parse(fs);
        }
        catch (JsonException ex)
        {
            Console.Error.WriteLine($"ERROR: contracts.json is not valid JSON: {ex.Message}");
            return 3;
        }

        var root = doc.RootElement;

        if (!root.TryGetProperty("$version", out var versionEl) ||
            versionEl.ValueKind != JsonValueKind.Number ||
            !versionEl.TryGetInt32(out var version))
        {
            Console.Error.WriteLine("ERROR: contracts.json missing $version integer");
            return 4;
        }

        if (version != SupportedContractsVersion)
        {
            Console.Error.WriteLine(
                $"ERROR: contracts.json $version={version}; this checkout supports {SupportedContractsVersion}. " +
                "Bump SupportedContractsVersion in Tools/ContractsCheck/Program.cs and add a migration note.");
            return 5;
        }

        if (!root.TryGetProperty("functions", out var fnsEl) ||
            fnsEl.ValueKind != JsonValueKind.Object)
        {
            Console.Error.WriteLine("ERROR: contracts.json missing 'functions' object");
            return 6;
        }

        var declared = fnsEl.EnumerateObject().Select(p => p.Name).ToHashSet();
        var missing = RequiredFunctions.Where(f => !declared.Contains(f)).ToArray();
        if (missing.Length > 0)
        {
            Console.Error.WriteLine(
                "ERROR: contracts.json missing required function(s): " +
                string.Join(", ", missing));
            return 7;
        }

        Console.WriteLine(
            $"OK: contracts.json v{version} with {declared.Count} function(s); " +
            $"all {RequiredFunctions.Length} required entries present");
        return 0;
    }

    /// <summary>
    /// Walk up from cwd looking for ../sinar-makmur-dashboard/supabase/functions/_shared/contracts.json.
    /// </summary>
    private static string? FindDefaultContractsPath()
    {
        var dir = new DirectoryInfo(Directory.GetCurrentDirectory());
        for (int i = 0; i < 6 && dir is not null; i++)
        {
            var candidate = Path.Combine(
                dir.FullName,
                "sinar-makmur-dashboard",
                "supabase",
                "functions",
                "_shared",
                "contracts.json");
            if (File.Exists(candidate)) return candidate;
            dir = dir.Parent;
        }
        return null;
    }
}
