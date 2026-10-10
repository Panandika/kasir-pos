using System;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Kasir.CloudSync.Pull;
using Kasir.CloudSync.Tests.TestHelpers;
using Kasir.Data;
using Kasir.Data.Repositories;
using Kasir.Models;
using Kasir.Services;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;
using NUnit.Framework;

namespace Kasir.CloudSync.Tests.Pull
{
    // Follow-up item 11 (D22 / WP-14): "Isi per dus?" saved in the dashboard reaches the
    // hub as a PRODUCT_PACK request (dashboard 0075 set_product_pack). The hub writes
    // products.unit2 / conversion1 once, queues the row for LAN sync, never lets an older
    // pack overwrite a newer one, waits on an NP code until its NEW_PRODUCT is in, and
    // rejects non-stock codes.
    [TestFixture]
    public class PullProductPackTests
    {
        private static readonly TimeSpan Wita = TimeSpan.FromHours(8);

        private SqliteConnection _db;
        private InMemoryPosRequestSource _source;
        private PullService _pull;

        [SetUp]
        public void SetUp()
        {
            _db = TestDb.Create();
            new ConfigRepository(_db).Set("register_id", "01");
            SeedProduct("P001");
            SeedProduct("P002");
            SeedProduct("1");
            _source = new InMemoryPosRequestSource();
            _pull = new PullService(_db, _source, NullLogger<PullService>.Instance, 200,
                () => new DateTimeOffset(2026, 10, 10, 4, 0, 0, TimeSpan.Zero));
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
                ProductCode = code, Name = code, Price = 500000, CostPrice = 300000,
                Status = "A", OpenPrice = "N", VatFlag = "N", LuxuryTaxFlag = "N", IsConsignment = "N"
            });
        }

        private long Scalar(string sql) => SqlHelper.ExecuteScalar<long>(_db, sql);
        private string Text(string sql) => SqlHelper.ExecuteScalar<string>(_db, sql);
        private string PackOf(string code) =>
            Text($"SELECT COALESCE(unit2, '-') || ' x ' || conversion1 FROM products WHERE product_code = '{code}'");

        private static DateTimeOffset At(int hour, int minute = 0) => new DateTimeOffset(2026, 10, 10, hour, minute, 0, Wita);

        // Key as the dashboard writes it: PRODUCT_PACK:<code>:<yyyyMMddHHmmssffffff>.
        private PosStockRequest Pack(string code, string unit2, long conversion1, DateTimeOffset at, string payload = null) =>
            _source.Add(new PosStockRequest
            {
                RequestKind = PosRequestKinds.ProductPack,
                IdempotencyKey = "PRODUCT_PACK:" + code + ":" + at.ToString("yyyyMMddHHmmss") + "000000",
                ProductCode = code,
                PayloadJson = payload ?? "{\"unit2\":\"" + unit2 + "\",\"conversion1\":" + conversion1
                              + ",\"previous_unit2\":null,\"previous_conversion1\":100,\"source\":\"opname\"}",
                HappenedAt = at, CreatedAt = at
            });

        [Test]
        public async Task product_pack_sets_unit2_and_conversion1_and_queues_the_row_for_lan_sync()
        {
            var r = Pack("P001", "dus", 2400, At(9));
            long queuedBefore = Scalar("SELECT COUNT(*) FROM sync_queue WHERE table_name = 'products' AND record_key = 'P001' AND operation = 'U'");

            (await _pull.TickAsync(CancellationToken.None)).Should().Be(1);

            PackOf("P001").Should().Be("DUS x 2400");
            _source.RowOf(r.Id).AppliedAt.Should().NotBeNull();
            Scalar("SELECT COUNT(*) FROM applied_requests WHERE request_kind = 'PRODUCT_PACK'").Should().Be(1);
            Scalar("SELECT COUNT(*) FROM sync_queue WHERE table_name = 'products' AND record_key = 'P001' AND operation = 'U'")
                .Should().Be(queuedBefore + 1, "unit2 / conversion1 are not watched by trg_products_sync_u");
            Scalar("SELECT COUNT(*) FROM stock_movements").Should().Be(0, "a pack size moves no stock");
            PackOf("P002").Should().Be("- x 100");
        }

        [Test]
        public async Task replay_after_a_lost_mark_changes_nothing_and_is_re_marked()
        {
            var r = Pack("P001", "DUS", 2400, At(9));
            _source.FailMarks = 1;
            Func<Task> first = () => _pull.TickAsync(CancellationToken.None);
            await first.Should().ThrowAsync<PullMarkException>();
            PackOf("P001").Should().Be("DUS x 2400");
            long queued = Scalar("SELECT COUNT(*) FROM sync_queue WHERE record_key = 'P001'");

            (await _pull.TickAsync(CancellationToken.None)).Should().Be(0);
            _pull.LastResult.Remarked.Should().Equal(r.Id);
            _source.RowOf(r.Id).AppliedAt.Should().NotBeNull();
            Scalar("SELECT COUNT(*) FROM sync_queue WHERE record_key = 'P001'").Should().Be(queued);
        }

        [Test]
        public async Task two_changes_in_one_tick_end_on_the_latest()
        {
            Pack("P001", "DUS", 4000, At(9));
            Pack("P001", "BAL", 2450, At(10));

            (await _pull.TickAsync(CancellationToken.None)).Should().Be(2);

            PackOf("P001").Should().Be("BAL x 2450");
        }

        [Test]
        public async Task an_older_pack_applied_after_a_newer_one_is_a_no_op()
        {
            var newer = Pack("P001", "DUS", 4800, At(11));
            (await _pull.TickAsync(CancellationToken.None)).Should().Be(1);

            // The older change reaches the hub late (e.g. it was failing to apply before).
            var older = Pack("P001", "DUS", 4000, At(9));
            (await _pull.TickAsync(CancellationToken.None)).Should().Be(1);

            PackOf("P001").Should().Be("DUS x 4800", "an old pack must never come back");
            _source.RowOf(older.Id).AppliedAt.Should().NotBeNull("marked applied (superseded)");
            _source.RowOf(newer.Id).AppliedAt.Should().NotBeNull();
        }

        [Test]
        public async Task same_pack_already_here_is_a_marked_no_op()
        {
            SqlHelper.ExecuteNonQuery(_db, "UPDATE products SET unit2 = 'DUS', conversion1 = 2400 WHERE product_code = 'P001'");
            long queued = Scalar("SELECT COUNT(*) FROM sync_queue WHERE record_key = 'P001'");
            var r = Pack("P001", "DUS", 2400, At(9));

            (await _pull.TickAsync(CancellationToken.None)).Should().Be(1);

            _source.RowOf(r.Id).AppliedAt.Should().NotBeNull();
            Scalar("SELECT COUNT(*) FROM sync_queue WHERE record_key = 'P001'").Should().Be(queued, "nothing changed");
        }

        [Test]
        public async Task an_unknown_product_waits()
        {
            var r = Pack("P404", "DUS", 2400, At(9));

            (await _pull.TickAsync(CancellationToken.None)).Should().Be(0);

            _pull.LastResult.Failed.Should().Equal(r.Id);
            _source.RowOf(r.Id).AppliedAt.Should().BeNull();
            _source.RowOf(r.Id).FailedAt.Should().BeNull("waiting is not a rejection");
        }

        [Test]
        public async Task an_np_code_waits_for_its_new_product_then_applies_after_it()
        {
            var pack = Pack("NP0021", "DUS", 1200, At(9));
            (await _pull.TickAsync(CancellationToken.None)).Should().Be(0);
            _pull.LastResult.Failed.Should().Equal(pack.Id);

            _source.Add(new PosStockRequest
            {
                RequestKind = PosRequestKinds.NewProduct, IdempotencyKey = "NEW_PRODUCT:NP0021", ProductCode = "NP0021",
                PayloadJson = "{\"name\":\"MIE BARU\",\"unit\":\"PCS\",\"price\":200000,\"cost_price\":150000}",
                HappenedAt = At(8), CreatedAt = At(8)
            });
            (await _pull.TickAsync(CancellationToken.None)).Should().Be(2);

            PackOf("NP0021").Should().Be("DUS x 1200");
        }

        [Test]
        public async Task a_non_stock_code_is_rejected()
        {
            var r = Pack("1", "DUS", 2400, At(9));

            (await _pull.TickAsync(CancellationToken.None)).Should().Be(0);

            _pull.LastResult.Rejected.Should().Equal(r.Id);
            _source.RowOf(r.Id).FailedReason.Should().Contain("is not a stock item");
            PackOf("1").Should().Be("- x 100");
        }

        [TestCase("{\"conversion1\":2400}", "payload.unit2 (pack unit) is required")]
        [TestCase("{\"unit2\":\"KARTONS\",\"conversion1\":2400}", "payload.unit2 is longer than 6")]
        [TestCase("{\"unit2\":\"DUS\"}", "payload.conversion1 (x100 stock units per pack) is required")]
        [TestCase("{\"unit2\":\"DUS\",\"conversion1\":100}", "payload.conversion1 (x100 stock units per pack) must be > 100")]
        public async Task bad_payload_is_invalid_and_stays_pending(string payload, string reason)
        {
            var r = Pack("P001", null, 0, At(9), payload);

            (await _pull.TickAsync(CancellationToken.None)).Should().Be(0);

            _pull.LastResult.Failed.Should().Equal(r.Id);
            _source.RowOf(r.Id).AppliedAt.Should().BeNull();
            PackOf("P001").Should().Be("- x 100");
            var ex = Assert.Throws<PosRequestApplyException>(() => new PosRequestApplier(_db).Apply(r));
            ex.Message.Should().EndWith(reason);
        }

        [Test]
        public void pack_sorts_with_product_status_and_waits_like_stock_on_np_codes()
        {
            PosRequestKinds.Priority(PosRequestKinds.ProductPack).Should().Be(PosRequestKinds.Priority(PosRequestKinds.ProductStatus));
            PosRequestKinds.Priority(PosRequestKinds.ProductPack).Should().BeLessThan(PosRequestKinds.Priority(PosRequestKinds.Purchase));
            PosRequestKinds.NeedsDashboardProduct(PosRequestKinds.ProductPack).Should().BeTrue();
            PosRequestKinds.MovesStock(PosRequestKinds.ProductPack).Should().BeFalse();
            PostgresPosRequestSource.PendingSql.Should().Contain("WHEN 'PRODUCT_PACK' THEN 1");
        }
    }
}
