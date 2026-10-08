using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Kasir.CloudSync.Outbox;
using Kasir.CloudSync.Pull;
using Kasir.CloudSync.Push;
using Kasir.CloudSync.Tests.TestHelpers;
using Kasir.Data;
using Kasir.Data.Repositories;
using Kasir.Models;
using Kasir.Services;
using Kasir.Utils;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NUnit.Framework;

namespace Kasir.CloudSync.Tests.Pull
{
    // WP-04: the hub applies dashboard pos_stock_requests to kasir.db exactly once.
    // Edge cases (RALPLAN 6): #33 pull_applies_opname_once, #50 pull_applies_purchase_once,
    // #51 pull_movement_id_in_reserved_range, #53 pull_applies_vendor_bill_to_payables_register,
    // #12/#28 exact-time opname, plus replay, ordering and network failure mid-apply.
    [TestFixture]
    public class PullServiceTests
    {
        private static readonly TimeSpan Wib = TimeSpan.FromHours(7);
        private const long Floor = 5_000_000_000L;

        private SqliteConnection _db;
        private InMemoryPosRequestSource _source;
        private PullService _pull;

        private sealed class FixedClock : IClock
        {
            public DateTime Now { get; set; } = new DateTime(2026, 10, 9, 15, 0, 0);
            public DateTime UtcNow => Now.AddHours(-7);
        }

        [SetUp]
        public void SetUp()
        {
            _db = TestDb.Create();
            var cfg = new ConfigRepository(_db);
            cfg.Set("register_id", "01");
            cfg.Set(InventoryService.CostEngineOwnsCostPriceKey, "true");
            SeedProduct("P001", "A", 300000);
            SeedProduct("P002", "A", 100000);
            SeedProduct("P003", "I", 200000);
            _source = new InMemoryPosRequestSource();
            _pull = NewPull();
        }

        [TearDown]
        public void TearDown()
        {
            _db.Close();
            _db.Dispose();
        }

        private PullService NewPull() =>
            new PullService(_db, _source, NullLogger<PullService>.Instance, 200,
                () => new DateTimeOffset(2026, 10, 9, 9, 30, 0, TimeSpan.Zero));

        private void SeedProduct(string code, string status, long cost)
        {
            new ProductRepository(_db).Insert(new Product
            {
                ProductCode = code, Name = code, Price = 500000, CostPrice = cost,
                Status = status, OpenPrice = "N", VatFlag = "N", LuxuryTaxFlag = "N", IsConsignment = "N"
            });
        }

        // Opening stock written the way legacy rows sit in kasir.db (x100, early time).
        private void SeedStock(string code, int ledgerQty)
        {
            Exec($@"INSERT INTO stock_movements (product_code, journal_no, movement_type, doc_date, period_code,
                    qty_in, cost_price, changed_at, created_at)
                    VALUES ('{code}', 'GSMRY-2609', 'PURCHASE', '2026-09-30', '202609', {ledgerQty}, 300000,
                    '2026-09-30 20:00:00', '2026-09-30 20:00:00')");
        }

        private void Exec(string sql)
        {
            using var cmd = _db.CreateCommand();
            cmd.CommandText = sql;
            cmd.ExecuteNonQuery();
        }

        private long Scalar(string sql) => SqlHelper.ExecuteScalar<long>(_db, sql);
        private string Text(string sql) => SqlHelper.ExecuteScalar<string>(_db, sql);
        private int OnHand(string code) => new InventoryService(_db).GetStockOnHand(code);

        private static DateTimeOffset At(int hour, int minute = 0) =>
            new DateTimeOffset(2026, 10, 9, hour, minute, 0, Wib);

        private PosStockRequest Opname(string code, int counted, DateTimeOffset countedAt, long? unitCost = null,
            DateTimeOffset? createdAt = null) =>
            _source.Add(new PosStockRequest
            {
                RequestKind = "OPNAME", IdempotencyKey = "OPNAME:sess-okt:" + code, ProductCode = code,
                Qty = counted, UnitCost = unitCost, DocNo = "OPN-DB-OKT26",
                HappenedAt = countedAt, CreatedAt = createdAt ?? At(16)
            });

