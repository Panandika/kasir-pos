#nullable enable
using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;

namespace Kasir.Help.Auth
{
    /// <summary>
    /// Strongly-typed config for Bantuan/Supabase machine auth and Edge Functions.
    /// Loaded from %APPDATA%\Kasir\help.json on Windows; ~/.kasir/help.json elsewhere.
    /// </summary>
    public sealed record HelpConfig(
        string SupabaseUrl,
        string AnonKey,
        string MachineEmail,
        string MachinePassword,
        string StoreId,
        string RegisterId);

    public static class HelpConfigLoader
    {
        /// <summary>
        /// Load HelpConfig from disk. Returns null when the file is missing,
        /// unreadable, or malformed. NEVER throws — caller treats null as
        /// "Bantuan operates in offline-only mode" (graceful degradation).
        ///
        /// Search order:
        ///   1. %APPDATA%\Kasir\help.json (or ~/.kasir/help.json on non-Windows) — operator override
        ///   2. {exe directory}/help.json — per-register baked into release ZIP
        /// </summary>
        public static HelpConfig? TryLoad()
        {
            try
            {
                string? path = ResolveExistingPath();
                if (path is null) return null;

                string raw = File.ReadAllText(path);
                using var doc = JsonDocument.Parse(raw);
                var root = doc.RootElement;

                string supabaseUrl = ReadString(root, "SupabaseUrl");
                string anonKey = ReadString(root, "AnonKey");
                string machineEmail = ReadString(root, "MachineEmail");
                string machinePassword = ReadString(root, "MachinePassword");
                string storeId = ReadStringOrDefault(root, "StoreId", "");
                string registerId = ReadStringOrDefault(root, "RegisterId", "");

                if (string.IsNullOrWhiteSpace(supabaseUrl)
                    || string.IsNullOrWhiteSpace(anonKey)
                    || string.IsNullOrWhiteSpace(machineEmail)
                    || string.IsNullOrWhiteSpace(machinePassword))
                {
                    Console.Error.WriteLine("[HelpConfig] missing required field(s) in help.json");
                    return null;
                }

                return new HelpConfig(supabaseUrl, anonKey, machineEmail, machinePassword, storeId, registerId);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[HelpConfig] failed to load: {ex.GetType().Name}: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// %APPDATA%\Kasir\help.json on Windows; ~/.kasir/help.json otherwise.
        /// This is the OPERATOR OVERRIDE path — wins over the baked-in copy if present.
        /// </summary>
        public static string ResolvePath()
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
                return Path.Combine(appData, "Kasir", "help.json");
            }
            string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            return Path.Combine(home, ".kasir", "help.json");
        }

        /// <summary>
        /// Path next to the running executable. Used by the release ZIP build —
        /// release.yml writes per-register help.json into the publish dir so each
        /// register's binary ships with its own machine credentials.
        /// </summary>
        public static string ResolveExePath()
        {
            return Path.Combine(AppContext.BaseDirectory, "help.json");
        }

        /// <summary>
        /// Returns the first existing path from the search order, or null.
        /// </summary>
        private static string? ResolveExistingPath()
        {
            string overridePath = ResolvePath();
            if (File.Exists(overridePath)) return overridePath;
            string exePath = ResolveExePath();
            if (File.Exists(exePath)) return exePath;
            return null;
        }

        private static string ReadString(JsonElement root, string name)
        {
            if (root.TryGetProperty(name, out var el) && el.ValueKind == JsonValueKind.String)
                return el.GetString() ?? "";
            return "";
        }

        private static string ReadStringOrDefault(JsonElement root, string name, string fallback)
        {
            if (root.TryGetProperty(name, out var el) && el.ValueKind == JsonValueKind.String)
                return el.GetString() ?? fallback;
            return fallback;
        }
    }
}
