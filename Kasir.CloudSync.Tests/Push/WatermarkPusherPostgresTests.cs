using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Kasir.CloudSync.Outbox;
using Kasir.CloudSync.Push;
using Kasir.CloudSync.Sinks;
using Kasir.CloudSync.Tests.TestHelpers;
using Kasir.Data.Repositories;
using Kasir.Models;
using Kasir.Services;
using Kasir.Utils;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Npgsql;
using NUnit.Framework;

namespace Kasir.CloudSync.Tests.Push
{
    // WP-02 acceptance against a real (LOCAL) Supabase Postgres: a POS sale ends up as
    // a stock_movements row with x100 qty and the store-time (WITA) instant; replay adds nothing;
    // the watermark advances; a colliding legacy id is not overwritten.
    //
    // [Explicit]: needs a LOCAL database, e.g. `supabase start` in sinar-makmur-dashboard:
    //   export KASIR_CLOUDSYNC_TEST_PG="Host=127.0.0.1;Port=54322;Database=postgres;Username=postgres;Password=postgres"
    //   dotnet test Kasir.CloudSync.Tests --filter "FullyQualifiedName~WatermarkPusherPostgresTests"
    // Each test works in its own throw-away schema (search_path), with tables cloned
    // from public (LIKE ... INCLUDING ALL, i.e. the real mirror shape) or, when public
    // lacks them, from Kasir.CloudSync/Sql. public is never written. Refuses non-local hosts.
    [TestFixture]
    [Explicit("Needs a local Postgres via KASIR_CLOUDSYNC_TEST_PG")]
    public class WatermarkPusherPostgresTests
    {
        private string _baseConn;
        private string _schema;
        private string _sinkConn;
        private SqliteConnection _db;

        private sealed class FixedClock : IClock
        {
            public DateTime Now => new DateTime(2026, 10, 9, 15, 0, 0);
            public DateTime UtcNow => Now.AddHours(-7);
        }

        [SetUp]
        public async Task SetUp()
        {
            _baseConn = Environment.GetEnvironmentVariable("KASIR_CLOUDSYNC_TEST_PG");
            if (string.IsNullOrWhiteSpace(_baseConn)) Assert.Ignore("KASIR_CLOUDSYNC_TEST_PG not set");
            var csb = new NpgsqlConnectionStringBuilder(_baseConn);
            if (csb.Host != "localhost" && csb.Host != "127.0.0.1") Assert.Ignore("refusing non-local host " + csb.Host);

            _schema = "wp02_" + Guid.NewGuid().ToString("N").Substring(0, 12);
            csb.SearchPath = _schema;
            _sinkConn = csb.ToString();

            var sqlDir = Path.Combine(TestContext.CurrentContext.TestDirectory,
                "..", "..", "..", "..", "Kasir.CloudSync", "Sql");
            await using var pg = new NpgsqlConnection(_baseConn);
            await pg.OpenAsync();
            await ExecAsync(pg, $"CREATE SCHEMA {_schema};");
            foreach (var t in new[] { "stock_movements", "shifts", "sales" })
            {
                bool inPublic = Convert.ToBoolean(await ScalarAsync(pg,
                    $"SELECT to_regclass('public.{t}') IS NOT NULL;"));
                if (inPublic)
                    await ExecAsync(pg, $"CREATE TABLE {_schema}.{t} (LIKE public.{t} INCLUDING ALL);");
                else
                    await ExecAsync(pg, $"SET search_path TO {_schema}; " + File.ReadAllText(Path.Combine(sqlDir, t + ".sql")) + " RESET search_path;");
            }

            _db = TestDb.Create();
            new ConfigRepository(_db).Set("register_id", "01");
            new ProductRepository(_db).Insert(new Product
            {
                ProductCode = "P001", Name = "LAMPU 10W", Price = 2500000, CostPrice = 1800000,
                Status = "A", OpenPrice = "N", VatFlag = "N", LuxuryTaxFlag = "N", IsConsignment = "N"
            });
        }

