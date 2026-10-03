using System;
using System.IO;
using FluentAssertions;
using Kasir.Auth;
using Kasir.CloudSync.Restore;
using Kasir.Data;
using Microsoft.Data.Sqlite;
using NUnit.Framework;

namespace Kasir.CloudSync.Tests.Restore
{
    // A cloud snapshot is Schema.sql + mirror tables: no users, roles, counters, and
    // config register_id = NULL. After restore the register must be able to log in and
    // must number documents with the paired register's number.
    [TestFixture]
    public class RestoredDatabasePreparationTests
    {
        private string _dir;
        private string _db;

        [SetUp]
        public void SetUp()
        {
            _dir = Path.Combine(Path.GetTempPath(), "restore-prep-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_dir);
            _db = Path.Combine(_dir, "snapshot.db");
            CreateSnapshotShapedDb(_db);
        }

        [TearDown]
        public void TearDown()
        {
            SqliteConnection.ClearAllPools();
            if (Directory.Exists(_dir)) Directory.Delete(_dir, true);
        }

        // Schema.sql only, exactly what SnapshotBuilder lays down before copying mirror rows.
        private static void CreateSnapshotShapedDb(string path)
        {
            string schema;
            using (var stream = typeof(DbConnection).Assembly.GetManifestResourceStream("Kasir.Data.Schema.sql"))
            using (var reader = new StreamReader(stream))
                schema = reader.ReadToEnd();

            using var conn = new SqliteConnection($"Data Source={path};Pooling=False");
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = schema;
            cmd.ExecuteNonQuery();
        }

        private static object Scalar(SqliteConnection conn, string sql)
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = sql;
            return cmd.ExecuteScalar();
        }

        [Test]
        public void Snapshot_without_users_gets_loginable_SM_user_and_roles()
        {
            var summary = new CloudSnapshotRestorer.RestoreSummary();

            CloudSnapshotRestorer.PrepareRestoredDatabase(_db, "KLR-02", summary);

            summary.SeededDefaultLogin.Should().BeTrue();
            using var conn = new SqliteConnection($"Data Source={_db};Pooling=False");
            conn.Open();
            Convert.ToInt64(Scalar(conn, "SELECT COUNT(*) FROM roles")).Should().Be(3);
            Convert.ToInt64(Scalar(conn, "SELECT COUNT(*) FROM users WHERE username = 'SM' AND is_active = 1")).Should().Be(1);

            var login = new AuthService(conn).Login("SM", "74121");
            login.Success.Should().BeTrue("the seeded default login must work on a restored register");
        }

        [Test]
        public void Restored_snapshot_passes_the_validator_that_rejected_it_before()
        {
            // Before seeding: no active user, validator rejects the snapshot.
            DatabaseValidator.Validate(_db).Errors.Should().Contain(e => e.Contains("No active users"));

            CloudSnapshotRestorer.PrepareRestoredDatabase(_db, "KLR-01", new CloudSnapshotRestorer.RestoreSummary());
            SqliteConnection.ClearAllPools();

            DatabaseValidator.Validate(_db).Errors.Should().NotContain(e => e.Contains("No active users"));
        }

        [TestCase("KLR-01", "01")]
        [TestCase("KLR-03", "03")]
        [TestCase("KLR-99", "99")]
        [TestCase("02", "02")]
        public void Register_id_is_set_from_pairing(string paired, string expected)
        {
            var summary = new CloudSnapshotRestorer.RestoreSummary();

            CloudSnapshotRestorer.PrepareRestoredDatabase(_db, paired, summary);

            summary.RegisterId.Should().Be(expected);
            using var conn = new SqliteConnection($"Data Source={_db};Pooling=False");
            conn.Open();
            Scalar(conn, "SELECT value FROM config WHERE key = 'register_id'").Should().Be(expected);
        }

        [Test]
        public void Existing_users_are_kept_and_not_reseeded()
        {
            using (var conn = new SqliteConnection($"Data Source={_db};Pooling=False"))
            {
                conn.Open();
                DbConnection.SeedDefaultData(conn);
                using var cmd = conn.CreateCommand();
                cmd.CommandText = "UPDATE users SET display_name = 'Pemilik' WHERE username = 'SM'";
                cmd.ExecuteNonQuery();
            }
            var summary = new CloudSnapshotRestorer.RestoreSummary();

            CloudSnapshotRestorer.PrepareRestoredDatabase(_db, "KLR-01", summary);

            summary.SeededDefaultLogin.Should().BeFalse();
            using var check = new SqliteConnection($"Data Source={_db};Pooling=False");
            check.Open();
            Scalar(check, "SELECT display_name FROM users WHERE username = 'SM'").Should().Be("Pemilik");
        }

        [TestCase("KLR-01", "01")]
        [TestCase("klr-2", "02")]
        [TestCase(" 03 ", "03")]
        [TestCase("KLR", null)]
        [TestCase("", null)]
        [TestCase(null, null)]
        public void NormalizeRegisterId(string input, string expected)
        {
            DbConnection.NormalizeRegisterId(input).Should().Be(expected);
        }
    }
}
