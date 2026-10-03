using System;
using System.Text.RegularExpressions;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using Kasir.Data.Repositories;
using Kasir.Utils;

namespace Kasir.Services
{
    public class UpdateCheckResult
    {
        public bool Available { get; set; }
        public string CurrentVersion { get; set; }
        public string NewVersion { get; set; }
        /// <summary>"What's new" text from the GitHub release (install instructions stripped).</summary>
        public string ReleaseNotes { get; set; }
        public string AssetName { get; set; }
        public string AssetUrl { get; set; }
        public long AssetSize { get; set; }
        public string Error { get; set; }
    }

    public class UpdatePrepareResult
    {
        public bool Success { get; set; }
        public string Error { get; set; }
    }

    /// <summary>
    /// Self-update straight from GitHub Releases (Panandika/kasir-pos):
    ///   1. CheckForUpdateAsync — latest release vs AppVersion.Current, pick this
    ///      register's zip.
    ///   2. DownloadAndPrepareAsync — download into update-staging\, extract, verify the
    ///      ECDSA signature over checksum.sha256 and every file hash (no unsigned or
    ///      unlisted file is ever installed).
    ///   3. WalCheckpoint + ApplyUpdate — start the staged build with --apply-update
    ///      (see UpdateApplier) and exit; it swaps the files and relaunches the POS.
    /// The old hub / LAN-share / shared-HMAC update path was removed: releases were never
    /// signed with a key the registers shared, so it could not work.
    /// </summary>
    public class UpdateService
    {
        private readonly ConfigRepository _configRepo; // null before the DB exists (first run)
        private readonly IFileSystem _fs;
        private readonly IReleaseSource _source;
        private readonly int _timeoutMs;
        private readonly SqliteConnection _db;

        /// <summary>Overridable for tests; production uses the embedded release key.</summary>
        public string PublicKeyPem { get; set; } = UpdateSignature.PublicKeyPem;

        /// <summary>Install folder. Overridable for tests.</summary>
        public string BaseDirectory { get; set; } = AppDomain.CurrentDomain.BaseDirectory;

        public UpdateService(SqliteConnection db)
            : this(db, new FileSystemImpl(), new GitHubReleaseClient(), 30000)
        {
        }

        public UpdateService(SqliteConnection db, IFileSystem fs, IReleaseSource source, int timeoutMs)
        {
            _db = db;
            _configRepo = db != null ? new ConfigRepository(db) : null;
            _fs = fs;
            _source = source;
            _timeoutMs = timeoutMs;
        }

        /// <summary>
        /// Host-supplied callback to exit the application after starting the update
        /// (Avalonia: ApplicationLifetime.Shutdown()).
        /// </summary>
        public Action ExitAction { get; set; }

        public string GetStagingPath() => Path.Combine(BaseDirectory, UpdateApplier.StagingDir);

        public string GetStagedAppPath() => Path.Combine(GetStagingPath(), "app");

        // ── 1. Check ─────────────────────────────────────────────────────────

