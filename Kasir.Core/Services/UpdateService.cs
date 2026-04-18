using System;
using System.Data.SQLite;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Kasir.Data.Repositories;
using Kasir.Utils;

namespace Kasir.Services
{
    public class UpdateCheckResult
    {
        public bool Available { get; set; }
        public string CurrentVersion { get; set; }
        public string NewVersion { get; set; }
        public string Error { get; set; }
    }

    public class UpdatePrepareResult
    {
        public bool Success { get; set; }
        public string Error { get; set; }
    }

    public class UpdateService
    {
        private readonly ConfigRepository _configRepo;
        private readonly SQLiteConnection _db;
        private readonly IFileSystem _fs;
        private readonly int _timeoutMs;

        public UpdateService(SQLiteConnection db) : this(db, new FileSystemImpl(), 15000)
        {
        }

        public UpdateService(SQLiteConnection db, IFileSystem fs, int timeoutMs)
        {
            _db = db;
            _configRepo = new ConfigRepository(db);
            _fs = fs;
            _timeoutMs = timeoutMs;
        }

        /// <summary>
        /// Host-supplied callback to exit the application after launching the updater.
        /// WinForms hosts wire this to Application.Exit; Avalonia hosts wire this to
        /// ApplicationLifetime.Shutdown().
        /// </summary>
        public Action ExitAction { get; set; }

        public string GetUpdateSharePath()
        {
            return _configRepo.Get("update_share") ?? "";
        }

        public string GetStagingPath()
        {
            return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "update-staging");
        }

        public Task<UpdateCheckResult> CheckForUpdateAsync()
        {
            var workTask = Task.Run(() => CheckForUpdate());
            var timeoutTask = Task.Delay(_timeoutMs).ContinueWith(_ =>
                new UpdateCheckResult
                {
                    CurrentVersion = AppVersion.Current,
                    Error = UpdateMessages.Unreachable
                });
            return Task.WhenAny(workTask, timeoutTask)
                .ContinueWith(t => t.Result.Result);
        }

