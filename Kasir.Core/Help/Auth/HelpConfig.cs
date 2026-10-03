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
        /// Load HelpConfig. Returns null when anything required is missing,
        /// unreadable, or malformed. NEVER throws — caller treats null as
        /// "Bantuan operates in offline-only mode" (graceful degradation).
        ///
        /// Public fields (SupabaseUrl, AnonKey, StoreId, RegisterId) come from help.json:
        ///   1. %APPDATA%\Kasir\help.json (or ~/.kasir/help.json on non-Windows) — operator override
        ///   2. {exe directory}/help.json — baked into the release ZIP
        ///
        /// Machine login (email + password):
        ///   1. <see cref="MachineCredentialStore"/> — received from cloud pairing (preferred).
        ///   2. MachineEmail/MachinePassword in help.json — older release zips only.
        ///      New zips no longer carry them (they were public), so a fresh install
        ///      gets Bantuan cloud features after "Daftarkan dari cloud".
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
                string storeId = ReadStringOrDefault(root, "StoreId", "");
                string registerId = ReadStringOrDefault(root, "RegisterId", "");

                string machineEmail;
                string machinePassword;
                var paired = MachineCredentialStore.TryLoad();
                if (paired != null)
                {
                    machineEmail = paired.Email;
                    machinePassword = paired.Password;
                    if (!string.IsNullOrWhiteSpace(paired.StoreId)) storeId = paired.StoreId;
                    if (!string.IsNullOrWhiteSpace(paired.RegisterId)) registerId = paired.RegisterId;
                }
                else
                {
                    machineEmail = ReadString(root, "MachineEmail");
                    machinePassword = ReadString(root, "MachinePassword");
                }

                if (string.IsNullOrWhiteSpace(supabaseUrl)
                    || string.IsNullOrWhiteSpace(anonKey)
                    || string.IsNullOrWhiteSpace(machineEmail)
                    || string.IsNullOrWhiteSpace(machinePassword))
                {
                    Console.Error.WriteLine("[HelpConfig] missing required field(s): server config in help.json or machine login (pair this register via 'Daftarkan dari cloud')");
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
        /// Reads one optional string key (e.g. "DashboardUrl") from the same help.json
        /// TryLoad uses. Returns null when the file or key is missing. NEVER throws.
        /// </summary>
        public static string? TryReadOptional(string key)
        {
            try
            {
                string? path = ResolveExistingPath();
                if (path is null) return null;
                using var doc = JsonDocument.Parse(File.ReadAllText(path));
                string value = ReadString(doc.RootElement, key);
                return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
            }
            catch
            {
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
        /// Path next to the running executable. release.yml writes help.json into
        /// the publish dir with PUBLIC fields only (server URL, anon key, store id);
        /// machine credentials arrive via cloud pairing (MachineCredentialStore).
        /// </summary>
        public static string ResolveExePath()
        {
            return Path.Combine(AppContext.BaseDirectory, "help.json");
        }

        /// <summary>
        /// Returns the first existing path from the search order, or null.
        /// </summary>
        /// <summary>Test hook: when set, help.json is read only from this path.</summary>
        public static string? HelpJsonPathOverride { get; set; }

        private static string? ResolveExistingPath()
        {
            if (!string.IsNullOrEmpty(HelpJsonPathOverride))
                return File.Exists(HelpJsonPathOverride) ? HelpJsonPathOverride : null;
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
