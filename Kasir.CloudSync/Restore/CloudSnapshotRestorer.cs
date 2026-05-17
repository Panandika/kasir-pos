using System;
using System.IO;
using System.Net.Http;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Kasir.Data;
using Microsoft.Data.Sqlite;

namespace Kasir.CloudSync.Restore
{
    // Downloads a fresh snapshot.db from Supabase Storage and atomically swaps
    // it into place at the target path.
    //
    // Flow:
    //   1. POST snapshot-download with bootstrap JWT -> JSON manifest
    //   2. Schema version gate
    //   3. Disk space pre-check (size * 1.1)
    //   4. GET signed_url -> temp file (with progress)
    //   5. SHA-256 verify
    //   6. PRAGMA integrity_check
    //   7. DatabaseValidator.Validate
    //   8. Atomic swap: mv kasir.db -> kasir.db.bak; mv temp -> kasir.db
    //   9. Rebuild FTS index
    //
    // On cancel / failure: temp deleted, no .bak swap, original untouched.
    //
    // PRD story: US-P3-1
    public class CloudSnapshotRestorer
    {
        public const int SupportedSchemaVersion = 1;
        public const long DiskSpaceSafetyMultiplier = 11; // 1.1x (denominator 10)

        public class RestoreProgress
        {
            public string Stage; // pair | manifest | downloading | verifying | swapping | done
            public long BytesDownloaded;
            public long TotalBytes;
            public string Message;
        }

        public class RestoreException : Exception
        {
            public string Stage { get; }
            public RestoreException(string stage, string message) : base(message)
            {
                Stage = stage;
            }
            public RestoreException(string stage, string message, Exception inner)
                : base(message, inner)
            {
                Stage = stage;
            }
        }

        public class Manifest
        {
            public string signed_url { get; set; }
            public string sha256 { get; set; }
            public long size_bytes { get; set; }
            public int schema_version { get; set; }
            public string built_at { get; set; }
            public string expires_at { get; set; }
        }

        private readonly HttpClient _http;
        private readonly Uri _supabaseUrl;

        public CloudSnapshotRestorer(string supabaseUrl, HttpClient http = null)
        {
            if (string.IsNullOrWhiteSpace(supabaseUrl))
                throw new ArgumentException("supabaseUrl required", nameof(supabaseUrl));
            _supabaseUrl = new Uri(supabaseUrl.TrimEnd('/') + "/");
            _http = http ?? new HttpClient { Timeout = TimeSpan.FromMinutes(10) };
        }

        public async Task RunAsync(
            string jwt,
            string targetPath,
            IProgress<RestoreProgress> progress,
            CancellationToken ct)
        {
            if (string.IsNullOrEmpty(jwt)) throw new ArgumentException("jwt required", nameof(jwt));
            if (string.IsNullOrEmpty(targetPath))
                throw new ArgumentException("targetPath required", nameof(targetPath));

            // Step 1+2: fetch manifest
            progress?.Report(new RestoreProgress { Stage = "manifest", Message = "Mengambil manifest…" });
            var manifest = await FetchManifestAsync(jwt, ct).ConfigureAwait(false);

            if (manifest.schema_version > SupportedSchemaVersion)
            {
                throw new RestoreException(
                    "manifest",
                    $"Server snapshot schema_version={manifest.schema_version}; client supports {SupportedSchemaVersion}. Update POS or rebuild snapshot.");
            }

            // Step 3: disk space check (size * 1.1)
            CheckDiskSpace(targetPath, manifest.size_bytes);

            // Step 4: download to temp
            string tmpPath = targetPath + ".tmp." + Guid.NewGuid().ToString("N");
            try
            {
                progress?.Report(new RestoreProgress
                {
                    Stage = "downloading",
                    TotalBytes = manifest.size_bytes,
                });
                await DownloadAsync(manifest.signed_url, tmpPath, manifest.size_bytes, progress, ct)
                    .ConfigureAwait(false);

                // Step 5: SHA-256
                progress?.Report(new RestoreProgress
                {
                    Stage = "verifying",
                    Message = "Memverifikasi integritas…",
                });
                var actualSha = await ComputeSha256Async(tmpPath, ct).ConfigureAwait(false);
                if (!string.Equals(actualSha, manifest.sha256, StringComparison.OrdinalIgnoreCase))
                {
                    throw new RestoreException(
                        "verifying",
                        $"SHA-256 mismatch: expected {manifest.sha256}, got {actualSha}");
                }

                // Step 6: integrity_check
                RunIntegrityCheck(tmpPath);

                // Step 7: DatabaseValidator
                var validation = DatabaseValidator.Validate(tmpPath, runIntegrityCheck: true);
                if (!validation.IsValid)
                {
                    throw new RestoreException(
                        "verifying",
                        "DatabaseValidator: " + string.Join("; ", validation.Errors));
                }

                // Step 8: atomic swap
                progress?.Report(new RestoreProgress { Stage = "swapping", Message = "Memasang database…" });
                AtomicSwap(tmpPath, targetPath);

                // Step 9: rebuild FTS (best-effort)
                TryRebuildFts(targetPath);

                progress?.Report(new RestoreProgress { Stage = "done", Message = "Selesai" });
            }
            catch (OperationCanceledException)
            {
                SafeDelete(tmpPath);
                throw;
            }
            catch (Exception ex) when (!(ex is RestoreException))
            {
                SafeDelete(tmpPath);
                throw new RestoreException("downloading", ex.Message, ex);
            }
            finally
            {
                SafeDelete(tmpPath); // no-op if already moved
            }
        }

