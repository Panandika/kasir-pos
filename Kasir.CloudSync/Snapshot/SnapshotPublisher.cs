using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Npgsql;

namespace Kasir.CloudSync.Snapshot
{
    // Options for `Kasir.CloudSync --build-snapshot`.
    public sealed class BuildSnapshotOptions
    {
        public string ConnectionString;
        public string OutputPath;
        public bool Upload;
        public string SupabaseUrl;
        public string ServiceRoleKey;
        // Claim and fulfil pending snapshot_build_requests (dashboard "Bangun snapshot").
        // With no pending request the run exits 0 without building.
        public bool ProcessPending;
        // Fulfil one specific request id (implies Upload).
        public string RequestId;
        // snapshot_metadata.build_trigger: 'manual' | 'auto_stale' | 'gha_fallback'.
        public string Trigger;
        // Fail before uploading a file larger than the project's Storage per-file
        // limit (Supabase free plan: 50 MB). 0 = no check. --max-upload-mb.
        public long MaxUploadBytes = 50L * 1024 * 1024;
        // Brotli-compress before upload (default). --no-compress uploads the raw .db
        // (only viable on a plan whose Storage file limit exceeds the raw size).
        public bool Compress = true;
        public int BrotliQuality = SnapshotCompression.DefaultQuality;
    }

    // Builds a snapshot from the cloud mirror (SnapshotBuilder), uploads it to the
    // private Storage bucket and records it in snapshot_metadata so register-pair /
    // snapshot-download can hand it to a register being commissioned. This is the
    // hub-less path: run by the snapshot-fallback GitHub workflow (cron + manual).
    //
    // Contract (sinar-makmur-dashboard 0043_register_pairing.sql, snapshot-download):
    //   bucket "snapshots", object "snapshot-{uuid}.db.br" (Brotli) or "snapshot-{uuid}.db"
    //   (--no-compress); snapshot_metadata.storage_path = "snapshots/<object>";
    //   sha256 / size_bytes describe the UPLOADED object (the bytes a register downloads
    //   and verifies), i.e. the compressed file when compressed;
    //   snapshot-download reports "encoding":"br" from the ".db.br" suffix;
    //   schema_version = SnapshotBuilder.SupportedSchemaVersion;
    //   latest non-superseded row is the one served.
    public static class SnapshotPublisher
    {
        public const string Bucket = "snapshots";
        public static readonly string[] ValidTriggers = { "manual", "auto_stale", "gha_fallback" };

        // Requests stuck in_progress longer than this (crashed run) are failed so the
        // dashboard can enqueue a new build (build-snapshot rejects while one is open).
        public static readonly TimeSpan StaleInProgress = TimeSpan.FromMinutes(30);

        // ------------------------------------------------------------------
        // Argument parsing
        // ------------------------------------------------------------------