        private PosStockRequest Purchase(string lineId, string code, int qty, long unitCost, string doc = "RCV-0001",
            DateTimeOffset? createdAt = null) =>
            _source.Add(new PosStockRequest
            {
                RequestKind = "PURCHASE", IdempotencyKey = "PURCHASE:" + lineId, ProductCode = code,
                Qty = qty, UnitCost = unitCost, VendorCode = "V001", DocNo = doc,
                PayloadJson = "{\"po_no\":\"PO-0007\",\"receipt_line_id\":\"" + lineId + "\"}",
                HappenedAt = At(10), CreatedAt = createdAt ?? At(10)
            });

        private void SellAt(string code, int units, DateTime wibTime)
        {
            var sales = new SalesService(_db, new FixedClock { Now = wibTime });
            sales.SetCashier("ADM", 1);
            sales.AddItem(code, units);
            var sale = sales.CompleteSale(100000000, 0, 0, "", "", "");
            string ts = wibTime.ToString("yyyy-MM-dd HH:mm:ss");
            Exec($"UPDATE stock_movements SET created_at = '{ts}', changed_at = '{ts}' WHERE journal_no = '{sale.JournalNo}'");
        }

        // ---------- OPNAME ----------

        [Test]
        public async Task pull_applies_opname_once()
        {
            SeedStock("P001", 5000); // 50 on hand
            var req = Opname("P001", 4800, At(9), unitCost: 999999); // counted 48; bogus cost must be ignored

            (await _pull.TickAsync(CancellationToken.None)).Should().Be(1);
            (await _pull.TickAsync(CancellationToken.None)).Should().Be(0, "nothing pending after the mark");
            _source.RowOf(req.Id).AppliedAt = null; // Supabase shows it pending again (e.g. re-polled)
            (await _pull.TickAsync(CancellationToken.None)).Should().Be(0);

            OnHand("P001").Should().Be(4800);
            Scalar("SELECT COUNT(*) FROM stock_movements WHERE movement_type = 'OPNAME'").Should().Be(1);
            var m = new StockMovementRepository(_db).GetByJournal("OPN-DB-OKT26").Single();
            m.QtyOut.Should().Be(200);
            m.CostPrice.Should().Be(300000, "OB-14: the local average cost, not the request's unit_cost");
            m.ValOut.Should().Be(600000);
            Scalar("SELECT id FROM stock_movements WHERE movement_type = 'OPNAME'").Should().BeGreaterThanOrEqualTo(Floor);
            Text("SELECT changed_at FROM stock_movements WHERE movement_type = 'OPNAME'").Should().Be("2026-10-09 09:00:00");

            Text("SELECT doc_type FROM stock_adjustments WHERE journal_no = 'OPN-DB-OKT26'").Should().Be("OPNAME");
            Text("SELECT remark || ' ' || quantity || ' ' || value FROM stock_adjustment_items WHERE journal_no = 'OPN-DB-OKT26'")
                .Should().Be("SHORTAGE 200 600000");
            Scalar("SELECT total_value FROM stock_adjustments WHERE journal_no = 'OPN-DB-OKT26'").Should().Be(600000);
            Scalar("SELECT COUNT(*) FROM applied_requests WHERE request_kind = 'OPNAME'").Should().Be(1);

            var row = _source.RowOf(req.Id);
            row.AppliedAt.Should().NotBeNull("applied requests are marked in Supabase");
            row.AppliedBy.Should().Be("01");
        }

        [Test]
        public async Task exact_time_count0900_48_sale1500_2_onhand46()
        {
            SeedStock("P001", 5000);
            SellAt("P001", 2, new DateTime(2026, 10, 9, 15, 0, 0)); // after the count, already on the hub
            Opname("P001", 4800, At(9), createdAt: At(16));         // applied at 16:00

            await _pull.TickAsync(CancellationToken.None);

            OnHand("P001").Should().Be(4600, "the 15:00 sale applies on top of the 09:00 count (D11)");
            Scalar("SELECT qty_out FROM stock_movements WHERE movement_type = 'OPNAME'").Should().Be(200,
                "on-hand at 09:00 was 50, counted 48");
        }