        public async Task<UpdateCheckResult> CheckForUpdateAsync(CancellationToken ct = default(CancellationToken))
        {
            string current = AppVersion.Current;
            var result = new UpdateCheckResult { CurrentVersion = current };

            using (var cts = CancellationTokenSource.CreateLinkedTokenSource(ct))
            {
                cts.CancelAfter(_timeoutMs);
                try
                {
                    GitHubRelease release = await _source.GetLatestAsync(cts.Token).ConfigureAwait(false);
                    result.NewVersion = release.Version;
                    result.ReleaseNotes = ExtractReleaseNotes(release.Body);
                    result.Available = AppVersion.IsNewerThan(release.Version, current);

                    if (result.Available)
                    {
                        var asset = SelectAsset(release, _configRepo?.Get("register_id"));
                        if (asset == null)
                        {
                            result.Available = false;
                            result.Error = string.Format(UpdateMessages.NoAsset, release.Version);
                            return result;
                        }
                        result.AssetName = asset.Name;
                        result.AssetUrl = asset.DownloadUrl;
                        result.AssetSize = asset.Size;
                    }

                    try { _configRepo?.Set("last_update_check", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")); }
                    catch { /* not critical */ }
                }
                catch (UpdateSourceException ex)
                {
                    result.Available = false;
                    result.Error = ex.Message;
                }
                catch (OperationCanceledException)
                {
                    result.Available = false;
                    result.Error = UpdateMessages.Timeout;
                }
            }
            return result;
        }

        /// <summary>
        /// Picks kasir-{version}-register-{NN}.zip for this register (config register_id,
        /// "KLR-02" or "02"), falling back to register-01: since release zips carry only
        /// public settings, any of them works.
        /// </summary>
        public static GitHubReleaseAsset SelectAsset(GitHubRelease release, string registerId)
        {
            if (release?.Assets == null || release.Assets.Count == 0) return null;
            string nn = RegisterNumber(registerId) ?? "01";
            string prefix = "kasir-" + release.Version + "-register-";

            GitHubReleaseAsset Find(string n) => release.Assets.FirstOrDefault(a =>
                string.Equals(a.Name, prefix + n + ".zip", StringComparison.OrdinalIgnoreCase)
                && !string.IsNullOrEmpty(a.DownloadUrl));

            return Find(nn) ?? Find("01")
                ?? release.Assets.FirstOrDefault(a =>
                    a.Name != null && a.Name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
                    && a.Name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)
                    && !string.IsNullOrEmpty(a.DownloadUrl));
        }

        private static string RegisterNumber(string registerId)
        {
            if (string.IsNullOrWhiteSpace(registerId)) return null;
            string digits = new string(registerId.Where(char.IsDigit).ToArray());
            if (digits.Length == 0 || !int.TryParse(digits, out int n)) return null;
            return n.ToString("00");
        }

        /// <summary>
        /// "What's new" text for the update screen. A release may carry hand-written
        /// cashier notes between &lt;!-- kasir-notes --&gt; markers; those win. Otherwise
        /// release-please's changelog is cleaned up: version header, Indonesian section
        /// names, no commit links/markdown, duplicates removed, install part dropped.
        /// </summary>
        public static string ExtractReleaseNotes(string body)
        {
            if (string.IsNullOrWhiteSpace(body)) return "";
            string text = body.Replace("\r\n", "\n");

            const string open = "<!-- kasir-notes -->", close = "<!-- /kasir-notes -->";
            int o = text.IndexOf(open, StringComparison.OrdinalIgnoreCase);
            int c = o >= 0 ? text.IndexOf(close, o, StringComparison.OrdinalIgnoreCase) : -1;
            if (o >= 0 && c > o) return text.Substring(o + open.Length, c - o - open.Length).Trim();

            int cut = text.IndexOf("## Cara Install", StringComparison.OrdinalIgnoreCase);
            if (cut >= 0) text = text.Substring(0, cut);

            var lines = new List<string>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var raw in text.Split('\n'))
            {
                string line = raw.Trim();
                if (line.Length == 0 || line == "---") continue;
                var header = Regex.Match(line, @"^#{1,2}\s*\[?([0-9][0-9.]*)\]?(?:\([^)]*\))?\s*(?:\(([^)]*)\))?");
                if (header.Success && line.StartsWith("#") && !line.StartsWith("###"))
                {
                    string date = header.Groups[2].Success ? " (" + header.Groups[2].Value + ")" : "";
                    lines.Add("Versi " + header.Groups[1].Value + date);
                    continue;
                }
                if (line.StartsWith("###"))
                {
                    string section = line.TrimStart('#').Trim();
                    string id = section.Equals("Features", StringComparison.OrdinalIgnoreCase) ? "Fitur baru"
                              : section.Equals("Bug Fixes", StringComparison.OrdinalIgnoreCase) ? "Perbaikan"
                              : section.Equals("Performance Improvements", StringComparison.OrdinalIgnoreCase) ? "Lebih cepat"
                              : section;
                    lines.Add("");
                    lines.Add(id + ":");
                    continue;
                }
                if (line.StartsWith("* ") || line.StartsWith("- "))
                {
                    string item = line.Substring(2);
                    item = Regex.Replace(item, @"\s*\(\[[0-9a-f]{6,40}\]\([^)]*\)\)", "");   // ([sha](url))
                    item = Regex.Replace(item, @"^\*\*[^*]+:\*\*\s*", "");                   // **scope:**
                    item = Regex.Replace(item, @"\[([^\]]+)\]\([^)]*\)", "$1");                // [text](url)
                    item = item.Replace("**", "").Trim();
                    if (item.Length == 0 || !seen.Add(item)) continue;
                    lines.Add("\u2022 " + item);
                    continue;
                }
                lines.Add(line.Replace("**", ""));
            }
            return string.Join("\n", lines).Trim();
        }