        public static BuildSnapshotOptions ParseArgs(string[] args, Func<string, string> env, out string error)
        {
            error = null;
            env ??= _ => null;
            var o = new BuildSnapshotOptions();

            string Next(ref int i, string name)
            {
                if (i + 1 >= args.Length) throw new ArgumentException($"{name} requires a value");
                return args[++i];
            }

            try
            {
                for (int i = 0; i < args.Length; i++)
                {
                    switch (args[i])
                    {
                        case "--build-snapshot": break;
                        case "--connection-string": o.ConnectionString = Next(ref i, args[i]); break;
                        case "--output": o.OutputPath = Next(ref i, args[i]); break;
                        case "--upload": o.Upload = true; break;
                        case "--supabase-url": o.SupabaseUrl = Next(ref i, args[i]); break;
                        case "--service-role-key": o.ServiceRoleKey = Next(ref i, args[i]); break;
                        case "--process-pending": o.ProcessPending = true; break;
                        case "--request-id": o.RequestId = Next(ref i, args[i]); break;
                        case "--trigger": o.Trigger = Next(ref i, args[i]); break;
                        case "--no-compress": o.Compress = false; break;
                        case "--brotli-quality":
                            var q = Next(ref i, args[i]);
                            if (!int.TryParse(q, out var qv) || qv < 0 || qv > 11)
                                throw new ArgumentException("--brotli-quality must be 0-11");
                            o.BrotliQuality = qv;
                            break;
                        case "--max-upload-mb":
                            var raw = Next(ref i, args[i]);
                            if (!long.TryParse(raw, out var mb) || mb < 0)
                                throw new ArgumentException("--max-upload-mb must be a non-negative number");
                            o.MaxUploadBytes = mb * 1024 * 1024;
                            break;
                        default:
                            error = $"unknown argument: {args[i]}";
                            return null;
                    }
                }
            }
            catch (ArgumentException ex)
            {
                error = ex.Message;
                return null;
            }

            o.ConnectionString = FirstNonEmpty(o.ConnectionString, env("SUPABASE_CONN_STRING"), env("PGCONN"));
            o.SupabaseUrl = FirstNonEmpty(o.SupabaseUrl, env("SUPABASE_URL"));
            o.ServiceRoleKey = FirstNonEmpty(o.ServiceRoleKey, env("SUPABASE_SERVICE_ROLE_KEY"));
            o.OutputPath = FirstNonEmpty(o.OutputPath, Path.Combine(Path.GetTempPath(), "kasir-snapshot.db"));

            if (o.ProcessPending || !string.IsNullOrEmpty(o.RequestId)) o.Upload = true;
            if (string.IsNullOrEmpty(o.Trigger))
                o.Trigger = o.ProcessPending || !string.IsNullOrEmpty(o.RequestId) ? "manual" : "gha_fallback";

            if (string.IsNullOrWhiteSpace(o.ConnectionString))
                error = "missing --connection-string (or SUPABASE_CONN_STRING env)";
            else if (o.Upload && string.IsNullOrWhiteSpace(o.SupabaseUrl))
                error = "--upload requires --supabase-url (or SUPABASE_URL env)";
            else if (o.Upload && string.IsNullOrWhiteSpace(o.ServiceRoleKey))
                error = "--upload requires --service-role-key (or SUPABASE_SERVICE_ROLE_KEY env)";
            else if (!ValidTriggers.Contains(o.Trigger))
                error = $"--trigger must be one of {string.Join(", ", ValidTriggers)}";
            else if (!string.IsNullOrEmpty(o.RequestId) && !Guid.TryParse(o.RequestId, out _))
                error = "--request-id must be a uuid";

            if (error != null) return null;
            o.ConnectionString = NormalizeConnectionString(o.ConnectionString);
            return o;
        }

        private static string FirstNonEmpty(params string[] values) =>
            values.FirstOrDefault(v => !string.IsNullOrWhiteSpace(v));

        // Npgsql only takes keyword connection strings; Supabase hands out URIs
        // (postgresql://user:pass@host:port/db). Convert, keeping SSL required.
        public static string NormalizeConnectionString(string conn)
        {
            if (string.IsNullOrWhiteSpace(conn)) return conn;
            conn = conn.Trim();
            if (!conn.StartsWith("postgres://", StringComparison.OrdinalIgnoreCase)
                && !conn.StartsWith("postgresql://", StringComparison.OrdinalIgnoreCase))
                return conn;

            var uri = new Uri(conn);
            var userInfo = uri.UserInfo.Split(new[] { ':' }, 2);
            var b = new NpgsqlConnectionStringBuilder
            {
                Host = uri.Host,
                Port = uri.Port > 0 ? uri.Port : 5432,
                Database = Uri.UnescapeDataString(uri.AbsolutePath.TrimStart('/')) is { Length: > 0 } db ? db : "postgres",
                Username = Uri.UnescapeDataString(userInfo[0]),
                Password = userInfo.Length > 1 ? Uri.UnescapeDataString(userInfo[1]) : null,
                SslMode = SslMode.Require,
            };
            // Transaction-mode pooler (6543) can't hold prepared statements.
            if (b.Port == 6543) b.MaxAutoPrepare = 0;
            return b.ConnectionString;
        }

        // ------------------------------------------------------------------
        // Contract helpers (pure; unit-tested)
        // ------------------------------------------------------------------

        public static string ObjectKey(Guid id, bool compressed = true) =>
            $"snapshot-{id}.db" + (compressed ? SnapshotCompression.BrotliSuffix : "");

        public static string StoragePath(Guid id, bool compressed = true) => $"{Bucket}/{ObjectKey(id, compressed)}";

        public static string UploadUrl(string supabaseUrl, Guid id, bool compressed = true) =>
            $"{supabaseUrl.TrimEnd('/')}/storage/v1/object/{Bucket}/{ObjectKey(id, compressed)}";

        public static string MaxIdsJson(IReadOnlyDictionary<string, long> maxIds) =>
            JsonSerializer.Serialize(maxIds ?? new Dictionary<string, long>());

        public static string Truncate(string s, int max) =>
            s == null || s.Length <= max ? s : s.Substring(0, max);

        // ------------------------------------------------------------------
        // Run
        // ------------------------------------------------------------------

