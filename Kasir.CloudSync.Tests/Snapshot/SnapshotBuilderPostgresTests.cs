using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Kasir.CloudSync.Snapshot;
using Kasir.Data.Repositories;
using Microsoft.Data.Sqlite;
using Npgsql;
using NUnit.Framework;

namespace Kasir.CloudSync.Tests.Snapshot
{
    // SnapshotBuilder.BuildAsync against a real Postgres holding the mirror DDL
    // (Kasir.CloudSync/Sql). [Explicit]: needs a LOCAL database, e.g. `supabase start`
    // in sinar-makmur-dashboard:
    //   export KASIR_CLOUDSYNC_TEST_PG="Host=127.0.0.1;Port=54322;Database=postgres;Username=postgres;Password=postgres"
    //   dotnet test Kasir.CloudSync.Tests --filter "FullyQualifiedName~SnapshotBuilderPostgresTests"
    // Everything runs in one transaction that TearDown rolls back (Npgsql commands use
    // the connection's open transaction); it still refuses non-local hosts.
    [TestFixture]
    [Explicit("Needs a local Postgres via KASIR_CLOUDSYNC_TEST_PG")]
    public class SnapshotBuilderPostgresTests
    {
        private string _connStr;
        private string _out;
        private NpgsqlConnection _pg;
        private NpgsqlTransaction _tx;

        [SetUp]
        public async Task SetUp()
        {
            _connStr = Environment.GetEnvironmentVariable("KASIR_CLOUDSYNC_TEST_PG");
            if (string.IsNullOrWhiteSpace(_connStr)) Assert.Ignore("KASIR_CLOUDSYNC_TEST_PG not set");
            var host = new NpgsqlConnectionStringBuilder(_connStr).Host;
            if (host != "localhost" && host != "127.0.0.1") Assert.Ignore("refusing non-local host " + host);

            _out = Path.Combine(Path.GetTempPath(), "snap-pg-" + Guid.NewGuid().ToString("N") + ".db");

            var sqlDir = Path.Combine(TestContext.CurrentContext.TestDirectory,
                "..", "..", "..", "..", "Kasir.CloudSync", "Sql");
            _pg = new NpgsqlConnection(_connStr);
            await _pg.OpenAsync();
            _tx = await _pg.BeginTransactionAsync();
            foreach (var f in new[] { "purchases.sql", "purchase_items.sql", "shifts.sql" })
            {
                await using var ddl = _pg.CreateCommand();
                ddl.CommandText = File.ReadAllText(Path.Combine(sqlDir, f));
                await ddl.ExecuteNonQueryAsync();
            }
            await using var seed = _pg.CreateCommand();
            seed.CommandText = @"
                -- A mirror created before May 2026 lacks these (CREATE IF NOT EXISTS never altered it).
                ALTER TABLE purchases ADD COLUMN IF NOT EXISTS disc2_pct INTEGER NOT NULL DEFAULT 0;
                ALTER TABLE purchases ADD COLUMN IF NOT EXISTS received_date TEXT;
                ALTER TABLE purchases ADD COLUMN IF NOT EXISTS terms INTEGER NOT NULL DEFAULT 0;
                TRUNCATE purchase_items, purchases, shifts;
                INSERT INTO purchases (journal_no, id, doc_type, doc_date, sub_code, period_code, terms, received_date, disc2_pct)
                VALUES ('MSK-01-2610-0001', 1, 'PURCHASE', '2026-10-01', 'V001', '202610', 30, '2026-10-02', 150);
                -- 3000000000: legacy DBF sync ids are 32-bit row-hash ids, above int.MaxValue.
                INSERT INTO purchase_items (id, journal_no, order_ref, product_code, quantity, qty_order, value, unit_price, disc2_pct, disc_amount, legacy_source)
                VALUES (3000000000, 'MSK-01-2610-0001', 'BPB-01-2610-0001', 'P001', 1200, 12, 600000, 50000, 250, 1000, 'DBF'),
                       (7, 'MSK-01-2610-0001', '', 'P002', 100, 0, 10000, 10000, 0, 0, NULL);
                -- Shift 1 exists on both registers; register 01's is still open.
                INSERT INTO shifts (id, register_id, shift_number, cashier_id, opened_at, opening_cash, status)
                VALUES (1, '02', '1', 3, '2026-10-07 07:05:00', 50000000, 'C'),
                       (1, '01', '1', 2, '2026-10-07 07:00:00', 50000000, 'O');";
            await seed.ExecuteNonQueryAsync();
        }

        [TearDown]
        public async Task TearDown()
        {
            if (_tx != null) await _tx.RollbackAsync();
            if (_pg != null) await _pg.DisposeAsync();
            if (_out != null && File.Exists(_out)) File.Delete(_out);
        }

        [Test]
        public async Task Snapshot_restores_purchase_lines_but_not_shifts()
        {
            var result = await SnapshotBuilder.BuildAsync(_pg, _out, CancellationToken.None);

            result.MissingInCloud.Should().NotContain(m => m.StartsWith("purchase"));
            result.MissingInCloud.Should().NotContain("shifts");
            result.MaxIds["purchase_items"].Should().Be(3000000000L);

            using var db = new SqliteConnection($"Data Source={_out};Pooling=False");
            db.Open();
            using (var cmd = db.CreateCommand())
            {
                cmd.CommandText = @"SELECT order_ref, quantity, qty_order, value, disc2_pct, disc_amount
                                    FROM purchase_items WHERE id = 3000000000;";
                using var rd = cmd.ExecuteReader();
                rd.Read().Should().BeTrue();
                rd.GetString(0).Should().Be("BPB-01-2610-0001");
                rd.GetInt64(1).Should().Be(1200);
                rd.GetInt64(2).Should().Be(12);
                rd.GetInt64(3).Should().Be(600000);
                rd.GetInt64(4).Should().Be(250);
                rd.GetInt64(5).Should().Be(1000);
            }
            using (var cmd = db.CreateCommand())
            {
                cmd.CommandText = "SELECT terms, received_date, disc2_pct FROM purchases WHERE journal_no = 'MSK-01-2610-0001';";
                using var rd = cmd.ExecuteReader();
                rd.Read().Should().BeTrue();
                rd.GetInt64(0).Should().Be(30);
                rd.GetString(1).Should().Be("2026-10-02");
                rd.GetInt64(2).Should().Be(150);
            }
            using (var cmd = db.CreateCommand())
            {
                cmd.CommandText = "SELECT COUNT(*) FROM purchase_items;";
                Convert.ToInt64(cmd.ExecuteScalar()).Should().Be(2);
                cmd.CommandText = "SELECT COUNT(*) FROM shifts;";
                Convert.ToInt64(cmd.ExecuteScalar()).Should().Be(0);
            }
            // What a register restored into slot 01 sees at login: no inherited open shift.
            new ShiftRepository(db).GetOpenShift("01").Should().BeNull();
        }
    }
}
