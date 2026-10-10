using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Kasir.CloudSync.Push;
using Kasir.CloudSync.Tests.TestHelpers;
using Kasir.Data.Repositories;
using Kasir.Models;
using Kasir.Services;
using Kasir.Utils;
using Microsoft.Data.Sqlite;
using NUnit.Framework;

namespace Kasir.CloudSync.Tests.Push
{
    // WP-02 WatermarkPusher: reads stock_movements / shifts above the stored
    // watermark and upserts them into the mirror, bypassing sync_queue.
    [TestFixture]
    public class WatermarkPusherTests
    {
        private SqliteConnection _db;
        private InMemoryMirrorSink _sink;
        private WatermarkPusher _pusher;
        private ConfigRepository _config;

        private const string SmKey = WatermarkPusher.StockMovementsWatermarkKey;
        private const string ShKey = WatermarkPusher.ShiftsWatermarkKey;

        private sealed class FixedClock : IClock
        {
            public FixedClock(DateTime now) { Now = now; }
            public DateTime Now { get; }
            public DateTime UtcNow => Now.AddHours(-7);
        }

        [SetUp]
        public void SetUp()
        {
            _db = TestDb.Create();
            _sink = new InMemoryMirrorSink();
            _pusher = new WatermarkPusher(_db, _sink, null);
            _config = new ConfigRepository(_db);
            _config.Set("register_id", "01");
        }

        [TearDown]
        public void TearDown()
        {
            _db.Close();
            _db.Dispose();
        }

        private void InsertMovement(long id, int qtyIn = 0, int qtyOut = 100, string journal = null,
            string product = "P001", string type = "SALE", string createdAt = "2026-10-09 15:00:00")
        {
            using var cmd = _db.CreateCommand();
            cmd.CommandText =
                @"INSERT INTO stock_movements (id, product_code, journal_no, movement_type, doc_date, period_code,
                    location_code, qty_in, qty_out, val_in, val_out, cost_price, changed_at, created_at)
                  VALUES (@id, @p, @j, @t, '2026-10-09', '202610', 'T', @qi, @qo, 0, 0, 0, @c, @c)";
            cmd.Parameters.AddWithValue("@id", id);
            cmd.Parameters.AddWithValue("@p", product);
            cmd.Parameters.AddWithValue("@j", journal ?? "KLR-01-2610-" + id.ToString("0000"));
            cmd.Parameters.AddWithValue("@t", type);
            cmd.Parameters.AddWithValue("@qi", qtyIn);
            cmd.Parameters.AddWithValue("@qo", qtyOut);
            cmd.Parameters.AddWithValue("@c", createdAt);
            cmd.ExecuteNonQuery();
        }