        [Test]
        public async Task opname_surplus_moves_stock_in_at_the_local_average()
        {
            SeedStock("P002", 1000);
            Opname("P002", 1250, At(9));

            await _pull.TickAsync(CancellationToken.None);

            OnHand("P002").Should().Be(1250);
            var m = new StockMovementRepository(_db).GetByJournal("OPN-DB-OKT26").Single();
            m.QtyIn.Should().Be(250);
            m.CostPrice.Should().Be(100000);
            Text("SELECT remark FROM stock_adjustment_items").Should().Be("SURPLUS");
        }

        [Test]
        public async Task opname_matching_the_system_writes_nothing_but_is_marked()
        {
            SeedStock("P001", 4800);
            var req = Opname("P001", 4800, At(9));

            (await _pull.TickAsync(CancellationToken.None)).Should().Be(1);

            Scalar("SELECT COUNT(*) FROM stock_movements WHERE movement_type = 'OPNAME'").Should().Be(0);
            Scalar("SELECT COUNT(*) FROM stock_adjustments").Should().Be(0);
            Scalar("SELECT COUNT(*) FROM applied_requests").Should().Be(1);
            _source.RowOf(req.Id).AppliedAt.Should().NotBeNull();
        }

        [Test]
        public async Task opname_zero_count_empties_the_shelf()
        {
            SeedStock("P001", 300);
            Opname("P001", 0, At(9));

            await _pull.TickAsync(CancellationToken.None);

            OnHand("P001").Should().Be(0, "a counted 0 is a count, not 'not counted'");
        }

        // ---------- PURCHASE / RETURN_OUT ----------

        [Test]
        public async Task pull_applies_purchase_once()
        {
            SeedStock("P001", 1000); // 10 @ 3,000
            var l1 = Purchase("line-1", "P001", 1000, 500000); // 10 @ 5,000
            var l2 = Purchase("line-2", "P002", 7200, 100000); // 3 dus x 24 = 72 pcs

            (await _pull.TickAsync(CancellationToken.None)).Should().Be(2);
            foreach (var row in _source.Rows) row.AppliedAt = null; // replay everything
            (await _pull.TickAsync(CancellationToken.None)).Should().Be(0);

            OnHand("P001").Should().Be(2000);
            OnHand("P002").Should().Be(7200);
            Scalar("SELECT COUNT(*) FROM stock_movements WHERE journal_no = 'RCV-0001'").Should().Be(2);
            Scalar("SELECT COUNT(*) FROM stock_movements WHERE journal_no = 'RCV-0001' AND id >= 5000000000").Should().Be(2);
            new ProductRepository(_db).GetByCode("P001").CostPrice.Should().Be(400000, "PR-K1 moving average");

            Text("SELECT doc_type || ' ' || sub_code || ' ' || legacy_source FROM purchases WHERE journal_no = 'RCV-0001'")
                .Should().Be("RECEIPT V001 DASHBOARD");
            Scalar("SELECT COUNT(*) FROM purchases").Should().Be(1, "both lines share one receipt document");
            Text("SELECT group_concat(product_code || ':' || quantity || ':' || unit_price || ':' || value || ':' || order_ref, ',') FROM (SELECT * FROM purchase_items ORDER BY product_code)")
                .Should().Be("P001:10:500000:5000000:PO-0007,P002:72:100000:7200000:PO-0007");
            Scalar("SELECT total_value FROM purchases WHERE journal_no = 'RCV-0001'").Should().Be(12200000);
            _source.RowOf(l1.Id).AppliedAt.Should().NotBeNull();
            _source.RowOf(l2.Id).AppliedAt.Should().NotBeNull();
        }

        [Test]
        public async Task purchase_half_unit_keeps_the_exact_ledger_qty()
        {
            Purchase("line-g", "P002", 50, 100000); // 0.5 galon

            await _pull.TickAsync(CancellationToken.None);

            OnHand("P002").Should().Be(50);
            Scalar("SELECT val_in FROM stock_movements WHERE journal_no = 'RCV-0001'").Should().Be(50000);
            Text("SELECT remark FROM purchase_items").Should().Be("qty 0,5");
        }

