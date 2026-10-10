using Microsoft.Data.Sqlite;
using NUnit.Framework;
using FluentAssertions;
using Kasir.Data;
using Kasir.Data.Migrations;
using Kasir.Data.Repositories;
using Kasir.Tests.TestHelpers;

namespace Kasir.Tests.Data
{
    [TestFixture]
    public class Migration015Tests
    {
        private SqliteConnection _db;

        [SetUp]
        public void SetUp() { _db = TestDb.Create(); }

        [TearDown]
        public void TearDown() { _db.Close(); _db.Dispose(); }

        private void Exec(string sql)
        {
            using var cmd = _db.CreateCommand();
            cmd.CommandText = sql;
            cmd.ExecuteNonQuery();
        }

        [Test]
        public void FreshSchema_HasAppliedRequests_AndTheIdSequence()
        {
            SqlHelper.ExecuteScalar<long>(_db,
                "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='applied_requests'")
                .Should().Be(1);
            new ConfigRepository(_db).Get("pull_movement_id_seq").Should().Be("5000000000");
        }

        [Test]
        public void OlderDb_GetsTheTable_AndKeepsAnExistingSequence()
        {
            Exec("DROP TABLE applied_requests; DELETE FROM config WHERE key = 'pull_movement_id_seq';");
            new Migration_015().Up(_db);
            new ConfigRepository(_db).Get("pull_movement_id_seq").Should().Be("5000000000");

            new ConfigRepository(_db).Set("pull_movement_id_seq", "5000000042");
            Exec("INSERT INTO applied_requests (request_kind, idempotency_key) VALUES ('OPNAME', 'OPNAME:s:P')");
            new Migration_015().Up(_db);

            new ConfigRepository(_db).Get("pull_movement_id_seq").Should().Be("5000000042");
            SqlHelper.ExecuteScalar<long>(_db, "SELECT COUNT(*) FROM applied_requests").Should().Be(1);
        }

        [Test]
        public void AppliedRequests_RejectsTheSameKindAndKeyTwice()
        {
            Exec("INSERT INTO applied_requests (request_kind, idempotency_key) VALUES ('PURCHASE', 'PURCHASE:l1')");
            System.Action again = () => Exec("INSERT INTO applied_requests (request_kind, idempotency_key) VALUES ('PURCHASE', 'PURCHASE:l1')");
            again.Should().Throw<SqliteException>();
            Exec("INSERT INTO applied_requests (request_kind, idempotency_key) VALUES ('RETURN_OUT', 'PURCHASE:l1')");
        }
    }
}
