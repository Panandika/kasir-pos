// Gate A0.1 — TLS smoke test for Supabase Postgres from target hardware (Windows 10).
// Verifies that Npgsql can negotiate a TLS 1.2/1.3 session with Supabase using
// SslMode=Require before any Kasir.CloudSync work begins.
//
// Exit codes:
//   0  -> GATE A0.1: PASS (handshake + SELECT 1 + SELECT version() all succeeded)
//   1  -> Missing SUPABASE_CONN_STRING env var
//   2  -> Npgsql threw during Open / Execute (see stderr for full exception)
//   3  -> Query returned unexpected result
//
// Usage (on target Windows 10 box):
//   setx SUPABASE_CONN_STRING "Host=db.PROJECT.supabase.co;Port=5432;Database=postgres;Username=postgres;Password=...;SslMode=Require;Trust Server Certificate=false"
//   (open new cmd window after setx so env var propagates)
//   TlsSmokeTest.exe
//
// The connection string MUST include SslMode=Require. The tool will still honour
// whatever mode the operator supplies, but logs which mode was used so failures
// are easy to diagnose.

using System;
using System.Data;
using System.Threading.Tasks;
using Npgsql;

namespace Kasir.Tools.TlsSmokeTest;

internal static class Program
{
    private const string EnvVar = "SUPABASE_CONN_STRING";

    internal static async Task<int> Main(string[] args)
    {
        Console.WriteLine($"Gate A0.1 TLS smoke test — {DateTime.UtcNow:yyyy-MM-ddTHH:mm:ssZ}");
        Console.WriteLine($"OS: {Environment.OSVersion} ({Environment.OSVersion.Platform})");
        Console.WriteLine($"Runtime: .NET {Environment.Version}");
        Console.WriteLine();

        var connStr = Environment.GetEnvironmentVariable(EnvVar);
        if (string.IsNullOrWhiteSpace(connStr))
        {
            Console.Error.WriteLine($"GATE A0.1: FAIL");
            Console.Error.WriteLine($"Missing env var {EnvVar}. See README.md.");
            return 1;
        }

        // Parse + log the SslMode + Host without leaking the password.
        NpgsqlConnectionStringBuilder builder;
        try
        {
            builder = new NpgsqlConnectionStringBuilder(connStr);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"GATE A0.1: FAIL");
            Console.Error.WriteLine($"Invalid connection string: {ex.Message}");
            return 1;
        }

        Console.WriteLine($"Host       : {builder.Host}");
        Console.WriteLine($"Port       : {builder.Port}");
        Console.WriteLine($"Database   : {builder.Database}");
        Console.WriteLine($"Username   : {builder.Username}");
        Console.WriteLine($"SslMode    : {builder.SslMode}");
        Console.WriteLine();

        await using var conn = new NpgsqlConnection(connStr);
        try
        {
            Console.WriteLine("Opening connection (TLS handshake happens here)...");
            await conn.OpenAsync().ConfigureAwait(false);
            Console.WriteLine($"  Opened. Server reports version {conn.PostgreSqlVersion}.");
            Console.WriteLine();
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"GATE A0.1: FAIL");
            Console.Error.WriteLine($"Npgsql.OpenAsync threw: {ex.GetType().Name}");
            Console.Error.WriteLine(ex.ToString());
            return 2;
        }

        try
        {
            Console.WriteLine("Query 1: SELECT 1");
            await using (var cmd = new NpgsqlCommand("SELECT 1;", conn))
            {
                var result = await cmd.ExecuteScalarAsync().ConfigureAwait(false);
                Console.WriteLine($"  Result: {result}");
                if (result is not int i || i != 1)
                {
                    Console.Error.WriteLine($"GATE A0.1: FAIL");
                    Console.Error.WriteLine($"Expected 1, got {result}");
                    return 3;
                }
            }

            Console.WriteLine("Query 2: SELECT version()");
            await using (var cmd = new NpgsqlCommand("SELECT version();", conn))
            {
                var result = await cmd.ExecuteScalarAsync().ConfigureAwait(false);
                Console.WriteLine($"  {result}");
            }

            Console.WriteLine("Query 3: SHOW ssl  (Postgres reports TLS status of the session)");
            await using (var cmd = new NpgsqlCommand("SHOW ssl;", conn))
            {
                var result = await cmd.ExecuteScalarAsync().ConfigureAwait(false);
                Console.WriteLine($"  ssl = {result}");
            }

            Console.WriteLine("Query 4: SELECT ssl_cipher, ssl_version FROM pg_stat_ssl WHERE pid = pg_backend_pid()");
            await using (var cmd = new NpgsqlCommand(
                "SELECT version, cipher FROM pg_stat_ssl WHERE pid = pg_backend_pid();", conn))
            await using (var reader = await cmd.ExecuteReaderAsync().ConfigureAwait(false))
            {
                if (await reader.ReadAsync().ConfigureAwait(false))
                {
                    var version = reader.IsDBNull(0) ? "<null>" : reader.GetString(0);
                    var cipher = reader.IsDBNull(1) ? "<null>" : reader.GetString(1);
                    Console.WriteLine($"  TLS version : {version}");
                    Console.WriteLine($"  TLS cipher  : {cipher}");
                }
            }
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"GATE A0.1: FAIL");
            Console.Error.WriteLine($"Query phase threw: {ex.GetType().Name}");
            Console.Error.WriteLine(ex.ToString());
            return 2;
        }

        Console.WriteLine();
        Console.WriteLine("GATE A0.1: PASS");
        return 0;
    }
}