        [TearDown]
        public async Task TearDown()
        {
            _db?.Close();
            _db?.Dispose();
            if (_schema == null || string.IsNullOrWhiteSpace(_baseConn)) return;
            await using var pg = new NpgsqlConnection(_baseConn);
            await pg.OpenAsync();
            await ExecAsync(pg, $"DROP SCHEMA IF EXISTS {_schema} CASCADE;");
        }

        private static async Task ExecAsync(NpgsqlConnection pg, string sql)
        {
            await using var cmd = pg.CreateCommand();
            cmd.CommandText = sql;
            await cmd.ExecuteNonQueryAsync();
        }

        private static async Task<object> ScalarAsync(NpgsqlConnection pg, string sql)
        {
            await using var cmd = pg.CreateCommand();
            cmd.CommandText = sql;
            return await cmd.ExecuteScalarAsync();
        }

        private async Task<object> CloudScalar(string sql)
        {
            await using var pg = new NpgsqlConnection(_sinkConn);
            await pg.OpenAsync();
            return await ScalarAsync(pg, sql);
        }

        private Sale Sell(int qty)
        {
            var sales = new SalesService(_db, new FixedClock());
            sales.SetCashier("ADM", 1);
            sales.AddItem("P001", qty);
            var sale = sales.CompleteSale(100000000, 0, 0, "", "", "");
            using var cmd = _db.CreateCommand();
            cmd.CommandText = "UPDATE stock_movements SET created_at = '2026-10-09 15:00:00', changed_at = '2026-10-09 15:00:00' WHERE journal_no = @j";
            cmd.Parameters.AddWithValue("@j", sale.JournalNo);
            cmd.ExecuteNonQuery();
            return sale;
        }

        private WatermarkPusher Pusher() =>
            new WatermarkPusher(_db, new GenericSink(_sinkConn), NullLogger<WatermarkPusher>.Instance);

        [Test]
        public async Task PosSale_ReachesSupabase_WithStoreTime_AndX100Qty()
        {
            var sale = Sell(2);

            var r = await Pusher().PushTableAsync("stock_movements", WatermarkPusher.StockMovementsWatermarkKey, 100, CancellationToken.None);

            r.Failed.Should().BeFalse(r.Error);
            r.Pushed.Should().Be(1);
            (await CloudScalar($"SELECT qty_out FROM stock_movements WHERE journal_no = '{sale.JournalNo}'"))
                .Should().Be(200L);
            (await CloudScalar($"SELECT val_out FROM stock_movements WHERE journal_no = '{sale.JournalNo}'"))
                .Should().Be(3600000L);
            (await CloudScalar($"SELECT to_char(created_at AT TIME ZONE 'Asia/Makassar', 'YYYY-MM-DD HH24:MI:SS') FROM stock_movements WHERE journal_no = '{sale.JournalNo}'"))
                .Should().Be("2026-10-09 15:00:00", "the WITA wall clock survives the round trip");
            (await CloudScalar($"SELECT to_char(created_at AT TIME ZONE 'UTC', 'YYYY-MM-DD HH24:MI:SS') FROM stock_movements WHERE journal_no = '{sale.JournalNo}'"))
                .Should().Be("2026-10-09 07:00:00");
        }

        [Test]
        public async Task Rerun_ProducesNoDuplicates_AndWatermarkAdvances()
        {
            Sell(1);
            Sell(3);
            var pusher = Pusher();
            string key = WatermarkPusher.StockMovementsWatermarkKey;

            var first = await pusher.PushTableAsync("stock_movements", key, 100, CancellationToken.None);
            var again = await pusher.PushTableAsync("stock_movements", key, 100, CancellationToken.None);
            new ConfigRepository(_db).Set(key, "0"); // replay from scratch
            var replay = await pusher.PushTableAsync("stock_movements", key, 100, CancellationToken.None);

            first.Pushed.Should().Be(2);
            again.Read.Should().Be(0, "nothing above the watermark");
            replay.Pushed.Should().Be(2);
            replay.Conflicts.Should().BeEmpty();
            (await CloudScalar("SELECT count(*) FROM stock_movements")).Should().Be(2L);
            new ConfigRepository(_db).Get(key).Should().Be(first.WatermarkAfter.ToString());
        }