        public UpdateCheckResult CheckForUpdate()
        {
            string currentVersion = AppVersion.Current;
            var result = new UpdateCheckResult { CurrentVersion = currentVersion };

            try
            {
                string sharePath = GetUpdateSharePath();
                if (string.IsNullOrEmpty(sharePath))
                {
                    result.Error = UpdateMessages.Unreachable;
                    return result;
                }

                string versionFile = Path.Combine(sharePath, "version.txt");
                if (!_fs.FileExists(versionFile))
                {
                    result.Error = UpdateMessages.Unreachable;
                    return result;
                }

                string newVersion = _fs.ReadAllText(versionFile).Trim();
                result.NewVersion = newVersion;

                if (AppVersion.IsNewerThan(newVersion, currentVersion))
                {
                    result.Available = true;
                }

                _configRepo.Set("last_update_check", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
            }
            catch (Exception ex)
            {
                result.Error = UpdateMessages.Unreachable + " (" + ex.Message + ")";
            }

            return result;
        }

        public string GetPatchNotes()
        {
            try
            {
                string sharePath = GetUpdateSharePath();
                string notesFile = Path.Combine(sharePath, "whatsnew.txt");
                if (_fs.FileExists(notesFile))
                {
                    return _fs.ReadAllText(notesFile);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("GetPatchNotes failed: " + ex.Message);
            }
            return "";
        }

        public UpdatePrepareResult PrepareUpdate()
        {
            var result = new UpdatePrepareResult();

            try
            {
                string sharePath = GetUpdateSharePath();
                string stagingPath = GetStagingPath();

                // Clean previous staging
                if (_fs.DirectoryExists(stagingPath))
                {
                    _fs.DeleteDirectory(stagingPath, true);
                }
                _fs.CreateDirectory(stagingPath);

                // Verify HMAC on checksum file
                if (!VerifyChecksumHmac(sharePath))
                {
                    result.Error = UpdateMessages.HmacFailed;
                    return result;
                }

                // Copy all files from share to staging
                CopyDirectory(sharePath, stagingPath);

                // Verify SHA256 checksums
                if (!VerifyChecksums(stagingPath))
                {
                    result.Error = UpdateMessages.ChecksumFailed;
                    // Clean up
                    _fs.DeleteDirectory(stagingPath, true);
                    return result;
                }

                // Check disk space
                long stagingSize = GetDirectorySize(stagingPath);
                long requiredSpace = stagingSize * 3;
                long freeSpace = _fs.GetAvailableDiskSpace(AppDomain.CurrentDomain.BaseDirectory);
                if (freeSpace < requiredSpace)
                {
                    result.Error = string.Format(UpdateMessages.InsufficientDisk,
                        requiredSpace / (1024 * 1024), freeSpace / (1024 * 1024));
                    _fs.DeleteDirectory(stagingPath, true);
                    return result;
                }

                result.Success = true;
            }
            catch (Exception ex)
            {
                result.Error = ex.Message;
            }

            return result;
        }

        public bool WalCheckpoint()
        {
            try
            {
                using (var cmd = new SQLiteCommand(_db))
                {
                    cmd.CommandText = "PRAGMA wal_checkpoint(TRUNCATE);";
                    cmd.ExecuteNonQuery();
                }
                return true;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("SaveUpdateState failed: " + ex.Message);
                return false;
            }
        }

        public void ApplyUpdate()
        {
            string stagingPath = GetStagingPath();
            string targetPath = AppDomain.CurrentDomain.BaseDirectory;
            int pid = Process.GetCurrentProcess().Id;

            // Copy Updater.exe from staging to a temp location and launch from there
            string updaterSource = Path.Combine(stagingPath, "Updater.exe");
            string updaterTemp = Path.Combine(stagingPath, "_Updater_run.exe");

            if (_fs.FileExists(updaterSource))
            {
                _fs.CopyFile(updaterSource, updaterTemp, true);
            }
            else
            {
                // Fallback: use the existing Updater.exe
                string existingUpdater = Path.Combine(targetPath, "Updater.exe");
                _fs.CopyFile(existingUpdater, updaterTemp, true);
            }

            string args = string.Format("--source \"{0}\" --target \"{1}\" --pid {2}",
                stagingPath, targetPath.TrimEnd('\\'), pid);

            Process.Start(updaterTemp, args);
            ExitAction?.Invoke();
        }

        public bool VerifyChecksumHmac(string directory)
        {
            string checksumFile = Path.Combine(directory, "checksum.sha256");
            string hmacFile = Path.Combine(directory, "checksum.sha256.hmac");

            if (!_fs.FileExists(checksumFile) && !_fs.FileExists(hmacFile))
            {
                return false; // Neither file present — unsigned update, refuse to install
            }

            if (!_fs.FileExists(checksumFile) || !_fs.FileExists(hmacFile))
            {
                return false; // One exists without the other — tampered or incomplete
            }

            string hmacKey = _configRepo.Get("sync_hmac_key") ?? "default-hmac-key-change-me";

            if (hmacKey == "default-hmac-key-change-me")
            {
                return false; // HMAC key not configured — refuse to verify
            }

            string checksumContent = _fs.ReadAllText(checksumFile);
            string expectedHmac = _fs.ReadAllText(hmacFile).Trim();

            string actualHmac = ComputeHmac(checksumContent, hmacKey);
            return ConstantTimeHexEquals(actualHmac, expectedHmac);
        }

        public bool VerifyChecksums(string directory)
        {
            string checksumFile = Path.Combine(directory, "checksum.sha256");
            if (!_fs.FileExists(checksumFile))
            {
                return true; // No checksum file = skip verification
            }

            string content = _fs.ReadAllText(checksumFile);
            string[] lines = content.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);

            foreach (string line in lines)
            {
                // Format: "hash  filename" (two spaces between hash and name)
                int sepIndex = line.IndexOf("  ");
                if (sepIndex < 0) continue;

                string expectedHash = line.Substring(0, sepIndex).Trim();
                string fileName = line.Substring(sepIndex + 2).Trim();

                string filePath = Path.Combine(directory, fileName);
                if (!_fs.FileExists(filePath))
                {
                    return false;
                }

                string actualHash = ComputeFileSha256(filePath);
                if (!string.Equals(actualHash, expectedHash, StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }
            }

            return true;
        }

        public void PublishToShare(string zipPath)
        {
            string sharePath = GetUpdateSharePath();
            if (string.IsNullOrEmpty(sharePath))
            {
                throw new InvalidOperationException("update_share not configured");
            }

            // Extract to a temp dir first, then swap
            string tempExtract = sharePath + "_extracting";
            if (_fs.DirectoryExists(tempExtract))
            {
                _fs.DeleteDirectory(tempExtract, true);
            }

            ZipFile.ExtractToDirectory(zipPath, tempExtract);

            // Verify the extracted contents have HMAC
            if (!VerifyChecksumHmac(tempExtract))
            {
                _fs.DeleteDirectory(tempExtract, true);
                throw new InvalidOperationException(UpdateMessages.HmacFailed);
            }

            // Swap: delete old share contents, move new in
            if (_fs.DirectoryExists(sharePath))
            {
                _fs.DeleteDirectory(sharePath, true);
            }
            Directory.Move(tempExtract, sharePath);
        }

        public void RecoverFromFailedUpdate()
        {
            string basePath = AppDomain.CurrentDomain.BaseDirectory;
            string stateFile = Path.Combine(basePath, "update-state.txt");

            if (!_fs.FileExists(stateFile))
                return;

            string state = _fs.ReadAllText(stateFile).Trim();

            if (state == "COPY_IN_PROGRESS" || state == "BACKUP_COMPLETE")
            {
                string backupPath = Path.Combine(basePath, "backup");
                if (_fs.DirectoryExists(backupPath))
                {
                    // Restore from backup
                    string[] backupFiles = _fs.GetFiles(backupPath, "*.*", false);
                    foreach (string file in backupFiles)
                    {
                        string name = Path.GetFileName(file);
                        string dest = Path.Combine(basePath, name);
                        _fs.CopyFile(file, dest, true);
                    }

                    // Restore x86 subdir
                    string x86Backup = Path.Combine(backupPath, "x86");
                    if (_fs.DirectoryExists(x86Backup))
                    {
                        string x86Dir = Path.Combine(basePath, "x86");
                        _fs.CreateDirectory(x86Dir);
                        string[] x86Files = _fs.GetFiles(x86Backup, "*.*", false);
                        foreach (string file in x86Files)
                        {
                            string name = Path.GetFileName(file);
                            _fs.CopyFile(file, Path.Combine(x86Dir, name), true);
                        }
                    }
                }

                File.WriteAllText(stateFile, "ROLLED_BACK");
            }

            // Clean up state file for completed states
            if (state == "COPY_COMPLETE" || state == "ROLLED_BACK")
            {
                try { File.Delete(stateFile); }
                catch (Exception ex) { System.Diagnostics.Debug.WriteLine("Delete state file failed: " + ex.Message); }
            }
        }

        private void CopyDirectory(string source, string dest)
        {
            string[] files = _fs.GetFiles(source, "*.*", false);
            foreach (string file in files)
            {
                string name = Path.GetFileName(file);
                _fs.CopyFile(file, Path.Combine(dest, name), true);
            }

            string[] dirs = _fs.GetDirectories(source);
            foreach (string dir in dirs)
            {
                string dirName = Path.GetFileName(dir);
                if (dirName.Equals("data", StringComparison.OrdinalIgnoreCase))
                    continue;

                string destSubDir = Path.Combine(dest, dirName);
                _fs.CreateDirectory(destSubDir);
                CopyDirectory(dir, destSubDir);
            }
        }

        private long GetDirectorySize(string path)
        {
            long total = 0;
            string[] files = _fs.GetFiles(path, "*.*", true);
            foreach (string file in files)
            {
                total += _fs.GetFileSize(file);
            }
            return total;
        }

        public static string ComputeFileSha256(string filePath)
        {
            using (var sha = SHA256.Create())
            using (var stream = File.OpenRead(filePath))
            {
                byte[] hash = sha.ComputeHash(stream);
                return BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant();
            }
        }

        private static bool ConstantTimeHexEquals(string a, string b)
        {
            if (a == null || b == null) return false;
            if (a.Length != b.Length) return false;

            int diff = 0;
            for (int i = 0; i < a.Length; i++)
            {
                diff |= char.ToLowerInvariant(a[i]) ^ char.ToLowerInvariant(b[i]);
            }
            return diff == 0;
        }

        public static string ComputeHmac(string content, string key)
        {
            using (var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(key)))
            {
                byte[] hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(content));
                return BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant();
            }
        }
    }
}