        [Test]
        public async Task return_out_moves_stock_out_at_the_local_average_when_cost_is_null()
        {
            SeedStock("P001", 1000);
            _source.Add(new PosStockRequest
            {
                RequestKind = "RETURN_OUT", IdempotencyKey = "RETURN_OUT:ret-line-1", ProductCode = "P001",
                Qty = 300, UnitCost = null, VendorCode = "V001", DocNo = "RTN-0001",
                PayloadJson = "{\"ref_no\":\"RCV-0001\"}", HappenedAt = At(11), CreatedAt = At(11)
            });

            (await _pull.TickAsync(CancellationToken.None)).Should().Be(1);

            OnHand("P001").Should().Be(700);
            var m = new StockMovementRepository(_db).GetByJournal("RTN-0001").Single();
            m.MovementType.Should().Be("RETURN_OUT");
            m.QtyOut.Should().Be(300);
            m.CostPrice.Should().Be(300000);
            Text("SELECT doc_type || ' ' || ref_no FROM purchases WHERE journal_no = 'RTN-0001'").Should().Be("PURCHASE_RETURN RCV-0001");
            new ProductRepository(_db).GetByCode("P001").CostPrice.Should().Be(300000, "a return never moves the average");
        }

        // ---------- VENDOR_BILL / PRODUCT_STATUS / NEW_PRODUCT / BARCODE_LINK ----------

        [Test]
        public async Task pull_applies_vendor_bill_to_payables_register()
        {
            var req = _source.Add(new PosStockRequest
            {
                RequestKind = "VENDOR_BILL", IdempotencyKey = "VENDOR_BILL:bill-1", VendorCode = "V001",
                DocNo = "BILL-0001",
                PayloadJson = "{\"amount\":12200000,\"gross_amount\":12500000,\"disc_amount\":300000," +
                              "\"due_date\":\"2026-11-08\",\"bill_date\":\"2026-10-09\",\"vendor_invoice_no\":\"INV/ABC/77\"}",
                HappenedAt = At(12), CreatedAt = At(12)
            });

            (await _pull.TickAsync(CancellationToken.None)).Should().Be(1);
            _source.RowOf(req.Id).AppliedAt = null;
            await _pull.TickAsync(CancellationToken.None);

            Scalar("SELECT COUNT(*) FROM payables_register").Should().Be(1);
            var ap = new PayablesRepository(_db).GetByJournalNo("BILL-0001");
            ap.SubCode.Should().Be("V001");
            ap.Amount.Should().Be(12200000);
            ap.GrossAmount.Should().Be(12500000);
            ap.DueDate.Should().Be("2026-11-08");
            ap.DocDate.Should().Be("2026-10-09");
            ap.Direction.Should().Be("D");
            ap.IsPaid.Should().Be("N");
            Text("SELECT ref || ' ' || disc_amount || ' ' || period_code FROM payables_register").Should().Be("INV/ABC/77 300000 202610");
            new PayablesRepository(_db).GetTotalUnpaidByVendor("V001").Should().Be(12200000);
        }

        [Test]
        public async Task vendor_bill_without_amount_stays_pending()
        {
            var req = _source.Add(new PosStockRequest
            {
                RequestKind = "VENDOR_BILL", IdempotencyKey = "VENDOR_BILL:bill-2", VendorCode = "V001",
                DocNo = "BILL-0002", PayloadJson = "{}", HappenedAt = At(12), CreatedAt = At(12)
            });

            (await _pull.TickAsync(CancellationToken.None)).Should().Be(0);

            _pull.LastResult.Failed.Should().Equal(req.Id);
            _source.RowOf(req.Id).AppliedAt.Should().BeNull();
            Scalar("SELECT COUNT(*) FROM payables_register").Should().Be(0);
            Scalar("SELECT COUNT(*) FROM applied_requests").Should().Be(0);
        }

