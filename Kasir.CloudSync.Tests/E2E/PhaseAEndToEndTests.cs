using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using NUnit.Framework;
using FluentAssertions;
using Kasir.CloudSync.Outbox;
using Kasir.CloudSync.Sinks;
using Kasir.CloudSync.Tests.TestHelpers;
using Kasir.Data.Repositories;

namespace Kasir.CloudSync.Tests.E2E
{
    // Phase A US-A3 end-to-end verification. Marked [Explicit] because it needs
    // a real Postgres target. Run against either:
    //   - A Docker Postgres container started by the developer locally:
    //       docker run --rm -d -p 5432:5432 -e POSTGRES_PASSWORD=test \
    //           --name kasir-cloudsync-test postgres:15
    //       export KASIR_CLOUDSYNC_TEST_PG="Host=localhost;Port=5432;Database=postgres;Username=postgres;Password=test"
    //       dotnet test --filter "FullyQualifiedName~PhaseAEndToEndTests"
    //   - A real Supabase project (after Gate A0.1 passes):
    //       export KASIR_CLOUDSYNC_TEST_PG="Host=db.PROJECT.supabase.co;...;SslMode=Require"
    //       dotnet test --filter "FullyQualifiedName~PhaseAEndToEndTests"
    [TestFixture]
    [Explicit("Needs a real Postgres connection via KASIR_CLOUDSYNC_TEST_PG env var")]
    public class PhaseAEndToEndTests
    {
        private string _connStr;
        private SqliteConnection _sqlite;
        private SyncQueueRepository _repo;
        private PostgresSink _sink;
        private OutboxReader _reader;

        [SetUp]
        public async Task SetUp()
        {
            _connStr = Environment.GetEnvironmentVariable("KASIR_CLOUDSYNC_TEST_PG");
            if (string.IsNullOrWhiteSpace(_connStr))
            {
                Assert.Ignore("KASIR_CLOUDSYNC_TEST_PG not set");
            }

            _sqlite = TestDb.Create();
            _repo = new SyncQueueRepository(_sqlite);
            _sink = new PostgresSink(_connStr);
            _reader = new OutboxReader(_sqlite, _repo, _sink, NullLogger<OutboxReader>.Instance);

            // Seed Postgres with the products DDL + pg_trgm. Runs idempotently
            // (all statements use IF NOT EXISTS).
            string ddl = System.IO.File.ReadAllText(
                System.IO.Path.Combine(TestContext.CurrentContext.TestDirectory,
                    "..", "..", "..", "..",
                    "Kasir.CloudSync", "Sql", "products.sql"));
            await using (var conn = new NpgsqlConnection(_connStr))
            {
                await conn.OpenAsync();
                await using var cmd = conn.CreateCommand();
                cmd.CommandText = "TRUNCATE products; " + ddl;
                await cmd.ExecuteNonQueryAsync();
            }
        }

        [TearDown]
        public void TearDown()
        {
            _sqlite?.Close();
            _sqlite?.Dispose();
        }

        [Test]
        public async Task Insert_In_SQLite_Appears_In_Postgres_Within_60s()
        {
            Exec("INSERT INTO departments (dept_code, name) VALUES ('D1','Dept 1');");
            Exec(@"INSERT INTO products (product_code, name, barcode, dept_code, unit, price, buying_price)
                   VALUES ('E2E-001','E2E Test Product','8901234','D1','pcs',150000,100000);");
            // The Schema.sql trigger enqueues the row with status='pending'.
            // The cloud worker requires status='synced', so mark it synced
            // (simulating LAN push completion).
            Exec("UPDATE sync_queue SET status='synced', synced_at=datetime('now') WHERE table_name='products';");

            var shipped = await _reader.TickAsync(10, CancellationToken.None);
            shipped.Should().Be(1);

            // Verify in Postgres
            await using var conn = new NpgsqlConnection(_connStr);
            await conn.OpenAsync();
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT product_code, name, price, buying_price FROM products WHERE product_code='E2E-001';";
            await using var reader = await cmd.ExecuteReaderAsync();
            (await reader.ReadAsync()).Should().BeTrue();
            reader.GetString(0).Should().Be("E2E-001");
            reader.GetString(1).Should().Be("E2E Test Product");
            reader.GetInt64(2).Should().Be(150000L);
            reader.GetInt64(3).Should().Be(100000L);
        }

        [Test]
        public async Task Update_In_SQLite_Upserts_Into_Postgres()
        {
            Exec("INSERT INTO departments (dept_code, name) VALUES ('D1','Dept 1');");
            Exec(@"INSERT INTO products (product_code, name, dept_code, price)
                   VALUES ('E2E-002','Original','D1',100);");
            Exec("UPDATE sync_queue SET status='synced', synced_at=datetime('now') WHERE table_name='products';");
            await _reader.TickAsync(10, CancellationToken.None);

            // Update in SQLite
            Exec("UPDATE products SET name='Updated', price=200 WHERE product_code='E2E-002';");
            Exec("UPDATE sync_queue SET status='synced', synced_at=datetime('now'), cloud_synced=0 WHERE table_name='products';");

            await _reader.TickAsync(10, CancellationToken.None);

            await using var conn = new NpgsqlConnection(_connStr);
            await conn.OpenAsync();
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT name, price FROM products WHERE product_code='E2E-002';";
            await using var reader = await cmd.ExecuteReaderAsync();
            (await reader.ReadAsync()).Should().BeTrue();
            reader.GetString(0).Should().Be("Updated", "ON CONFLICT UPDATE wired up");
            reader.GetInt64(1).Should().Be(200L);
        }

        private void Exec(string sql)
        {
            using var cmd = _sqlite.CreateCommand();
            cmd.CommandText = sql;
            cmd.ExecuteNonQuery();
        }
    }
}
