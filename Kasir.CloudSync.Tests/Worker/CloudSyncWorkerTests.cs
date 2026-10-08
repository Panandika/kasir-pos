using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Kasir.CloudSync.Outbox;
using Kasir.CloudSync.Pull;
using Kasir.CloudSync.Push;
using Kasir.CloudSync.Sinks;
using Kasir.CloudSync.Tests.TestHelpers;
using Kasir.Data.Repositories;
using Kasir.Models;
using Kasir.Services;
using Kasir.Utils;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NUnit.Framework;

namespace Kasir.CloudSync.Tests.Worker
{
    // WP-02 task 0 + 2: the worker is wired through DI and each tick runs
    // OutboxRouter (sync_queue tables in scope) -> WatermarkPusher (stock_movements,
    // shifts) -> PullService, with one failing step not blocking the others.
    [TestFixture]
    public class CloudSyncWorkerTests
    {
        private SqliteConnection _db;
        private InMemoryMirrorSink _sink;

        private sealed class FixedClock : IClock
        {
            public DateTime Now => new DateTime(2026, 10, 9, 15, 0, 0);
            public DateTime UtcNow => Now.AddHours(-7);
        }

        private sealed class CountingPull : IPullService
        {
            public int Calls;
            public bool Throw;
            public Task<int> TickAsync(CancellationToken ct)
            {
                Calls++;
                if (Throw) throw new InvalidOperationException("pull down");
                return Task.FromResult(0);
            }
        }

        [SetUp]
        public void SetUp()
        {
            _db = TestDb.Create();
            _sink = new InMemoryMirrorSink();
            new ConfigRepository(_db).Set("register_id", "01");
        }

        [TearDown]
        public void TearDown()
        {
            _db.Close();
            _db.Dispose();
        }

        private void SeedProduct(string code)
        {
            new ProductRepository(_db).Insert(new Product
            {
                ProductCode = code, Name = code, Price = 2500000, CostPrice = 1800000,
                Status = "A", OpenPrice = "N", VatFlag = "N", LuxuryTaxFlag = "N", IsConsignment = "N"
            });
        }

        private Sale MakeSale(string code, int qty)
        {
            var sales = new SalesService(_db, new FixedClock());
            sales.SetCashier("ADM", 1);
            sales.AddItem(code, qty);
            return sales.CompleteSale(100000000, 0, 0, "", "", "");
        }

        // LAN sync marks queue rows 'synced'; the cloud reader only takes those.
        private void MarkQueueLanSynced()
        {
            using var cmd = _db.CreateCommand();
            cmd.CommandText = "UPDATE sync_queue SET status = 'synced'";
            cmd.ExecuteNonQuery();
        }

        private CloudSyncWorker Worker(CountingPull pull, IReadOnlyCollection<string> tables = null)
        {
            var cfg = new CloudSyncConfig { BatchSize = 100, PushBatchSize = 100, PushMaxBatchesPerTick = 5 };
            var router = new OutboxRouter(_db, new SyncQueueRepository(_db), _sink,
                NullLogger<OutboxRouter>.Instance, tables ?? cfg.OutboxTableList());
            var pusher = new WatermarkPusher(_db, _sink, NullLogger<WatermarkPusher>.Instance);
            return new CloudSyncWorker(NullLogger<CloudSyncWorker>.Instance, Options.Create(cfg), router, pusher, pull);
        }

        [Test]
        public async Task Tick_PushesSale_StockMovement_Shift_AndRunsPull()
        {
            SeedProduct("P001");
            var sale = MakeSale("P001", 2);
            MarkQueueLanSynced();
            using (var cmd = _db.CreateCommand())
            {
                cmd.CommandText = "INSERT INTO shifts (register_id, shift_number, cashier_id, opened_at, status) VALUES ('01','1',1,'2026-10-09 07:00:00','O')";
                cmd.ExecuteNonQuery();
            }
            var pull = new CountingPull();

            bool ok = await Worker(pull).TickAsync(CancellationToken.None);

            ok.Should().BeTrue();
            _sink.Table("sales").Keys.Should().Contain(sale.JournalNo);
            _sink.Table("stock_movements").Values.Single()["qty_out"].Should().Be(200L);
            _sink.Table("shifts").Should().HaveCount(1);
            pull.Calls.Should().Be(1);
            _sink.Tables.ContainsKey("products").Should().BeFalse("products are outside the default push scope");
        }

        [Test]
        public async Task Tick_OutboxFailure_DoesNotBlockStockMovements()
        {
            SeedProduct("P001");
            MakeSale("P001", 1);
            MarkQueueLanSynced();
            var pull = new CountingPull { Throw = true };
            _sink.FailNext = true; // first sink call = the outbox sales upsert

            bool ok = await Worker(pull).TickAsync(CancellationToken.None);

            _sink.Table("stock_movements").Should().HaveCount(1, "the movement push still runs");
            pull.Calls.Should().Be(1, "the pull step still runs");
            ok.Should().BeFalse("a failing pull step fails the tick for backoff");
        }