        [Test]
        public async Task product_status_activates_an_inactive_product()
        {
            _source.Add(new PosStockRequest
            {
                RequestKind = "PRODUCT_STATUS", IdempotencyKey = "PRODUCT_STATUS:P003:1", ProductCode = "P003",
                PayloadJson = "{\"status\":\"A\"}", HappenedAt = At(9), CreatedAt = At(9)
            });

            (await _pull.TickAsync(CancellationToken.None)).Should().Be(1);

            new ProductRepository(_db).GetByCode("P003").Status.Should().Be("A");
        }

        [Test]
        public async Task new_product_then_purchase_in_one_rpc_applies_product_first()
        {
            var created = At(10);
            // The dashboard RPC wrote both in one transaction: same created_at. The
            // PURCHASE is listed first to prove the kind priority, not list order, decides.
            Purchase("line-np", "NP0001", 500, 250000, doc: "RCV-0002", createdAt: created);
            _source.Add(new PosStockRequest
            {
                RequestKind = "NEW_PRODUCT", IdempotencyKey = "NEW_PRODUCT:NP0001", ProductCode = "NP0001",
                PayloadJson = "{\"name\":\"Lampu Tidur Bulan\",\"dept_code\":\"42\",\"price\":3500000,\"cost_price\":250000}",
                HappenedAt = created, CreatedAt = created
            });

            (await _pull.TickAsync(CancellationToken.None)).Should().Be(2);

            var p = new ProductRepository(_db).GetByCode("NP0001");
            p.Name.Should().Be("LAMPU TIDUR BULAN");
            p.Status.Should().Be("A");
            p.Price.Should().Be(3500000);
            p.CostPrice.Should().Be(250000);
            OnHand("NP0001").Should().Be(500);
        }

        [Test]
        public async Task new_product_for_an_existing_code_changes_nothing()
        {
            _source.Add(new PosStockRequest
            {
                RequestKind = "NEW_PRODUCT", IdempotencyKey = "NEW_PRODUCT:P001", ProductCode = "P001",
                PayloadJson = "{\"name\":\"OTHER NAME\",\"price\":1}", HappenedAt = At(9), CreatedAt = At(9)
            });

            (await _pull.TickAsync(CancellationToken.None)).Should().Be(1);

            new ProductRepository(_db).GetByCode("P001").Name.Should().Be("P001");
        }

        [Test]
        public async Task barcode_link_is_a_marked_no_op()
        {
            var req = _source.Add(new PosStockRequest
            {
                RequestKind = "BARCODE_LINK", IdempotencyKey = "BARCODE_LINK:899000:P001", ProductCode = "P001",
                PayloadJson = "{\"barcode\":\"899000\"}", HappenedAt = At(9), CreatedAt = At(9)
            });

            (await _pull.TickAsync(CancellationToken.None)).Should().Be(1);

            _source.RowOf(req.Id).AppliedAt.Should().NotBeNull();
            Scalar("SELECT COUNT(*) FROM stock_movements").Should().Be(0);
        }

        // ---------- ids, ordering, failures ----------