        public static async Task<int> RunAsync(BuildSnapshotOptions o, Action<string> log, CancellationToken ct)
        {
            await using var pg = new NpgsqlConnection(o.ConnectionString);
            await pg.OpenAsync(ct).ConfigureAwait(false);
            log("connected to cloud database");

            List<Guid> claimed = new List<Guid>();
            if (o.ProcessPending || !string.IsNullOrEmpty(o.RequestId))
            {
                int failedStale = await FailStaleInProgressAsync(pg, ct).ConfigureAwait(false);
                if (failedStale > 0) log($"marked {failedStale} stuck in_progress request(s) as failed");

                claimed = await ClaimRequestsAsync(pg, o.RequestId, ct).ConfigureAwait(false);
                if (claimed.Count == 0)
                {
                    log("no pending snapshot build requests; nothing to do");
                    return 0;
                }
                log($"claimed {claimed.Count} request(s): {string.Join(", ", claimed)}");
            }

            try
            {
                log("building snapshot from cloud mirror -> " + o.OutputPath);
                var started = DateTime.UtcNow;
                var result = await SnapshotBuilder.BuildAsync(pg, o.OutputPath, ct, w => log("WARN " + w))
                    .ConfigureAwait(false);
                log($"built: {result.RowCount} rows, {result.SizeBytes} bytes, sha256 {result.Sha256} " +
                    $"({(DateTime.UtcNow - started).TotalSeconds:F0}s)");

                // What gets uploaded: the Brotli file (default) or the raw .db.
                string uploadPath = o.OutputPath;
                long uploadSize = result.SizeBytes;
                string uploadSha = result.Sha256;
                if (o.Compress)
                {
                    var cStarted = DateTime.UtcNow;
                    var c = SnapshotCompression.CompressFile(o.OutputPath, o.OutputPath + SnapshotCompression.BrotliSuffix,
                        o.BrotliQuality, SnapshotCompression.DefaultWindow);
                    uploadPath = c.Path;
                    uploadSize = c.SizeBytes;
                    uploadSha = c.Sha256;
                    log($"compressed (brotli q{o.BrotliQuality} w{SnapshotCompression.DefaultWindow}): " +
                        $"{result.SizeBytes} -> {c.SizeBytes} bytes " +
                        $"({100.0 * c.SizeBytes / Math.Max(1, result.SizeBytes):F1}%), sha256 {c.Sha256} " +
                        $"({(DateTime.UtcNow - cStarted).TotalSeconds:F0}s)");
                }

                if (!o.Upload)
                {
                    log("--upload not set: snapshot left on disk, nothing published");
                    return 0;
                }

                if (o.MaxUploadBytes > 0 && uploadSize > o.MaxUploadBytes)
                {
                    throw new InvalidOperationException(
                        $"snapshot upload is {uploadSize / 1048576} MB{(o.Compress ? " (compressed)" : "")}, over the " +
                        $"Storage per-file limit of {o.MaxUploadBytes / 1048576} MB. Raise the project's Storage file " +
                        "size limit (needs a paid plan above 50 MB) and --max-upload-mb; not uploading.");
                }

                var id = Guid.NewGuid();
                await UploadAsync(o.SupabaseUrl, o.ServiceRoleKey, id, uploadPath, o.Compress, log, ct).ConfigureAwait(false);
                await RecordMetadataAsync(pg, id, uploadSha, uploadSize, result.MaxIds, o.Compress, o.Trigger, claimed, ct)
                    .ConfigureAwait(false);
                log($"published snapshot {id} ({StoragePath(id, o.Compress)}), trigger={o.Trigger}");
                return 0;
            }
            catch (Exception ex) when (claimed.Count > 0)
            {
                log("FAILED: " + ex.Message);
                await MarkRequestsFailedAsync(pg, claimed, ex.GetType().Name + ": " + ex.Message, CancellationToken.None)
                    .ConfigureAwait(false);
                throw;
            }
        }

