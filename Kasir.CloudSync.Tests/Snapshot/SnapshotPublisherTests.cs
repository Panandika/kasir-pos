using System;
using System.Collections.Generic;
using System.IO;
using FluentAssertions;
using Kasir.CloudSync.Snapshot;
using Microsoft.Data.Sqlite;
using NUnit.Framework;

namespace Kasir.CloudSync.Tests.Snapshot
{
    [TestFixture]
    public class SnapshotPublisherTests
    {
        private static Func<string, string> Env(params (string Key, string Value)[] vars)
        {
            var d = new Dictionary<string, string>();
            foreach (var (k, v) in vars) d[k] = v;
            return k => d.TryGetValue(k, out var v) ? v : null;
        }

        [Test]
        public void ParseArgs_BuildOnly_DefaultsToGhaFallbackWithoutUpload()
        {
            var o = SnapshotPublisher.ParseArgs(
                new[] { "--build-snapshot", "--connection-string", "Host=h;Database=d", "--output", "./out/s.db" },
                Env(), out var err);

            err.Should().BeNull();
            o.Upload.Should().BeFalse();
            o.Trigger.Should().Be("gha_fallback");
            o.OutputPath.Should().Be("./out/s.db");
        }

        [Test]
        public void ParseArgs_ProcessPending_ImpliesUploadAndManualTrigger_ReadsEnv()
        {
            var o = SnapshotPublisher.ParseArgs(
                new[] { "--build-snapshot", "--process-pending" },
                Env(("SUPABASE_CONN_STRING", "Host=h;Database=d"),
                    ("SUPABASE_URL", "https://x.supabase.co"),
                    ("SUPABASE_SERVICE_ROLE_KEY", "secret")),
                out var err);

            err.Should().BeNull();
            o.Upload.Should().BeTrue();
            o.Trigger.Should().Be("manual");
            o.SupabaseUrl.Should().Be("https://x.supabase.co");
            o.ServiceRoleKey.Should().Be("secret");
        }

        [Test]
        public void ParseArgs_MaxUploadMb_DefaultsTo50_AndCanBeRaisedOrDisabled()
        {
            SnapshotPublisher.ParseArgs(new[] { "--connection-string", "Host=h" }, Env(), out _)
                .MaxUploadBytes.Should().Be(50L * 1024 * 1024);
            SnapshotPublisher.ParseArgs(new[] { "--connection-string", "Host=h", "--max-upload-mb", "500" }, Env(), out _)
                .MaxUploadBytes.Should().Be(500L * 1024 * 1024);
            SnapshotPublisher.ParseArgs(new[] { "--connection-string", "Host=h", "--max-upload-mb", "0" }, Env(), out _)
                .MaxUploadBytes.Should().Be(0);
            SnapshotPublisher.ParseArgs(new[] { "--connection-string", "Host=h", "--max-upload-mb", "big" }, Env(), out var err)
                .Should().BeNull();
            err.Should().Contain("--max-upload-mb");
        }

        [Test]
        public void ParseArgs_UploadWithoutKey_IsUsageError()
        {
            SnapshotPublisher.ParseArgs(
                new[] { "--build-snapshot", "--connection-string", "Host=h", "--upload", "--supabase-url", "https://x" },
                Env(), out var err).Should().BeNull();
            err.Should().Contain("service-role-key");
        }

        [TestCase(new[] { "--build-snapshot" }, "connection-string")]
        [TestCase(new[] { "--build-snapshot", "--connection-string", "Host=h", "--bogus" }, "unknown argument")]
        [TestCase(new[] { "--build-snapshot", "--connection-string" }, "requires a value")]
        [TestCase(new[] { "--build-snapshot", "--connection-string", "Host=h", "--trigger", "cron" }, "--trigger")]
        [TestCase(new[] { "--build-snapshot", "--connection-string", "Host=h", "--request-id", "abc",
            "--supabase-url", "https://x", "--service-role-key", "k" }, "uuid")]
        public void ParseArgs_InvalidInput_ReturnsError(string[] args, string expected)
        {
            SnapshotPublisher.ParseArgs(args, Env(), out var err).Should().BeNull();
            err.Should().Contain(expected);
        }

        [Test]
        public void NormalizeConnectionString_ConvertsSupabaseUri()
        {
            var conn = SnapshotPublisher.NormalizeConnectionString(
                "postgresql://postgres.ref:p%40ss@aws-0-ap.pooler.supabase.com:6543/postgres");

            conn.Should().Contain("Host=aws-0-ap.pooler.supabase.com");
            conn.Should().Contain("Port=6543");
            conn.Should().Contain("Username=postgres.ref");
            conn.Should().Contain("Password=p@ss");
            conn.Should().Contain("SSL Mode=Require");
            conn.Should().Contain("Max Auto Prepare=0", "transaction pooler can't keep prepared statements");
        }

        [Test]
        public void NormalizeConnectionString_LeavesKeywordFormat()
        {
            SnapshotPublisher.NormalizeConnectionString("Host=h;Database=d").Should().Be("Host=h;Database=d");
        }

        [Test]
        public void StorageContract_MatchesSnapshotDownload()
        {
            var id = Guid.Parse("11111111-2222-3333-4444-555555555555");

            SnapshotPublisher.StoragePath(id).Should().Be("snapshots/snapshot-11111111-2222-3333-4444-555555555555.db");
            SnapshotPublisher.UploadUrl("https://x.supabase.co/", id).Should()
                .Be("https://x.supabase.co/storage/v1/object/snapshots/snapshot-11111111-2222-3333-4444-555555555555.db");
        }

        [Test]
        public void MaxIdsJson_SerializesTableMaxima()
        {
            SnapshotPublisher.MaxIdsJson(new Dictionary<string, long> { ["sales"] = 42 })
                .Should().Be("{\"sales\":42}");
            SnapshotPublisher.MaxIdsJson(null).Should().Be("{}");
        }

        [Test]
        public void FinalizeSingleFile_FoldsWalIntoMainFile()
        {
            var path = Path.Combine(Path.GetTempPath(), $"snap-{Guid.NewGuid():N}.db");
            try
            {
                using (var c = new SqliteConnection($"Data Source={path};Pooling=False"))
                {
                    c.Open();
                    using var cmd = c.CreateCommand();
                    cmd.CommandText = "PRAGMA journal_mode=WAL; CREATE TABLE t(x); INSERT INTO t VALUES (1),(2),(3);";
                    cmd.ExecuteNonQuery();

                    SnapshotBuilder.FinalizeSingleFile(c);
                }

                File.Exists(path + "-wal").Should().BeFalse("the uploaded .db must be self-contained");
                using var check = new SqliteConnection($"Data Source={path};Mode=ReadOnly;Pooling=False");
                check.Open();
                using var q = check.CreateCommand();
                q.CommandText = "PRAGMA freelist_count;";
                Convert.ToInt32(q.ExecuteScalar()).Should().Be(0, "VACUUM leaves no free pages");
                q.CommandText = "PRAGMA journal_mode;";
                q.ExecuteScalar().Should().Be("delete");
                q.CommandText = "SELECT count(*) FROM t;";
                Convert.ToInt32(q.ExecuteScalar()).Should().Be(3);
            }
            finally
            {
                foreach (var f in new[] { path, path + "-wal", path + "-shm" })
                    if (File.Exists(f)) File.Delete(f);
            }
        }
    }
}