        private void InsertShift(long id, string status = "O", string closedAt = null)
        {
            using var cmd = _db.CreateCommand();
            cmd.CommandText =
                @"INSERT INTO shifts (id, register_id, shift_number, cashier_id, opened_at, closed_at, opening_cash, status)
                  VALUES (@id, '01', @n, 1, @o, @c, 50000000, @s)";
            cmd.Parameters.AddWithValue("@id", id);
            cmd.Parameters.AddWithValue("@n", id.ToString());
            cmd.Parameters.AddWithValue("@o", "2026-10-09 07:0" + (id % 10) + ":00");
            cmd.Parameters.AddWithValue("@c", (object)closedAt ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@s", status);
            cmd.ExecuteNonQuery();
        }

        private void CloseShift(long id)
        {
            using var cmd = _db.CreateCommand();
            cmd.CommandText = "UPDATE shifts SET status = 'C', closed_at = '2026-10-09 21:00:00', closing_cash = 90000000 WHERE id = @id";
            cmd.Parameters.AddWithValue("@id", id);
            cmd.ExecuteNonQuery();
        }

        private System.Collections.Generic.Dictionary<string, System.Collections.Generic.IDictionary<string, object>> Cloud(string t) =>
            _sink.Table(t);

        private long Wm(string key) => long.Parse(_config.Get(key) ?? "0");

        // ---------- stock_movements ----------

        [Test]
        public void Schema_SeedsWatermarksAtZero()
        {
            _config.Get(SmKey).Should().Be("0");
            _config.Get(ShKey).Should().Be("0");
        }

        [Test]
        public async Task ReadsOnlyRowsAboveWatermark()
        {
            for (int i = 1; i <= 5; i++) InsertMovement(i);
            _config.Set(SmKey, "3");

            var r = await _pusher.PushTableAsync("stock_movements", SmKey, 100, CancellationToken.None);

            r.Pushed.Should().Be(2);
            Cloud("stock_movements").Keys.Should().BeEquivalentTo(new[] { "4", "5" });
        }

        [Test]
        public async Task WatermarkAdvancesAfterSuccessfulPush()
        {
            InsertMovement(10);
            InsertMovement(11);

            var r = await _pusher.PushTableAsync("stock_movements", SmKey, 100, CancellationToken.None);

            r.WatermarkBefore.Should().Be(0);
            r.WatermarkAfter.Should().Be(11);
            Wm(SmKey).Should().Be(11);

            InsertMovement(12);
            var r2 = await _pusher.PushTableAsync("stock_movements", SmKey, 100, CancellationToken.None);
            r2.Pushed.Should().Be(1, "only the new row is above the watermark");
            Wm(SmKey).Should().Be(12);
        }

        [Test]
        public async Task WatermarkStaysOnFailure_AndRetrySendsTheSameRows()
        {
            InsertMovement(1);
            InsertMovement(2);
            _sink.FailNext = true;

            var r = await _pusher.PushTableAsync("stock_movements", SmKey, 100, CancellationToken.None);

            r.Failed.Should().BeTrue();
            r.Error.Should().Contain("outage");
            Wm(SmKey).Should().Be(0, "nothing was confirmed");
            Cloud("stock_movements").Should().BeEmpty();

            var retry = await _pusher.PushTableAsync("stock_movements", SmKey, 100, CancellationToken.None);
            retry.Failed.Should().BeFalse();
            retry.Pushed.Should().Be(2);
            Wm(SmKey).Should().Be(2);
        }

        [Test]
        public async Task EmptyTable_SkipsTheSink()
        {
            var r = await _pusher.PushTableAsync("stock_movements", SmKey, 100, CancellationToken.None);

            r.Read.Should().Be(0);
            _sink.Calls.Should().Be(0, "no rows = no Supabase round trip");
            Wm(SmKey).Should().Be(0);
        }

        [Test]
        public async Task ReplayIsIdempotent_NoDuplicates()
        {
            for (int i = 1; i <= 3; i++) InsertMovement(i);
            await _pusher.PushTableAsync("stock_movements", SmKey, 100, CancellationToken.None);

            // Crash between upsert and watermark write: the same rows go again.
            _config.Set(SmKey, "0");
            var replay = await _pusher.PushTableAsync("stock_movements", SmKey, 100, CancellationToken.None);

            replay.Pushed.Should().Be(3);
            replay.Conflicts.Should().BeEmpty("the same movement may overwrite itself");
            Cloud("stock_movements").Should().HaveCount(3);
        }

        [Test]
        public async Task DashboardOriginatedIds_AreNeverPushedBack()
        {
            InsertMovement(7);
            InsertMovement(WatermarkPusher.DashboardIdFloor, type: "OPNAME", journal: "OPNAME:s1:P001");
            InsertMovement(WatermarkPusher.DashboardIdFloor + 1, type: "PURCHASE", qtyIn: 500, qtyOut: 0, journal: "PURCHASE:r1");

            await _pusher.PushTableAsync("stock_movements", SmKey, 100, CancellationToken.None);

            Cloud("stock_movements").Keys.Should().BeEquivalentTo(new[] { "7" });
            Wm(SmKey).Should().Be(7, "the watermark never enters the reserved range");
        }

        [Test]
        public async Task ZeroQtyRows_AreSkipped_ButPassed()
        {
            InsertMovement(1, qtyIn: 0, qtyOut: 0);
            InsertMovement(2);

            var r = await _pusher.PushTableAsync("stock_movements", SmKey, 100, CancellationToken.None);

            Cloud("stock_movements").Keys.Should().BeEquivalentTo(new[] { "2" });
            r.WatermarkAfter.Should().Be(2);
        }

        [Test]
        public async Task CreatedAt_IsPushedAsStoreTimeInstant()
        {
            InsertMovement(1, createdAt: "2026-10-09 15:00:00");

            await _pusher.PushTableAsync("stock_movements", SmKey, 100, CancellationToken.None);

            var row = Cloud("stock_movements")["1"];
            row["created_at"].Should().Be(new DateTimeOffset(2026, 10, 9, 7, 0, 0, TimeSpan.Zero),
                "15:00 WITA on the register is 07:00 UTC");
            row["changed_at"].Should().Be(new DateTimeOffset(2026, 10, 9, 7, 0, 0, TimeSpan.Zero));
        }

        [Test]
        public async Task LegacyBigIds_AboveInt32_ArePushedAs64Bit()
        {
            InsertMovement(4_294_956_567L, journal: "GHIST-1", type: "SALE");

            await _pusher.PushTableAsync("stock_movements", SmKey, 100, CancellationToken.None);

            Cloud("stock_movements")["4294956567"]["id"].Should().Be(4_294_956_567L);
            Wm(SmKey).Should().Be(4_294_956_567L);
        }

        [Test]
        public async Task CloudIdHeldByDifferentRow_IsNotOverwritten_AndRecorded()
        {
            // The cloud already has a legacy GHIST row whose 32-bit hash id equals POS rowid 40.
            var legacy = new System.Collections.Generic.Dictionary<string, object>
            {
                ["id"] = 40L, ["journal_no"] = "GHIST-X", ["product_code"] = "OTHER", ["movement_type"] = "SALE"
            };
            Cloud("stock_movements")["40"] = legacy;
            InsertMovement(40);
            InsertMovement(41);

            var r = await _pusher.PushTableAsync("stock_movements", SmKey, 100, CancellationToken.None);

            r.Conflicts.Should().Equal(40L);
            r.Pushed.Should().Be(1);
            Cloud("stock_movements")["40"]["journal_no"].Should().Be("GHIST-X", "the legacy row is kept");
            Cloud("stock_movements")["41"]["journal_no"].Should().Be("KLR-01-2610-0041");
            _config.Get(WatermarkPusher.ConflictsKey).Should().Be("40");
            Wm(SmKey).Should().Be(41, "a collision must not stall the push");
        }

        [Test]
        public async Task CloudRowWithSameIdentity_IsUpdated()
        {
            // The initial load already copied this register's movement 5 to the cloud.
            Cloud("stock_movements")["5"] = new System.Collections.Generic.Dictionary<string, object>
            {
                ["id"] = 5L, ["journal_no"] = "KLR-01-2610-0005", ["product_code"] = "P001",
                ["movement_type"] = "SALE", ["created_at"] = null
            };
            InsertMovement(5);

            var r = await _pusher.PushTableAsync("stock_movements", SmKey, 100, CancellationToken.None);

            r.Conflicts.Should().BeEmpty();
            Cloud("stock_movements")["5"]["created_at"].Should().NotBeNull("the real timestamp replaces the null");
        }

        [Test]
        public async Task Drain_PushesInBatches_UntilCaughtUp()
        {
            for (int i = 1; i <= 25; i++) InsertMovement(i);

            var r = await _pusher.DrainAsync("stock_movements", SmKey, batchSize: 10, maxBatches: 10, CancellationToken.None);

            r.Pushed.Should().Be(25);
            r.Batches.Should().Be(3);
            _sink.BatchSizes.Should().Equal(10, 10, 5);
            Wm(SmKey).Should().Be(25);
        }

        [Test]
        public async Task Drain_StopsAtMaxBatches()
        {
            for (int i = 1; i <= 25; i++) InsertMovement(i);

            var r = await _pusher.DrainAsync("stock_movements", SmKey, batchSize: 10, maxBatches: 2, CancellationToken.None);

            r.Pushed.Should().Be(20);
            Wm(SmKey).Should().Be(20, "the rest goes next tick");
        }

        [Test]
        public async Task Drain_StopsOnFailure()
        {
            for (int i = 1; i <= 25; i++) InsertMovement(i);
            await _pusher.PushTableAsync("stock_movements", SmKey, 10, CancellationToken.None);
            _sink.FailNext = true;

            var r = await _pusher.DrainAsync("stock_movements", SmKey, 10, 10, CancellationToken.None);

            r.Failed.Should().BeTrue();
            Wm(SmKey).Should().Be(10);
        }

        [Test]
        public async Task PosSale_IsPushedWithX100Qty_AndStoreTime()
        {
            // Acceptance: a sale in the POS creates a stock_movements row in the mirror
            // with x100 qty and the store-time (WITA) instant it happened.
            new ProductRepository(_db).Insert(new Product
            {
                ProductCode = "P001", Name = "LAMPU 10W", Price = 2500000, CostPrice = 1800000,
                Status = "A", OpenPrice = "N", VatFlag = "N", LuxuryTaxFlag = "N", IsConsignment = "N"
            });
            var sales = new SalesService(_db, new FixedClock(new DateTime(2026, 10, 9, 15, 0, 0)));
            sales.SetCashier("ADM", 1);
            sales.AddItem("P001", 2);
            var sale = sales.CompleteSale(10000000, 0, 0, "", "", "");
            // created_at is datetime('now','localtime'); pin it to the sale time.
            using (var cmd = _db.CreateCommand())
            {
                cmd.CommandText = "UPDATE stock_movements SET created_at = '2026-10-09 15:00:00' WHERE journal_no = @j";
                cmd.Parameters.AddWithValue("@j", sale.JournalNo);
                cmd.ExecuteNonQuery();
            }

            await _pusher.PushTableAsync("stock_movements", SmKey, 100, CancellationToken.None);

            var row = Cloud("stock_movements").Values.Single();
            row["journal_no"].Should().Be(sale.JournalNo);
            row["movement_type"].Should().Be("SALE");
            row["qty_out"].Should().Be(200L, "2 units in the x100 ledger");
            row["val_out"].Should().Be(3600000L, "2 x Rp 18.000");
            row["created_at"].Should().Be(new DateTimeOffset(2026, 10, 9, 7, 0, 0, TimeSpan.Zero));
        }

        [Test]
        public async Task UnknownTable_Throws()
        {
            Func<Task> act = () => _pusher.PushTableAsync("products", "k", 10, CancellationToken.None);
            await act.Should().ThrowAsync<ArgumentException>();
        }

        // ---------- shifts ----------

        [Test]
        public async Task Shifts_PushAboveWatermark()
        {
            InsertShift(1, "C", "2026-10-08 21:00:00");
            InsertShift(2, "C", "2026-10-09 13:00:00");
            _config.Set(ShKey, "1");

            var r = await _pusher.PushTableAsync("shifts", ShKey, 100, CancellationToken.None);

            r.Pushed.Should().Be(1);
            Cloud("shifts").Keys.Should().BeEquivalentTo(new[] { "2|01" });
            Cloud("shifts")["2|01"]["closed_at"].Should().Be("2026-10-09 13:00:00", "shifts keep POS local text");
            Wm(ShKey).Should().Be(2);
        }

        [Test]
        public async Task Shifts_OpenShift_IsRepushedUntilClosed()
        {
            InsertShift(1, "O");
            await _pusher.PushTableAsync("shifts", ShKey, 100, CancellationToken.None);
            Cloud("shifts")["1|01"]["status"].Should().Be("O");
            _config.Get(WatermarkPusher.OpenShiftsKey).Should().Be("1");

            CloseShift(1);
            var r = await _pusher.PushTableAsync("shifts", ShKey, 100, CancellationToken.None);

            r.Pushed.Should().Be(1, "the closed state is sent although id 1 is at the watermark");
            Cloud("shifts")["1|01"]["status"].Should().Be("C");
            Cloud("shifts")["1|01"]["closing_cash"].Should().Be(90000000L);
            _config.Get(WatermarkPusher.OpenShiftsKey).Should().BeEmpty();

            var idle = await _pusher.PushTableAsync("shifts", ShKey, 100, CancellationToken.None);
            idle.Read.Should().Be(0);
        }

        [Test]
        public async Task Shifts_FailureKeepsWatermarkAndOpenSet()
        {
            InsertShift(1, "O");
            await _pusher.PushTableAsync("shifts", ShKey, 100, CancellationToken.None);
            CloseShift(1);
            InsertShift(2, "O");
            _sink.FailNext = true;

            var r = await _pusher.PushTableAsync("shifts", ShKey, 100, CancellationToken.None);

            r.Failed.Should().BeTrue();
            Wm(ShKey).Should().Be(1);
            _config.Get(WatermarkPusher.OpenShiftsKey).Should().Be("1");
        }
    }
}
