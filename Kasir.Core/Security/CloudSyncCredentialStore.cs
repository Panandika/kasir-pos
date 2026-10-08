#nullable enable
using System;
using System.IO;
using System.Text.Json;

namespace Kasir.Security
{
    /// <summary>Supabase Postgres (pooler) credentials for cloud sync.</summary>
    public sealed class CloudSyncCreds
    {
        public string Host { get; set; } = "";
        public int Port { get; set; } = 6543;
        public string Database { get; set; } = "postgres";
        public string Username { get; set; } = "";
        public string Password { get; set; } = "";
    }

    /// <summary>
    /// Local, encrypted home for the cloud sync credentials entered on
    /// F8 Admin -> Cloud Sync. Read by the POS (CloudSyncStatusModel, setup screen)
    /// and by the Kasir.CloudSync worker as its lowest-priority config source.
    ///
    /// Windows: %LOCALAPPDATA%\Kasir\cloudsync.dat, DPAPI CurrentUser, no entropy
    /// (see <see cref="DpapiCurrentUserProtector"/> for where the key lives).
    /// Other OS (dev): ~/Library/Application Support/Kasir/cloudsync.dat (macOS) or
    /// ~/.local/share/Kasir/cloudsync.dat (Linux), plain JSON, mode 0600.
    ///
    /// Up to v2.10.0 the same data was written as plaintext JSON to
    /// <see cref="LegacyPlaintextFileName"/> in the same folder. <see cref="TryLoad"/>
    /// migrates it transparently: read plaintext -> write encrypted -> read back and
    /// compare -> delete plaintext. If any step fails the plaintext is kept (and
    /// still used) so credentials are never lost.
    ///
    /// Never throws; never logs the password.
    /// </summary>
    public static class CloudSyncCredentialStore
    {
        public const string FileName = "cloudsync.dat";
        public const string LegacyPlaintextFileName = "cloudsync.json";

        /// <summary>Test hook: folder holding both files.</summary>
        public static string? DirectoryOverride { get; set; }

        /// <summary>Test hook: replaces the platform protector (DPAPI / plain).</summary>
        public static ISecretProtector? ProtectorOverride { get; set; }

        public static string DirectoryPath =>
            !string.IsNullOrEmpty(DirectoryOverride)
                ? DirectoryOverride!
                : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Kasir");

        public static string FilePath => Path.Combine(DirectoryPath, FileName);

        public static string LegacyPlaintextPath => Path.Combine(DirectoryPath, LegacyPlaintextFileName);

        /// <summary>True when the stored file is encrypted (Windows DPAPI).</summary>
        public static bool IsEncrypted => Protector.Encrypts;

        private static ISecretProtector Protector => ProtectorOverride ?? SecretProtectors.PlatformDefault;

        // Serialises load/migrate/save inside one process (UI thread + status timers).
        private static readonly object Gate = new object();

        public static CloudSyncCreds? TryLoad()
        {
            lock (Gate) return TryLoadCore();
        }

        private static CloudSyncCreds? TryLoadCore()
        {
            try
            {
                string path = FilePath;
                string legacy = LegacyPlaintextPath;
                bool hasProtected = File.Exists(path);
                bool hasLegacy = File.Exists(legacy);

                CloudSyncCreds? current = hasProtected ? TryReadProtected(path) : null;

                // A plaintext file newer than the encrypted one was written by an older
                // build after migration (rollback + re-entry): it is the latest truth.
                bool legacyIsNewer = hasLegacy && current != null
                    && File.GetLastWriteTimeUtc(legacy) > File.GetLastWriteTimeUtc(path);

                if (current != null && !legacyIsNewer)
                {
                    if (hasLegacy) TryDeleteFile(legacy, "stale plaintext");
                    return current;
                }

                if (!hasLegacy) return current; // null when missing or unreadable

                CloudSyncCreds? plain = TryReadLegacy(legacy);
                if (plain == null) return current;

                MigrateLegacy(plain, legacy);
                return plain;
            }
            catch (Exception ex)
            {
                Log($"load failed: {Describe(ex)}");
                return null;
            }
        }

        public static bool TrySave(CloudSyncCreds creds)
        {
            lock (Gate) return TrySaveCore(creds);
        }

        private static bool TrySaveCore(CloudSyncCreds creds)
        {
            try
            {
                if (creds == null) return false;
                ProtectedFile.Write(FilePath, Serialize(creds), Protector);
                // The encrypted file is now the source of truth; drop any old plaintext.
                if (File.Exists(LegacyPlaintextPath)) TryDeleteFile(LegacyPlaintextPath, "plaintext");
                return true;
            }
            catch (Exception ex)
            {
                Log($"save failed: {Describe(ex)}");
                return false;
            }
        }

        public static void TryDelete()
        {
            lock (Gate)
            {
                TryDeleteFile(FilePath, "encrypted");
                TryDeleteFile(LegacyPlaintextPath, "plaintext");
            }
        }

        private static void MigrateLegacy(CloudSyncCreds plain, string legacy)
        {
            try
            {
                ProtectedFile.Write(FilePath, Serialize(plain), Protector);
                CloudSyncCreds? check = TryReadProtected(FilePath);
                if (check == null || !Same(check, plain))
                {
                    Log("migration verify failed; keeping plaintext " + legacy);
                    return;
                }
                if (TryDeleteFile(legacy, "plaintext"))
                {
                    Log($"migrated plaintext credentials to {(Protector.Encrypts ? "encrypted" : "owner-only")} {FilePath}");
                }
            }
            catch (Exception ex)
            {
                Log($"migration failed, keeping plaintext {legacy}: {Describe(ex)}");
            }
        }

        private static CloudSyncCreds? TryReadProtected(string path)
        {
            try
            {
                return JsonSerializer.Deserialize<CloudSyncCreds>(ProtectedFile.Read(path, Protector));
            }
            catch (Exception ex)
            {
                // Typical on Windows: CryptographicException when the file was copied from
                // another PC / another Windows user, or after an admin password reset.
                Log($"cannot read {path}: {Describe(ex)}");
                return null;
            }
        }

        private static CloudSyncCreds? TryReadLegacy(string path)
        {
            try
            {
                return JsonSerializer.Deserialize<CloudSyncCreds>(File.ReadAllText(path));
            }
            catch (Exception ex)
            {
                Log($"cannot read plaintext {path}: {Describe(ex)}");
                return null;
            }
        }

        private static bool TryDeleteFile(string path, string what)
        {
            try
            {
                if (File.Exists(path)) File.Delete(path);
                return true;
            }
            catch (Exception ex)
            {
                Log($"delete {what} {path} failed: {Describe(ex)}");
                return false;
            }
        }

        private static byte[] Serialize(CloudSyncCreds creds) =>
            JsonSerializer.SerializeToUtf8Bytes(creds, new JsonSerializerOptions { WriteIndented = true });

        private static bool Same(CloudSyncCreds a, CloudSyncCreds b) =>
            a.Host == b.Host && a.Port == b.Port && a.Database == b.Database
            && a.Username == b.Username && a.Password == b.Password;

        // JsonException messages can quote fragments of the file (i.e. a password).
        private static string Describe(Exception ex) =>
            ex is JsonException ? "JsonException (file is not valid credentials JSON)" : $"{ex.GetType().Name}: {ex.Message}";

        private static void Log(string message) =>
            Console.Error.WriteLine("[CloudSyncCredentialStore] " + message);
    }
}
