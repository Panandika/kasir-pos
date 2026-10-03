#nullable enable
using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Kasir.Help.Auth
{
    /// <summary>
    /// Bantuan machine login for this register, received from cloud pairing
    /// (register-pair returns a fresh password once).
    /// </summary>
    public sealed record MachineCredentials(string Email, string Password, string StoreId, string RegisterId);

    /// <summary>
    /// Local, encrypted home for the register's Bantuan machine credentials.
    /// Replaces MachineEmail/MachinePassword baked into help.json in the PUBLIC
    /// release zips.
    ///
    /// Windows: %APPDATA%\Kasir\machine-credentials.dat, DPAPI CurrentUser — the
    /// same scope SupabaseMachineAuth already uses for auth.dat. CurrentUser (not
    /// LocalMachine) so another Windows account or process running as a different
    /// user on the PC cannot decrypt it; the POS always runs as the cashier's
    /// Windows user. If the POS is ever run under a different account, Bantuan
    /// falls back to help.json (if any) until the register is paired again.
    ///
    /// Other OS (dev/macOS/Linux): ~/.kasir/machine-credentials.json, mode 0600.
    ///
    /// Never throws; never logs the password.
    /// </summary>
    public static class MachineCredentialStore
    {
        private const string WindowsFileName = "machine-credentials.dat";
        private const string OtherFileName = "machine-credentials.json";

        /// <summary>Test hook: overrides the storage path (plain JSON, no DPAPI).</summary>
        public static string? PathOverride { get; set; }

        public static string ResolvePath()
        {
            if (!string.IsNullOrEmpty(PathOverride)) return PathOverride!;
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
                return Path.Combine(appData, "Kasir", WindowsFileName);
            }
            string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            return Path.Combine(home, ".kasir", OtherFileName);
        }

        private static bool UseDpapi =>
            string.IsNullOrEmpty(PathOverride) && RuntimeInformation.IsOSPlatform(OSPlatform.Windows);

        public static bool TrySave(MachineCredentials credentials)
        {
            try
            {
                if (credentials == null
                    || string.IsNullOrWhiteSpace(credentials.Email)
                    || string.IsNullOrWhiteSpace(credentials.Password))
                {
                    return false;
                }

                string path = ResolvePath();
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                byte[] plain = JsonSerializer.SerializeToUtf8Bytes(new Dto
                {
                    Email = credentials.Email,
                    Password = credentials.Password,
                    StoreId = credentials.StoreId ?? "",
                    RegisterId = credentials.RegisterId ?? "",
                });

                if (UseDpapi)
                {
#pragma warning disable CA1416 // guarded by UseDpapi (Windows only)
                    byte[] enc = ProtectedData.Protect(plain, null, DataProtectionScope.CurrentUser);
#pragma warning restore CA1416
                    File.WriteAllBytes(path, enc);
                }
                else
                {
                    File.WriteAllBytes(path, plain);
                    if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                    {
                        File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
                    }
                }
                return true;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[MachineCredentialStore] save failed: {ex.GetType().Name}: {ex.Message}");
                return false;
            }
        }

        public static MachineCredentials? TryLoad()
        {
            try
            {
                string path = ResolvePath();
                if (!File.Exists(path)) return null;
                byte[] data = File.ReadAllBytes(path);
                if (UseDpapi)
                {
#pragma warning disable CA1416
                    data = ProtectedData.Unprotect(data, null, DataProtectionScope.CurrentUser);
#pragma warning restore CA1416
                }
                var dto = JsonSerializer.Deserialize<Dto>(data);
                if (dto == null
                    || string.IsNullOrWhiteSpace(dto.Email)
                    || string.IsNullOrWhiteSpace(dto.Password))
                {
                    return null;
                }
                return new MachineCredentials(dto.Email!, dto.Password!, dto.StoreId ?? "", dto.RegisterId ?? "");
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[MachineCredentialStore] load failed: {ex.GetType().Name}: {ex.Message}");
                return null;
            }
        }

        public static void TryDelete()
        {
            try
            {
                string path = ResolvePath();
                if (File.Exists(path)) File.Delete(path);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[MachineCredentialStore] delete failed: {ex.GetType().Name}: {ex.Message}");
            }
        }

        private sealed class Dto
        {
            public string? Email { get; set; }
            public string? Password { get; set; }
            public string? StoreId { get; set; }
            public string? RegisterId { get; set; }
        }
    }
}