        [Test]
        public async Task pull_movement_id_in_reserved_range()
        {
            Exec(@"INSERT INTO stock_movements (id, product_code, journal_no, movement_type, doc_date, period_code, qty_in)
                   VALUES (25146, 'P002', 'KLR-01-2610-0099', 'PURCHASE', '2026-10-01', '202610', 100)");
            Purchase("a", "P001", 100, 300000);
            Purchase("b", "P001", 100, 300000);
            Purchase("c", "P002", 100, 100000);

            await _pull.TickAsync(CancellationToken.None);

            using (var cmd = _db.CreateCommand())
            {
                cmd.CommandText = "SELECT id FROM stock_movements WHERE journal_no = 'RCV-0001' ORDER BY id";
                using var r = cmd.ExecuteReader();
                var got = new System.Collections.Generic.List<long>();
                while (r.Read()) got.Add(r.GetInt64(0));
                got.Should().Equal(Floor, Floor + 1, Floor + 2);
            }
            new ConfigRepository(_db).Get(PosRequestApplier.MovementIdSeqKey).Should().Be("5000000003");
        }

        [Test]
        public async Task movement_id_never_reuses_an_id_already_in_the_table()
        {
            Exec(@"INSERT INTO stock_movements (id, product_code, journal_no, movement_type, doc_date, period_code, qty_in)
                   VALUES (5000000010, 'P002', 'OPN-OLD', 'OPNAME', '2026-10-01', '202610', 100)");
            new ConfigRepository(_db).Set(PosRequestApplier.MovementIdSeqKey, "5000000000"); // e.g. a restored DB
            Purchase("a", "P001", 100, 300000);

            await _pull.TickAsync(CancellationToken.None);

            Scalar("SELECT id FROM stock_movements WHERE journal_no = 'RCV-0001'").Should().Be(5000000011);
        }

        [Test]
        public async Task pos_sale_after_a_pull_stays_below_the_range_and_is_cloud_pushed_but_the_pulled_row_is_not()
        {
            SeedStock("P001", 1000);
            Purchase("a", "P001", 100, 300000);
            await _pull.TickAsync(CancellationToken.None);

            SellAt("P001", 1, new DateTime(2026, 10, 9, 15, 0, 0));
            long saleId = Scalar("SELECT id FROM stock_movements WHERE movement_type = 'SALE'");
            saleId.Should().BeLessThan(Floor);

            var sink = new InMemoryMirrorSink();
            var pusher = new WatermarkPusher(_db, sink, NullLogger<WatermarkPusher>.Instance);
            var r = await pusher.PushTableAsync("stock_movements", WatermarkPusher.StockMovementsWatermarkKey, 100, CancellationToken.None);

            r.Failed.Should().BeFalse();
            var pushed = sink.Table("stock_movements").Values.Select(v => Convert.ToInt64(v["id"])).ToList();
            pushed.Should().Contain(saleId);
            pushed.Should().OnlyContain(id => id < Floor, "dashboard-originated rows are never pushed back (PV-3)");
        }

        [Test]
        public async Task out_of_order_purchase_before_its_new_product_waits_one_tick()
        {
            var purchase = Purchase("np-line", "NP0002", 200, 150000, doc: "RCV-0003", createdAt: At(10));
            _source.Add(new PosStockRequest
            {
                RequestKind = "NEW_PRODUCT", IdempotencyKey = "NEW_PRODUCT:NP0002", ProductCode = "NP0002",
                PayloadJson = "{\"name\":\"KABEL ROL\"}", HappenedAt = At(10, 5), CreatedAt = At(10, 5)
            });

            (await _pull.TickAsync(CancellationToken.None)).Should().Be(1, "only the product; the purchase waits");
            _pull.LastResult.Failed.Should().Equal(purchase.Id);
            _source.RowOf(purchase.Id).AppliedAt.Should().BeNull();
            OnHand("NP0002").Should().Be(0);

            (await _pull.TickAsync(CancellationToken.None)).Should().Be(1);
            OnHand("NP0002").Should().Be(200);
            _source.RowOf(purchase.Id).AppliedAt.Should().NotBeNull();
        }

        [Test]
        public async Task failed_request_holds_back_later_requests_for_the_same_product_only()
        {
            SeedStock("P001", 1000);
            SeedStock("P002", 1000);
            // Collides with a local non-receipt document of the same number -> rejected.
            Exec(@"INSERT INTO purchases (doc_type, journal_no, doc_date, sub_code, period_code)
                   VALUES ('PURCHASE', 'MSK-CLASH', '2026-10-01', 'V009', '202610')");
            var bad = Purchase("bad", "P001", 500, 300000, doc: "MSK-CLASH", createdAt: At(10));
            var opnameP1 = Opname("P001", 1500, At(11), createdAt: At(12));
            var okP2 = Purchase("ok", "P002", 100, 100000, doc: "RCV-0004", createdAt: At(10, 30));

            (await _pull.TickAsync(CancellationToken.None)).Should().Be(1);

            _pull.LastResult.Failed.Should().Equal(bad.Id);
            _pull.LastResult.HeldBack.Should().Equal(opnameP1.Id);
            _pull.LastResult.Applied.Should().Equal(okP2.Id);
            OnHand("P001").Should().Be(1000, "the opname did not run against on-hand missing the failed receipt");
            _source.RowOf(opnameP1.Id).AppliedAt.Should().BeNull();
            Scalar("SELECT COUNT(*) FROM purchase_items WHERE journal_no = 'MSK-CLASH'").Should().Be(0, "rolled back");
        }

        [Test]
        public async Task unknown_kind_or_bad_payload_is_rejected_and_the_rest_continue()
        {
            var junk = _source.Add(new PosStockRequest
            {
                RequestKind = "PRODUCT_STATUS", IdempotencyKey = "PRODUCT_STATUS:P001:x", ProductCode = "P001",
                PayloadJson = "{not json", HappenedAt = At(9), CreatedAt = At(9)
            });
            var ok = Purchase("ok", "P002", 100, 100000);

            (await _pull.TickAsync(CancellationToken.None)).Should().Be(1);

            _pull.LastResult.Failed.Should().Equal(junk.Id);
            _source.RowOf(ok.Id).AppliedAt.Should().NotBeNull();
            new ProductRepository(_db).GetByCode("P001").Status.Should().Be("A");
        }

        [Test]
        public async Task network_failure_after_local_apply_is_remarked_not_reapplied()
        {
            SeedStock("P001", 5000);
            var req = Opname("P001", 4800, At(9));
            _source.FailMarks = 1;

            Func<Task> tick = () => _pull.TickAsync(CancellationToken.None);
            await tick.Should().ThrowAsync<PullMarkException>("the worker must count the tick as failed and back off");
            OnHand("P001").Should().Be(4800, "the local apply committed");
            _source.RowOf(req.Id).AppliedAt.Should().BeNull();

            (await _pull.TickAsync(CancellationToken.None)).Should().Be(0, "nothing newly applied");
            _pull.LastResult.Remarked.Should().Equal(req.Id);
            _source.RowOf(req.Id).AppliedAt.Should().NotBeNull();
            OnHand("P001").Should().Be(4800);
            Scalar("SELECT COUNT(*) FROM stock_movements WHERE movement_type = 'OPNAME'").Should().Be(1);
        }

        [Test]
        public async Task fetch_failure_throws_and_changes_nothing()
        {
            Purchase("a", "P001", 100, 300000);
            _source.FailFetch = true;

            Func<Task> tick = () => _pull.TickAsync(CancellationToken.None);
            await tick.Should().ThrowAsync<TimeoutException>();

            Scalar("SELECT COUNT(*) FROM stock_movements").Should().Be(0);
            Scalar("SELECT COUNT(*) FROM applied_requests").Should().Be(0);
        }

        [Test]
        public async Task requests_for_another_register_are_not_fetched()
        {
            var other = _source.Add(new PosStockRequest
            {
                RequestKind = "PURCHASE", IdempotencyKey = "PURCHASE:reg02", ProductCode = "P001", Qty = 100,
                UnitCost = 1, VendorCode = "V001", DocNo = "RCV-9", TargetRegister = "KLR-02",
                HappenedAt = At(9), CreatedAt = At(9)
            });
            var hub = Purchase("hub", "P002", 100, 100000);
            hub.TargetRegister = "hub";

            (await _pull.TickAsync(CancellationToken.None)).Should().Be(1);

            _source.RowOf(other.Id).AppliedAt.Should().BeNull();
            _source.RowOf(hub.Id).AppliedAt.Should().NotBeNull();
        }

        [Test]
        public async Task worker_tick_runs_the_real_pull_after_the_push()
        {
            SeedStock("P001", 5000);
            Opname("P001", 4800, At(9));
            var cfg = new CloudSyncConfig();
            var sink = new InMemoryMirrorSink();
            var worker = new CloudSyncWorker(NullLogger<CloudSyncWorker>.Instance, Options.Create(cfg),
                new OutboxRouter(_db, new SyncQueueRepository(_db), sink, NullLogger<OutboxRouter>.Instance, cfg.OutboxTableList()),
                new WatermarkPusher(_db, sink, NullLogger<WatermarkPusher>.Instance),
                _pull);

            (await worker.TickAsync(CancellationToken.None)).Should().BeTrue();

            OnHand("P001").Should().Be(4800);
            sink.Table("stock_movements").Keys.Should().NotContain(k => long.Parse(k) >= Floor);
        }
    }
}
