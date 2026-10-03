using System;
using System.IO;
using System.Net.Http;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Kasir.CloudSync.Snapshot;
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
    //   5. SHA-256 verify (of the downloaded bytes — the compressed file when encoding=br)
    //   5b. encoding "br": stream-decompress to the .db temp file
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
        // Brotli snapshots expand ~5x (37 MB -> 189 MB, 2026-10). Need room for the
        // compressed download plus the decompressed database: compressed * 8 (denominator 1).
        public const long CompressedDiskSpaceMultiplier = 8;

        public class RestoreProgress
        {
            public string Stage; // pair | manifest | downloading | verifying | decompressing | swapping | done
            public long BytesDownloaded;
            public long TotalBytes;
            public string Message;
        }

        public class RestoreException : Exception
        {
            public string Stage { get; }
            public int? HttpStatus { get; private set; }
            public string ServerCode { get; private set; } = "";
            public string ServerMessage { get; private set; } = "";
            public RestoreException(string stage, string message) : base(message)
            {
                Stage = stage;
            }
            public RestoreException(string stage, string message, Exception inner)
                : base(message, inner)
            {
                Stage = stage;
            }
            public static RestoreException FromHttp(string stage, int status, string body)
            {
                var err = ServerError.Parse(body);
                return new RestoreException(stage, $"snapshot-download {status}: {body}")
                {
                    HttpStatus = status,
                    ServerCode = err.Code ?? "",
                    ServerMessage = err.Message ?? "",
                };
            }
            internal RestoreException WithStatus(int? status)
            {
                HttpStatus = status;
                return this;
            }
        }

        // Diagnostics hook: (step, endpoint, httpStatus, responseBody, exception).
        // Endpoints are logged without query strings; never receives the JWT.
        public Action<string, string, int?, string, Exception> OnEvent { get; set; }

        private void Emit(string step, string endpoint, int? status, string body, Exception ex)
        {
            try { OnEvent?.Invoke(step, endpoint, status, body, ex); } catch { /* never break restore */ }
        }

        public class Manifest
        {
            public string signed_url { get; set; }
            public string sha256 { get; set; }
            public long size_bytes { get; set; }
            public int schema_version { get; set; }
            public string built_at { get; set; }
            public string expires_at { get; set; }
            // "br" = Brotli-compressed .db; null/""/"identity" = plain .db (older servers).
            public string encoding { get; set; }
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

        public class RestoreSummary
        {
            // True when the snapshot had no login and the default SM user + roles
            // were seeded (operator must change the password).
            public bool SeededDefaultLogin;
            // config register_id written to the restored database ("01"), or null.
            public string RegisterId;
        }

        // pairedRegisterId: slot from register-pair ("KLR-01"); written to config
        // register_id so document numbers use this register's number.
        public async Task<RestoreSummary> RunAsync(
            string jwt,
            string targetPath,
            IProgress<RestoreProgress> progress,
            CancellationToken ct,
            string pairedRegisterId = null)
        {
            var summary = new RestoreSummary();
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

            bool compressed = SnapshotCompression.IsBrotli(manifest.encoding);
            if (!compressed && !SnapshotCompression.IsPlain(manifest.encoding))
            {
                throw new RestoreException(
                    "manifest",
                    $"Unsupported snapshot encoding \"{manifest.encoding}\". Update POS.");
            }

            // Step 3: disk space check (plain: size * 1.1; brotli: compressed * 8)
            CheckDiskSpace(targetPath, manifest.size_bytes, compressed);

            // Step 4: download to temp
            string tmpPath = targetPath + ".tmp." + Guid.NewGuid().ToString("N");
            string downloadPath = compressed ? tmpPath + SnapshotCompression.BrotliSuffix : tmpPath;
            try
            {
                progress?.Report(new RestoreProgress
                {
                    Stage = "downloading",
                    TotalBytes = manifest.size_bytes,
                });
                await DownloadAsync(manifest.signed_url, downloadPath, manifest.size_bytes, progress, ct)
                    .ConfigureAwait(false);

                // Step 5: SHA-256 of the downloaded bytes (compressed file when br)
                progress?.Report(new RestoreProgress
                {
                    Stage = "verifying",
                    Message = "Memverifikasi integritas…",
                });
                var actualSha = await ComputeSha256Async(downloadPath, ct).ConfigureAwait(false);
                if (!string.Equals(actualSha, manifest.sha256, StringComparison.OrdinalIgnoreCase))
                {
                    throw new RestoreException(
                        "verifying",
                        $"SHA-256 mismatch: expected {manifest.sha256}, got {actualSha}");
                }

                // Step 5b: decompress (verified bytes -> .db temp file)
                if (compressed)
                {
                    progress?.Report(new RestoreProgress
                    {
                        Stage = "decompressing",
                        Message = "Membuka kemasan data…",
                    });
                    await DecompressAsync(downloadPath, tmpPath, ct).ConfigureAwait(false);
                    SafeDelete(downloadPath);
                    Emit("decompressing", null, null, null, null);
                }

                // Step 6: integrity_check
                RunIntegrityCheck(tmpPath);

                // Step 6b: make the register usable. Snapshots carry no users/roles/
                // config/counters (the cloud mirror has none), so seed the defaults and
                // stamp this register's number. Must run before the validator, which
                // rejects a database without an active user.
                PrepareRestoredDatabase(tmpPath, pairedRegisterId, summary);

                // Step 7: DatabaseValidator
                var validation = DatabaseValidator.Validate(tmpPath, runIntegrityCheck: true);
                if (!validation.IsValid)
                {
                    throw new RestoreException(
                        "verifying",
                        "DatabaseValidator: " + string.Join("; ", validation.Errors));
                }

                // Step 8: atomic swap. Fold any WAL written by the prepare/validate steps
                // into the main file first: only the main file is moved, so rows left in
                // "<tmp>-wal" (the seeded SM login, register_id) would otherwise be lost.
                progress?.Report(new RestoreProgress { Stage = "swapping", Message = "Memasang database…" });
                ConsolidateForMove(tmpPath);
                AtomicSwap(tmpPath, targetPath);

                // Step 9: rebuild FTS (best-effort)
                TryRebuildFts(targetPath);

                progress?.Report(new RestoreProgress { Stage = "done", Message = "Selesai" });
                return summary;
            }
            catch (OperationCanceledException)
            {
                SafeDelete(tmpPath);
                SafeDelete(downloadPath);
                throw;
            }
            catch (Exception ex) when (!(ex is RestoreException))
            {
                SafeDelete(tmpPath);
                SafeDelete(downloadPath);
                throw new RestoreException("downloading", ex.Message, ex);
            }
            finally
            {
                SafeDelete(tmpPath); // no-op if already moved
                SafeDelete(tmpPath + "-wal");
                SafeDelete(tmpPath + "-shm");
                SafeDelete(downloadPath);
            }
        }

        // Brotli -> plain .db. Corrupt/truncated data and a full disk become
        // RestoreException("decompressing") so the UI can explain them.
        internal static async Task DecompressAsync(string compressedPath, string dbPath, CancellationToken ct)
        {
            try
            {
                await SnapshotCompression.DecompressFileAsync(compressedPath, dbPath, ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex) when (ex is InvalidDataException || ex is InvalidOperationException)
            {
                // BrotliStream reports corrupt input as InvalidOperationException
                // ("Decoder ran into invalid data") and truncated input as InvalidDataException.
                SafeDelete(dbPath);
                throw new RestoreException("decompressing", "Snapshot data is corrupt (brotli): " + ex.Message, ex);
            }
            catch (IOException ex) when (IsDiskFull(ex))
            {
                SafeDelete(dbPath);
                throw new RestoreException("decompressing", "Insufficient disk space while decompressing: " + ex.Message, ex);
            }
            catch (IOException ex)
            {
                SafeDelete(dbPath);
                throw new RestoreException("decompressing", "Decompressing snapshot failed: " + ex.Message, ex);
            }
        }

        // ERROR_DISK_FULL (0x70) / ERROR_HANDLE_DISK_FULL (0x27) on Windows, ENOSPC elsewhere.
        private static bool IsDiskFull(IOException ex)
        {
            int code = ex.HResult & 0xFFFF;
            return code == 0x70 || code == 0x27 || code == 28
                || ex.Message.IndexOf("space", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        // ────────────────────────────────────────────────────────────────

        private async Task<Manifest> FetchManifestAsync(string jwt, CancellationToken ct)
        {
            var url = new Uri(_supabaseUrl, "functions/v1/snapshot-download");
            using var req = new HttpRequestMessage(HttpMethod.Post, url);
            req.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", jwt);
            HttpResponseMessage resp;
            try
            {
                resp = await _http.SendAsync(req, ct).ConfigureAwait(false);
            }
            catch (HttpRequestException ex)
            {
                Emit("manifest", url.ToString(), null, null, ex);
                throw new RestoreException("manifest", "snapshot-download unreachable: " + ex.Message, ex);
            }
            if (!resp.IsSuccessStatusCode)
            {
                var body = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
                Emit("manifest", url.ToString(), (int)resp.StatusCode, body, null);
                throw RestoreException.FromHttp("manifest", (int)resp.StatusCode, body);
            }
            Emit("manifest", url.ToString(), (int)resp.StatusCode, null, null);
            var manifest = await resp.Content.ReadFromJsonAsync<Manifest>(cancellationToken: ct)
                .ConfigureAwait(false);
            if (manifest == null || string.IsNullOrEmpty(manifest.signed_url) ||
                string.IsNullOrEmpty(manifest.sha256))
            {
                throw new RestoreException("manifest", "incomplete manifest from server");
            }
            return manifest;
        }

        internal static long RequiredDiskBytes(long sizeBytes, bool compressed) =>
            compressed ? sizeBytes * CompressedDiskSpaceMultiplier : sizeBytes * DiskSpaceSafetyMultiplier / 10;

        internal static void CheckDiskSpace(string targetPath, long sizeBytes, bool compressed = false)
        {
            var dir = Path.GetDirectoryName(Path.GetFullPath(targetPath)) ?? ".";
            Directory.CreateDirectory(dir);
            try
            {
                var di = new DriveInfo(Path.GetPathRoot(Path.GetFullPath(dir)));
                long needed = RequiredDiskBytes(sizeBytes, compressed);
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
            Emit("downloading", url, (int)resp.StatusCode, null, null);
            if (!resp.IsSuccessStatusCode)
            {
                var body = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
                Emit("downloading", url, (int)resp.StatusCode, body, null);
                throw RestoreException.FromHttp("downloading", (int)resp.StatusCode, body);
            }

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

        internal static void PrepareRestoredDatabase(string dbPath, string pairedRegisterId, RestoreSummary summary)
        {
            try
            {
                // Pooling off: the file is moved right after, which fails on Windows if a
                // pooled handle keeps it open.
                using var conn = new SqliteConnection($"Data Source={dbPath};Pooling=False");
                conn.Open();
                summary.SeededDefaultLogin = DbConnection.SeedDefaultsIfNoLogin(conn);
                string reg = DbConnection.NormalizeRegisterId(pairedRegisterId);
                if (reg != null)
                {
                    DbConnection.SetRegisterId(conn, reg);
                    summary.RegisterId = reg;
                }
            }
            catch (Exception ex)
            {
                throw new RestoreException("verifying", "Preparing restored database failed: " + ex.Message, ex);
            }
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

        // Checkpoints the WAL into the main file, switches the journal back to a single
        // file and removes -wal/-shm siblings, so the database is complete in one file
        // before it is moved. Pools are cleared first so no handle keeps the WAL alive.
        internal static void ConsolidateForMove(string dbPath)
        {
            try
            {
                SqliteConnection.ClearAllPools();
                using (var conn = new SqliteConnection($"Data Source={dbPath};Pooling=False"))
                {
                    conn.Open();
                    using var cmd = conn.CreateCommand();
                    cmd.CommandText = "PRAGMA wal_checkpoint(TRUNCATE); PRAGMA journal_mode=DELETE;";
                    cmd.ExecuteNonQuery();
                }
                SafeDelete(dbPath + "-wal");
                SafeDelete(dbPath + "-shm");
            }
            catch (Exception ex)
            {
                throw new RestoreException("verifying", "Finalizing restored database failed: " + ex.Message, ex);
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