        // ── 2. Download + verify ─────────────────────────────────────────────

        public async Task<UpdatePrepareResult> DownloadAndPrepareAsync(
            UpdateCheckResult check, IProgress<int> downloadPercent, CancellationToken ct)
        {
            var result = new UpdatePrepareResult();
            if (check == null || !check.Available || string.IsNullOrEmpty(check.AssetUrl))
            {
                result.Error = UpdateMessages.NothingToInstall;
                return result;
            }

            string staging = GetStagingPath();
            string appDir = GetStagedAppPath();
            try
            {
                if (_fs.DirectoryExists(staging)) _fs.DeleteDirectory(staging, true);
                _fs.CreateDirectory(staging);

                // zip + extracted copy + backup of the replaced files ≈ 4× the zip.
                long required = Math.Max(check.AssetSize, 1) * 4;
                long free = _fs.GetAvailableDiskSpace(BaseDirectory);
                if (free < required)
                {
                    result.Error = string.Format(UpdateMessages.InsufficientDisk,
                        required / (1024 * 1024), free / (1024 * 1024));
                    return result;
                }

                string zipPath = Path.Combine(staging, "download.zip");
                await _source.DownloadAsync(check.AssetUrl, zipPath, downloadPercent, ct).ConfigureAwait(false);

                try
                {
                    // ExtractToDirectory refuses entries that would escape appDir (zip-slip).
                    ZipFile.ExtractToDirectory(zipPath, appDir);
                }
                catch (InvalidDataException)
                {
                    result.Error = UpdateMessages.ZipInvalid;
                    Cleanup(staging);
                    return result;
                }
                File.Delete(zipPath);

                if (!VerifySignature(appDir))
                {
                    result.Error = UpdateMessages.SignatureFailed;
                    Cleanup(staging);
                    return result;
                }
                if (!VerifyChecksums(appDir))
                {
                    result.Error = UpdateMessages.ChecksumFailed;
                    Cleanup(staging);
                    return result;
                }

                // version.txt is covered by the signed manifest: the package must be the
                // version that was announced (no replay of an older signed release).
                string versionFile = Path.Combine(appDir, "version.txt");
                string pkgVersion = _fs.FileExists(versionFile) ? _fs.ReadAllText(versionFile).Trim() : "";
                if (!string.Equals(pkgVersion, check.NewVersion, StringComparison.OrdinalIgnoreCase))
                {
                    result.Error = string.Format(UpdateMessages.VersionMismatch, pkgVersion, check.NewVersion);
                    Cleanup(staging);
                    return result;
                }

                result.Success = true;
            }
            catch (UpdateSourceException ex)
            {
                result.Error = ex.Message;
                Cleanup(staging);
            }
            catch (OperationCanceledException)
            {
                result.Error = UpdateMessages.Cancelled;
                Cleanup(staging);
            }
            catch (Exception ex)
            {
                result.Error = UpdateMessages.PrepareFailed + " (" + ex.Message + ")";
                Cleanup(staging);
            }
            return result;
        }

        private void Cleanup(string staging)
        {
            try { if (_fs.DirectoryExists(staging)) _fs.DeleteDirectory(staging, true); }
            catch { /* best effort */ }
        }

        /// <summary>
        /// checksum.sha256 must carry a valid release signature (checksum.sha256.sig).
        /// Missing either file = unsigned package = refused.
        /// </summary>
        public bool VerifySignature(string directory)
        {
            string checksumFile = Path.Combine(directory, UpdateSignature.ChecksumFileName);
            string sigFile = Path.Combine(directory, UpdateSignature.SignatureFileName);
            if (!_fs.FileExists(checksumFile) || !_fs.FileExists(sigFile)) return false;
            return UpdateSignature.Verify(_fs.ReadAllBytes(checksumFile), _fs.ReadAllText(sigFile), PublicKeyPem);
        }

