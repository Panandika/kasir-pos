#nullable enable
using System;
using System.IO;

namespace Kasir.Security
{
    /// <summary>
    /// Reads/writes a small secret file through an <see cref="ISecretProtector"/>.
    ///
    /// Writes are atomic (temp file + rename) so a crash or power cut mid-write never
    /// leaves a half-written credential file behind. On non-Windows the file is
    /// created with mode 0600 (owner read/write only). On Windows the file inherits
    /// the ACL of its folder (%APPDATA% / %LOCALAPPDATA% are already user-only).
    ///
    /// These methods throw on failure; callers decide how to log/fallback.
    /// </summary>
    public static class ProtectedFile
    {
        private const UnixFileMode OwnerOnly = UnixFileMode.UserRead | UnixFileMode.UserWrite;

        public static void Write(string path, byte[] plain, ISecretProtector protector)
        {
            if (path == null) throw new ArgumentNullException(nameof(path));
            if (plain == null) throw new ArgumentNullException(nameof(plain));
            if (protector == null) throw new ArgumentNullException(nameof(protector));

            byte[] stored = protector.Protect(plain);

            string? dir = Path.GetDirectoryName(Path.GetFullPath(path));
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

            // Unique temp name: the POS and the CloudSync worker may write concurrently.
            string tmp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";

            var options = new FileStreamOptions
            {
                Mode = FileMode.CreateNew,
                Access = FileAccess.Write,
                Share = FileShare.None,
            };
            if (!OperatingSystem.IsWindows()) options.UnixCreateMode = OwnerOnly;

            try
            {
                using (var fs = new FileStream(tmp, options))
                {
                    fs.Write(stored, 0, stored.Length);
                    fs.Flush(flushToDisk: true);
                }
                File.Move(tmp, path, overwrite: true);
            }
            catch
            {
                try { if (File.Exists(tmp)) File.Delete(tmp); } catch { /* best effort */ }
                throw;
            }

            // Enforce 0600 even when replacing a file that was created with a wider mode.
            if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(path, OwnerOnly);
        }

        public static byte[] Read(string path, ISecretProtector protector)
        {
            if (protector == null) throw new ArgumentNullException(nameof(protector));
            return protector.Unprotect(File.ReadAllBytes(path));
        }
    }
}