        [Test]
        public async Task LegacyRowHoldingTheSameId_IsNotOverwritten()
        {
            Sell(1);
            long posId = Convert.ToInt64(new SqliteCommand("SELECT max(id) FROM stock_movements", _db).ExecuteScalar());
            await using (var pg = new NpgsqlConnection(_sinkConn))
            {
                await pg.OpenAsync();
                await ExecAsync(pg,
                    $@"INSERT INTO stock_movements (id, product_code, journal_no, movement_type, doc_date, period_code, qty_out)
                       VALUES ({posId}, 'OTHER', 'GHIST-LEGACY', 'SALE', '2001-01-01', '200101', 700);");
            }

            var r = await Pusher().PushTableAsync("stock_movements", WatermarkPusher.StockMovementsWatermarkKey, 100, CancellationToken.None);

            r.Conflicts.Should().Equal(posId);
            (await CloudScalar($"SELECT journal_no FROM stock_movements WHERE id = {posId}")).Should().Be("GHIST-LEGACY");
            (await CloudScalar($"SELECT qty_out FROM stock_movements WHERE id = {posId}")).Should().Be(700L);
        }

        [Test]
        public async Task Shift_OpenThenClosed_EndsClosedInSupabase()
        {
            using (var cmd = _db.CreateCommand())
            {
                cmd.CommandText = "INSERT INTO shifts (id, register_id, shift_number, cashier_id, opened_at, status) VALUES (1,'01','1',1,'2026-10-09 07:00:00','O')";
                cmd.ExecuteNonQuery();
            }
            var pusher = Pusher();
            await pusher.PushTableAsync("shifts", WatermarkPusher.ShiftsWatermarkKey, 100, CancellationToken.None);
            (await CloudScalar("SELECT status FROM shifts WHERE id = 1 AND register_id = '01'")).Should().Be("O");

            using (var cmd = _db.CreateCommand())
            {
                cmd.CommandText = "UPDATE shifts SET status='C', closed_at='2026-10-09 21:00:00', closing_cash=90000000 WHERE id = 1";
                cmd.ExecuteNonQuery();
            }
            await pusher.PushTableAsync("shifts", WatermarkPusher.ShiftsWatermarkKey, 100, CancellationToken.None);

            (await CloudScalar("SELECT status || ' ' || closed_at || ' ' || closing_cash FROM shifts WHERE id = 1"))
                .Should().Be("C 2026-10-09 21:00:00 90000000");
            (await CloudScalar("SELECT count(*) FROM shifts")).Should().Be(1L);
        }

        [Test]
        public async Task WorkerTick_PushesSaleHeader_And_Movement()
        {
            var sale = Sell(2);
            using (var cmd = _db.CreateCommand())
            {
                cmd.CommandText = "UPDATE sync_queue SET status = 'synced'"; // LAN already delivered it
                cmd.ExecuteNonQuery();
            }
            var cfg = new CloudSyncConfig();
            var sink = new GenericSink(_sinkConn);
            var worker = new CloudSyncWorker(NullLogger<CloudSyncWorker>.Instance, Options.Create(cfg),
                new OutboxRouter(_db, new SyncQueueRepository(_db), sink, NullLogger<OutboxRouter>.Instance, cfg.OutboxTableList()),
                new WatermarkPusher(_db, sink, NullLogger<WatermarkPusher>.Instance),
                new Kasir.CloudSync.Pull.NoOpPullService());

            (await worker.TickAsync(CancellationToken.None)).Should().BeTrue();

            (await CloudScalar($"SELECT count(*) FROM sales WHERE journal_no = '{sale.JournalNo}'")).Should().Be(1L);
            (await CloudScalar($"SELECT qty_out FROM stock_movements WHERE journal_no = '{sale.JournalNo}'")).Should().Be(200L);
        }
    }
}