        [Test]
        public async Task Tick_SinkDown_FailsTick_AndKeepsWatermark()
        {
            SeedProduct("P001");
            MakeSale("P001", 1);
            // No LAN-synced queue rows -> the first sink call is the stock_movements push.
            _sink.FailNext = true;

            bool ok = await Worker(new CountingPull()).TickAsync(CancellationToken.None);

            ok.Should().BeFalse();
            new ConfigRepository(_db).Get(WatermarkPusher.StockMovementsWatermarkKey).Should().Be("0");
        }

        [Test]
        public async Task Outbox_ScopeFilter_DoesNotStarveSalesBehindProducts()
        {
            for (int i = 0; i < 5; i++) SeedProduct("P00" + i); // 5 products queue rows first
            var sale = MakeSale("P001", 1);
            MarkQueueLanSynced();
            var router = new OutboxRouter(_db, new SyncQueueRepository(_db), _sink,
                NullLogger<OutboxRouter>.Instance, new[] { "sales" });

            int shipped = await router.TickAsync(2, CancellationToken.None);

            shipped.Should().Be(1);
            _sink.Table("sales").Keys.Should().Equal(sale.JournalNo);
            new SyncQueueRepository(_db).GetPendingCloud(100).Select(e => e.TableName)
                .Should().OnlyContain(t => t == "products", "out-of-scope rows stay queued for a later phase");
        }

        [Test]
        public void OutboxTableList_Parses()
        {
            new CloudSyncConfig().OutboxTableList().Should().Equal("sales");
            new CloudSyncConfig { OutboxTables = " sales, products ,sales" }.OutboxTableList().Should().Equal("sales", "products");
            new CloudSyncConfig { OutboxTables = "*" }.OutboxTableList().Should().BeNull();
            new CloudSyncConfig { OutboxTables = "" }.OutboxTableList().Should().BeEmpty();
        }

        [Test]
        public void ValidateWorkerConfig_ReportsWhatIsMissing()
        {
            Program.ValidateWorkerConfig(new CloudSyncConfig()).Should().Contain("SupabaseConnectionString");
            Program.ValidateWorkerConfig(new CloudSyncConfig { SupabaseConnectionString = "Host=x" })
                .Should().Contain("KasirDbPath");
            Program.ValidateWorkerConfig(new CloudSyncConfig { SupabaseConnectionString = "Host=x", KasirDbPath = "/nope/kasir.db" })
                .Should().Contain("does not exist");
        }

        [Test]
        public async Task DependencyInjection_ResolvesTheWorkerGraph()
        {
            string path = Path.Combine(Path.GetTempPath(), "wp02-di-" + Guid.NewGuid().ToString("N") + ".db");
            try
            {
                using (var file = new SqliteConnection("Data Source=" + path))
                {
                    file.Open();
                    using var cmd = file.CreateCommand();
                    cmd.CommandText = "CREATE TABLE config (id INTEGER PRIMARY KEY, key TEXT UNIQUE, value TEXT, description TEXT);";
                    cmd.ExecuteNonQuery();
                }

                var services = new ServiceCollection();
                services.AddLogging();
                services.Configure<CloudSyncConfig>(c =>
                {
                    c.SupabaseConnectionString = "Host=127.0.0.1;Port=1;Database=x;Username=x;Password=x";
                    c.KasirDbPath = path;
                });
                Program.AddWorkerServices(services);
                Program.ValidateWorkerConfig(new CloudSyncConfig
                {
                    SupabaseConnectionString = "Host=x", KasirDbPath = path
                }).Should().BeNull();

                await using var sp = services.BuildServiceProvider();
                var hosted = sp.GetServices<IHostedService>().ToList();
                hosted.Should().ContainSingle().Which.Should().BeOfType<CloudSyncWorker>();
                sp.GetRequiredService<IMirrorSink>().Should().BeOfType<GenericSink>();
                sp.GetRequiredService<IPullService>().Should().BeOfType<PullService>("the hub applies dashboard requests (WP-04)");
                sp.GetRequiredService<IPosRequestSource>().Should().BeOfType<PostgresPosRequestSource>();
                sp.GetRequiredService<OutboxRouter>().AllowedTables.Should().Equal("sales");
                sp.GetRequiredService<SqliteConnection>().State.Should().Be(System.Data.ConnectionState.Open);

                var pushOnly = new ServiceCollection();
                pushOnly.AddLogging();
                pushOnly.Configure<CloudSyncConfig>(c =>
                {
                    c.SupabaseConnectionString = "Host=127.0.0.1;Port=1;Database=x;Username=x;Password=x";
                    c.KasirDbPath = path;
                    c.PullEnabled = false;
                });
                Program.AddWorkerServices(pushOnly);
                await using var sp2 = pushOnly.BuildServiceProvider();
                sp2.GetRequiredService<IPullService>().Should().BeOfType<NoOpPullService>("CloudSync:PullEnabled=false");
            }
            finally
            {
                SqliteConnection.ClearAllPools();
                try { File.Delete(path); } catch (IOException) { }
            }
        }
    }
}
