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
    // Follow-up item 7 (K1/K4): code 1/2/44/99 and the category keys AL/AT/PR/PL/MY/LL
    // are not stock items. A dashboard OPNAME / PURCHASE / RETURN_OUT on one of them is
    // rejected for good: nothing is written to kasir.db, the request is marked failed in
    // Supabase with a clear reason (failed_at, dashboard 0072), it is not fetched again,
    // the tick does not throw, and other requests carry on.
    [TestFixture]
    public class PullNonStockTests
    {
        private static readonly TimeSpan Wita = TimeSpan.FromHours(8);

        private SqliteConnection _db;
        private InMemoryPosRequestSource _source;
        private PullService _pull;

        [SetUp]
        public void SetUp()
        {
            _db = TestDb.Create();
            var cfg = new ConfigRepository(_db);
            cfg.Set("register_id", "01");
            cfg.Set(InventoryService.CostEngineOwnsCostPriceKey, "true");
            foreach (var code in new[] { "P001", "1", "2", "AL", "LL" })
                SeedProduct(code);
            // "44" and "99" are deliberately NOT in kasir.db: a missing row must not defer.
            _source = new InMemoryPosRequestSource();
            _pull = new PullService(_db, _source, NullLogger<PullService>.Instance, 200,
                () => new DateTimeOffset(2026, 10, 10, 2, 0, 0, TimeSpan.Zero));
        }

        [TearDown]
        public void TearDown()
        {
            _db.Close();
            _db.Dispose();
        }

        private void SeedProduct(string code)
        {
            var repo = new ProductRepository(_db);
            if (repo.GetByCode(code) != null) return;
            repo.Insert(new Product
            {
                ProductCode = code, Name = code, Price = 500000, CostPrice = 300000,
                Status = "A", OpenPrice = "N", VatFlag = "N", LuxuryTaxFlag = "N", IsConsignment = "N"
            });
        }

        private long Scalar(string sql) => SqlHelper.ExecuteScalar<long>(_db, sql);

        private static DateTimeOffset At(int hour) => new DateTimeOffset(2026, 10, 10, hour, 0, 0, Wita);

        private PosStockRequest Add(string kind, string key, string code, int qty, long? unitCost = 500000,
            string doc = "RCV-NS-1", int hour = 9) =>
            _source.Add(new PosStockRequest
            {
                RequestKind = kind, IdempotencyKey = kind + ":" + key, ProductCode = code,
                Qty = qty, UnitCost = kind == PosRequestKinds.Opname ? null : unitCost,
                VendorCode = "V001", DocNo = doc, PayloadJson = "{\"po_no\":\"DPO-1\"}",
                HappenedAt = At(hour), CreatedAt = At(hour)
            });

        private void NothingWritten()
        {
            Scalar("SELECT COUNT(*) FROM stock_movements").Should().Be(0);
            Scalar("SELECT COUNT(*) FROM purchases").Should().Be(0);
            Scalar("SELECT COUNT(*) FROM purchase_items").Should().Be(0);
            Scalar("SELECT COUNT(*) FROM stock_adjustments").Should().Be(0);
            Scalar("SELECT COUNT(*) FROM applied_requests").Should().Be(0);
        }

        [TestCase("OPNAME", "1")]
        [TestCase("OPNAME", "AL")]
        [TestCase("PURCHASE", "1")]
        [TestCase("PURCHASE", "2")]
        [TestCase("PURCHASE", "LL")]
        [TestCase("RETURN_OUT", "AL")]
        public async Task stock_request_on_a_non_stock_code_is_marked_failed_with_a_reason(string kind, string code)
        {
            var req = Add(kind, "ns", code, 500);

            (await _pull.TickAsync(CancellationToken.None)).Should().Be(0, "nothing was applied");

            NothingWritten();
            var row = _source.RowOf(req.Id);
            row.AppliedAt.Should().BeNull("a rejected request is never marked applied");
            row.FailedAt.Should().NotBeNull("the hub marks it failed in Supabase");
            row.FailedBy.Should().Be("01");
            row.FailedReason.Should().Be(kind + " " + kind + ":ns: " + code
                + " is not a stock item (manual price code / category key); the POS never counts, buys or returns it");
            _pull.LastResult.Rejected.Should().Equal(req.Id);
            _pull.LastResult.Failed.Should().BeEmpty();

            (await _source.FetchPendingAsync(200, CancellationToken.None)).Should().BeEmpty("a failed row is no longer pending");
            await _pull.TickAsync(CancellationToken.None);
            _pull.LastResult.Fetched.Should().Be(0, "it is not retried every tick");
        }

        [TestCase("44")]
        [TestCase("99")]
        public async Task a_manual_code_missing_from_kasir_db_is_rejected_not_deferred(string code)
        {
            new ProductRepository(_db).GetByCode(code).Should().BeNull("fixture: no local row");
            var req = Add(PosRequestKinds.Purchase, "m" + code, code, 100);

            await _pull.TickAsync(CancellationToken.None);

            _source.RowOf(req.Id).FailedAt.Should().NotBeNull();
            _source.RowOf(req.Id).FailedReason.Should().Contain(code + " is not a stock item");
            NothingWritten();
        }

        [Test]
        public async Task other_requests_carry_on_and_the_product_is_not_held_back()
        {
            var bad = Add(PosRequestKinds.Purchase, "bad", "1", 100, hour: 8);
            var badOpname = Add(PosRequestKinds.Opname, "bad", "1", 300, hour: 9);
            var good = Add(PosRequestKinds.Purchase, "good", "P001", 1000, 400000, doc: "RCV-OK-1", hour: 10);

            (await _pull.TickAsync(CancellationToken.None)).Should().Be(1);

            _pull.LastResult.Rejected.Should().BeEquivalentTo(new[] { bad.Id, badOpname.Id },
                "the OPNAME after a rejected PURCHASE is judged on its own (nothing was written for it)");
            _pull.LastResult.HeldBack.Should().BeEmpty();
            _source.RowOf(good.Id).AppliedAt.Should().NotBeNull();
            new InventoryService(_db).GetStockOnHand("P001").Should().Be(1000);
            new InventoryService(_db).GetStockOnHand("1").Should().Be(0, "code 1 never gets a movement");
            new ProductRepository(_db).GetByCode("1").CostPrice.Should().Be(300000, "its cost is never estimated from the request");
        }

        [Test]
        public async Task non_stock_kinds_on_a_non_stock_code_still_apply()
        {
            var req = _source.Add(new PosStockRequest
            {
                RequestKind = PosRequestKinds.ProductStatus, IdempotencyKey = "PRODUCT_STATUS:1", ProductCode = "1",
                PayloadJson = "{\"status\":\"A\"}", HappenedAt = At(9), CreatedAt = At(9)
            });

            (await _pull.TickAsync(CancellationToken.None)).Should().Be(1);

            _source.RowOf(req.Id).AppliedAt.Should().NotBeNull();
            _source.RowOf(req.Id).FailedAt.Should().BeNull();
        }

        [Test]
        public async Task a_failed_mark_does_not_throw_and_retries_next_tick()
        {
            var req = Add(PosRequestKinds.Opname, "retry", "AL", 100);
            _source.FailFailedMarks = 1;

            Func<Task> tick = () => _pull.TickAsync(CancellationToken.None);
            await tick.Should().NotThrowAsync("a reject has nothing to undo locally; the worker must not back off");
            _pull.LastResult.Failed.Should().Equal(req.Id);
            _source.RowOf(req.Id).FailedAt.Should().BeNull();

            await _pull.TickAsync(CancellationToken.None);
            _pull.LastResult.Rejected.Should().Equal(req.Id);
            _source.RowOf(req.Id).FailedAt.Should().NotBeNull();
            NothingWritten();
        }

        [Test]
        public void applier_throws_a_rejected_exception_not_a_deferred_one()
        {
            var req = Add(PosRequestKinds.Purchase, "direct", "99", 100);

            var ex = FluentActions.Invoking(() => new PosRequestApplier(_db).Apply(req))
                .Should().Throw<PosRequestApplyException>().Which;
            ex.Rejected.Should().BeTrue();
            ex.Deferred.Should().BeFalse();
            NothingWritten();
        }

        [Test]
        public void is_non_stock_item_matches_the_dashboard_list()
        {
            foreach (var c in new[] { "1", "2", "44", "99", "AL", "AT", "PR", "PL", "MY", "LL", " 44 " })
                SalesService.IsNonStockItem(c).Should().BeTrue(c);
            foreach (var c in new[] { "P001", "11", "100", "al", "ALL", "", null })
                SalesService.IsNonStockItem(c).Should().BeFalse(c ?? "null");
        }
    }
}