        private static async Task<int> FailStaleInProgressAsync(NpgsqlConnection pg, CancellationToken ct)
        {
            await using var cmd = new NpgsqlCommand(
                @"update public.snapshot_build_requests
                     set status = 'failed', completed_at = now(),
                         error_message = 'timed out: build did not finish within ' || @mins || ' minutes'
                   where status = 'in_progress' and started_at < now() - make_interval(mins => @mins)", pg);
            cmd.Parameters.AddWithValue("mins", (int)StaleInProgress.TotalMinutes);
            return await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }

        // Claims all pending requests (one build satisfies every queued request), or the
        // one given by id. SKIP LOCKED keeps two concurrent runs from double-building.
        private static async Task<List<Guid>> ClaimRequestsAsync(NpgsqlConnection pg, string requestId, CancellationToken ct)
        {
            string filter = string.IsNullOrEmpty(requestId)
                ? "status = 'pending'"
                : "id = @id and status in ('pending','failed')";
            await using var cmd = new NpgsqlCommand(
                $@"update public.snapshot_build_requests r
                      set status = 'in_progress', started_at = now(), error_message = null
                    where r.id in (select id from public.snapshot_build_requests
                                    where {filter}
                                    order by requested_at
                                    for update skip locked)
                returning r.id", pg);
            if (!string.IsNullOrEmpty(requestId)) cmd.Parameters.AddWithValue("id", Guid.Parse(requestId));
            var ids = new List<Guid>();
            await using var rd = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
            while (await rd.ReadAsync(ct).ConfigureAwait(false)) ids.Add(rd.GetGuid(0));
            return ids;
        }

        private static async Task UploadAsync(string supabaseUrl, string key, Guid id, string path,
            bool compressed, Action<string> log, CancellationToken ct)
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(15) };
            await using var fs = File.OpenRead(path);
            using var req = new HttpRequestMessage(HttpMethod.Post, UploadUrl(supabaseUrl, id, compressed))
            {
                Content = new StreamContent(fs),
            };
            req.Content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", key);
            req.Headers.Add("apikey", key);
            req.Headers.Add("x-upsert", "true");

            log($"uploading {fs.Length} bytes to storage {StoragePath(id, compressed)}");
            using var resp = await http.SendAsync(req, ct).ConfigureAwait(false);
            if (!resp.IsSuccessStatusCode)
            {
                var body = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
                string hint = (int)resp.StatusCode == 413
                    ? " (file larger than the project's Storage upload limit; raise it in Storage settings)"
                    : body.Contains("Bucket not found", StringComparison.OrdinalIgnoreCase)
                        ? " (bucket 'snapshots' missing; apply the dashboard storage migration)"
                        : "";
                throw new InvalidOperationException(
                    $"storage upload failed: HTTP {(int)resp.StatusCode} {Truncate(body, 500)}{hint}");
            }
        }

        // sha256 / sizeBytes describe the uploaded object (compressed when compressed).
        private static async Task RecordMetadataAsync(NpgsqlConnection pg, Guid id,
            string sha256, long sizeBytes, IReadOnlyDictionary<string, long> maxIds, bool compressed,
            string trigger, List<Guid> claimed, CancellationToken ct)
        {
            await using var tx = await pg.BeginTransactionAsync(ct).ConfigureAwait(false);

            // Only the newest snapshot is served; older ones are superseded and later
            // garbage-collected by cleanup_superseded_snapshots().
            await using (var sup = new NpgsqlCommand(
                "update public.snapshot_metadata set superseded = true where superseded = false", pg, tx))
            {
                await sup.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
            }

            await using (var ins = new NpgsqlCommand(
                @"insert into public.snapshot_metadata
                    (id, built_at, sha256, size_bytes, schema_version, storage_path, max_ids, superseded, build_trigger)
                  values (@id, now(), @sha, @size, @schema, @path, @maxIds::jsonb, false, @trigger)", pg, tx))
            {
                ins.Parameters.AddWithValue("id", id);
                ins.Parameters.AddWithValue("sha", sha256);
                ins.Parameters.AddWithValue("size", sizeBytes);
                ins.Parameters.AddWithValue("schema", SnapshotBuilder.SupportedSchemaVersion);
                ins.Parameters.AddWithValue("path", StoragePath(id, compressed));
                ins.Parameters.AddWithValue("maxIds", MaxIdsJson(maxIds));
                ins.Parameters.AddWithValue("trigger", trigger);
                await ins.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
            }

            if (claimed.Count > 0)
            {
                await using var done = new NpgsqlCommand(
                    @"update public.snapshot_build_requests
                         set status = 'completed', completed_at = now(), snapshot_metadata_id = @meta, error_message = null
                       where id = any(@ids)", pg, tx);
                done.Parameters.AddWithValue("meta", id);
                done.Parameters.AddWithValue("ids", claimed.ToArray());
                await done.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
            }

            await tx.CommitAsync(ct).ConfigureAwait(false);
        }

        private static async Task MarkRequestsFailedAsync(NpgsqlConnection pg, List<Guid> ids, string error,
            CancellationToken ct)
        {
            try
            {
                await using var cmd = new NpgsqlCommand(
                    @"update public.snapshot_build_requests
                         set status = 'failed', completed_at = now(), error_message = @err
                       where id = any(@ids)", pg);
                cmd.Parameters.AddWithValue("err", Truncate(error, 1000));
                cmd.Parameters.AddWithValue("ids", ids.ToArray());
                await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
            }
            catch
            {
                // Best effort: the stale-in_progress sweep fails it on the next run.
            }
        }
    }
}