        // ────────────────────────────────────────────────────────────────

        private async Task<Manifest> FetchManifestAsync(string jwt, CancellationToken ct)
        {
            var url = new Uri(_supabaseUrl, "functions/v1/snapshot-download");
            using var req = new HttpRequestMessage(HttpMethod.Post, url);
            req.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", jwt);
            var resp = await _http.SendAsync(req, ct).ConfigureAwait(false);
            if (!resp.IsSuccessStatusCode)
            {
                var body = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
                throw new RestoreException("manifest",
                    $"snapshot-download {(int)resp.StatusCode}: {body}");
            }
            var manifest = await resp.Content.ReadFromJsonAsync<Manifest>(cancellationToken: ct)
                .ConfigureAwait(false);
            if (manifest == null || string.IsNullOrEmpty(manifest.signed_url) ||
                string.IsNullOrEmpty(manifest.sha256))
            {
                throw new RestoreException("manifest", "incomplete manifest from server");
            }
            return manifest;
        }

        internal static void CheckDiskSpace(string targetPath, long sizeBytes)
        {
            var dir = Path.GetDirectoryName(Path.GetFullPath(targetPath)) ?? ".";
            Directory.CreateDirectory(dir);
            try
            {
                var di = new DriveInfo(Path.GetPathRoot(Path.GetFullPath(dir)));
                long needed = sizeBytes * DiskSpaceSafetyMultiplier / 10;
                if (di.AvailableFreeSpace < needed)
                {
                    throw new RestoreException(
                        "manifest",
                        $"Insufficient disk space: need {needed} bytes, have {di.AvailableFreeSpace}");
                }
            }
            catch (RestoreException) { throw; }
            catch
            {
                // Non-fatal — DriveInfo can fail on exotic mounts.
            }
        }

        private async Task DownloadAsync(
            string url,
            string tmpPath,
            long expectedSize,
            IProgress<RestoreProgress> progress,
            CancellationToken ct)
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, url);
            using var resp = await _http.SendAsync(
                req, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
            resp.EnsureSuccessStatusCode();

            using var src = await resp.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
            using var dst = new FileStream(tmpPath, FileMode.Create, FileAccess.Write, FileShare.None);

            var buffer = new byte[81920];
            long downloaded = 0;
            int read;
            while ((read = await src.ReadAsync(buffer, 0, buffer.Length, ct).ConfigureAwait(false)) > 0)
            {
                await dst.WriteAsync(buffer, 0, read, ct).ConfigureAwait(false);
                downloaded += read;
                if (progress != null && downloaded % (1024 * 256) < 81920)
                {
                    progress.Report(new RestoreProgress
                    {
                        Stage = "downloading",
                        BytesDownloaded = downloaded,
                        TotalBytes = expectedSize,
                    });
                }
            }
            await dst.FlushAsync(ct).ConfigureAwait(false);
        }

        internal static async Task<string> ComputeSha256Async(string path, CancellationToken ct)
        {
            using var sha = SHA256.Create();
            using var fs = new FileStream(
                path, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, useAsync: true);
            var buffer = new byte[81920];
            int read;
            while ((read = await fs.ReadAsync(buffer, 0, buffer.Length, ct).ConfigureAwait(false)) > 0)
            {
                sha.TransformBlock(buffer, 0, read, null, 0);
            }
            sha.TransformFinalBlock(Array.Empty<byte>(), 0, 0);
            var hash = sha.Hash;
            var sb = new StringBuilder(hash.Length * 2);
            foreach (var b in hash) sb.Append(b.ToString("x2"));
            return sb.ToString();
        }

        private static void RunIntegrityCheck(string dbPath)
        {
            using var conn = new SqliteConnection($"Data Source={dbPath};Mode=ReadOnly");
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "PRAGMA integrity_check;";
            using var rd = cmd.ExecuteReader();
            if (!rd.Read() || rd.GetString(0) != "ok")
            {
                throw new RestoreException("verifying", "PRAGMA integrity_check failed on downloaded snapshot");
            }
        }

        internal static void AtomicSwap(string tmpPath, string targetPath)
        {
            string bakPath = targetPath + ".bak";
            if (File.Exists(targetPath))
            {
                if (File.Exists(bakPath)) File.Delete(bakPath);
                File.Move(targetPath, bakPath);
            }
            try
            {
                File.Move(tmpPath, targetPath);
            }
            catch
            {
                // Rollback if swap failed after pre-move succeeded
                if (File.Exists(bakPath) && !File.Exists(targetPath))
                {
                    try { File.Move(bakPath, targetPath); } catch { /* best-effort */ }
                }
                throw;
            }
        }

        private static void TryRebuildFts(string dbPath)
        {
            try
            {
                using var conn = new SqliteConnection($"Data Source={dbPath}");
                conn.Open();
                using var cmd = conn.CreateCommand();
                cmd.CommandText = "INSERT INTO products_fts(products_fts) VALUES('rebuild');";
                cmd.ExecuteNonQuery();
            }
            catch
            {
                // Table may not exist in all DBs; non-fatal.
            }
        }

        private static void SafeDelete(string path)
        {
            try
            {
                if (File.Exists(path)) File.Delete(path);
            }
            catch { /* best-effort */ }
        }
    }
}