        /// <summary>
        /// Every file listed in checksum.sha256 must exist with that hash, and every file
        /// in the package must be listed (F23: an unlisted planted file would otherwise be
        /// installed unverified). Only the manifest and its signature are exempt.
        /// </summary>
        public bool VerifyChecksums(string directory)
        {
            string checksumFile = Path.Combine(directory, UpdateSignature.ChecksumFileName);
            if (!_fs.FileExists(checksumFile)) return false;

            string content = _fs.ReadAllText(checksumFile);
            string[] lines = content.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);

            var listed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string line in lines)
            {
                // Format: "hash  relative/path" (two spaces)
                int sep = line.IndexOf("  ", StringComparison.Ordinal);
                if (sep < 0) continue;

                string expectedHash = line.Substring(0, sep).Trim();
                string fileName = line.Substring(sep + 2).Trim();
                string rel = NormalizeRelPath(fileName);
                if (rel.Contains("..")) return false;
                listed.Add(rel);

                string filePath = Path.Combine(directory, rel);
                if (!_fs.FileExists(filePath)) return false;

                string actualHash = ComputeFileSha256(_fs.ReadAllBytes(filePath));
                if (!string.Equals(actualHash, expectedHash, StringComparison.OrdinalIgnoreCase)) return false;
            }
            if (listed.Count == 0) return false;

            var exempt = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                UpdateSignature.ChecksumFileName,
                UpdateSignature.SignatureFileName
            };
            foreach (string full in _fs.GetFiles(directory, "*", true))
            {
                string rel = RelativePathUnder(directory, full);
                if (exempt.Contains(rel)) continue;
                if (!listed.Contains(rel)) return false; // unlisted planted file
            }
            return true;
        }

        private static string NormalizeRelPath(string p) => p.Replace('\\', '/').TrimStart('/');

        private static string RelativePathUnder(string baseDir, string fullPath)
        {
            string b = baseDir.Replace('\\', '/').TrimEnd('/');
            string f = fullPath.Replace('\\', '/');
            if (f.Length > b.Length && f.StartsWith(b + "/", StringComparison.OrdinalIgnoreCase))
                return f.Substring(b.Length + 1);
            return Path.GetFileName(fullPath);
        }

        // ── 3. Apply ─────────────────────────────────────────────────────────

        public bool WalCheckpoint()
        {
            if (_db == null) return true;
            try
            {
                using (var cmd = _db.CreateCommand())
                {
                    cmd.CommandText = "PRAGMA wal_checkpoint(TRUNCATE);";
                    cmd.ExecuteNonQuery();
                }
                return true;
            }
            catch (Exception ex)
            {
                Debug.WriteLine("WalCheckpoint failed: " + ex.Message);
                return false;
            }
        }

        /// <summary>
        /// Starts the staged (new) build in --apply-update mode, then exits this POS so
        /// its files can be replaced. The staged build waits for this process to exit.
        /// </summary>
        public void ApplyUpdate()
        {
            string exeName = Path.GetFileName(Environment.ProcessPath ?? "Kasir.Avalonia.exe");
            string stagedExe = Path.Combine(GetStagedAppPath(), exeName);
            if (!File.Exists(stagedExe))
                throw new InvalidOperationException(string.Format(UpdateMessages.StagedExeMissing, exeName));

            var psi = new ProcessStartInfo(stagedExe)
            {
                UseShellExecute = false,
                WorkingDirectory = GetStagedAppPath(),
            };
            psi.ArgumentList.Add(UpdateApplier.ApplyFlag);
            psi.ArgumentList.Add("--source");
            psi.ArgumentList.Add(GetStagedAppPath());
            psi.ArgumentList.Add("--target");
            psi.ArgumentList.Add(BaseDirectory.TrimEnd('\\', '/'));
            psi.ArgumentList.Add("--pid");
            psi.ArgumentList.Add(Environment.ProcessId.ToString());
            psi.ArgumentList.Add("--exe");
            psi.ArgumentList.Add(exeName);

            Process.Start(psi);
            ExitAction?.Invoke();
        }

        public static string ComputeFileSha256(string filePath) => ComputeFileSha256(File.ReadAllBytes(filePath));

        public static string ComputeFileSha256(byte[] content)
        {
            using (var sha = SHA256.Create())
                return BitConverter.ToString(sha.ComputeHash(content)).Replace("-", "").ToLowerInvariant();
        }
    }
}
