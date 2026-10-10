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
        private static readonly TimeSpan Wita = TimeSpan.FromHours(8);
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
            new DateTimeOffset(2026, 10, 9, hour, minute, 0, Wita);

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

        // pos_stock_requests.qty is BIGINT: a qty the int ledger cannot hold is rejected
        // (failed_at set) once, not retried every tick.
        [Test]
        public async Task opname_qty_above_int_range_is_rejected_not_retried()
        {
            SeedStock("P001", 5000);
            var req = _source.Add(new PosStockRequest
            {
                RequestKind = "OPNAME", IdempotencyKey = "OPNAME:sess-big:P001", ProductCode = "P001",
                Qty = 2_400_000_000L, DocNo = "OPN-DB-OKT26", HappenedAt = At(9), CreatedAt = At(16)
            });

            (await _pull.TickAsync(CancellationToken.None)).Should().Be(0);
            _pull.LastResult.Rejected.Should().Equal(req.Id);
            _pull.LastResult.Failed.Should().BeEmpty();
            _source.RowOf(req.Id).FailedReason.Should()
                .Be("OPNAME OPNAME:sess-big:P001: qty 2400000000 is out of range for the register ledger (max 2147483647)");
            OnHand("P001").Should().Be(5000);
            Scalar("SELECT COUNT(*) FROM stock_movements WHERE movement_type = 'OPNAME'").Should().Be(0);

            (await _pull.TickAsync(CancellationToken.None)).Should().Be(0);
            _pull.LastResult.Fetched.Should().Be(0, "a rejected request leaves the pending fetch");
        }

        [Test]
        public async Task purchase_qty_above_int_range_is_rejected()
        {
            var req = Purchase("big-1", "P001", 100, 100000);
            req.Qty = (long)int.MaxValue + 1;

            await _pull.TickAsync(CancellationToken.None);

            _pull.LastResult.Rejected.Should().Equal(req.Id);
            Scalar("SELECT COUNT(*) FROM stock_movements").Should().Be(0);
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

        // ---------- payloads exactly as dashboard 0059 writes them (WP-11b) ----------

        private PosStockRequest DashboardBill(string id, string docNo, long total, string billType = "bill",
            string reverses = null, int hour = 12) =>
            _source.Add(new PosStockRequest
            {
                RequestKind = "VENDOR_BILL", IdempotencyKey = "VENDOR_BILL:" + id, VendorCode = "V001", DocNo = docNo,
                // post_vendor_bill: bill_id, bill_type, vendor_invoice_no, faktur_pajak_no, bill_date,
                // due_date, subtotal, tax_amount, total, reverses_doc_no (no "amount").
                PayloadJson = "{\"bill_id\":\"" + id + "\",\"bill_type\":\"" + billType + "\",\"vendor_invoice_no\":\"INV-" + id + "\"," +
                              "\"faktur_pajak_no\":null,\"bill_date\":\"2026-10-09\",\"due_date\":\"2026-11-08\"," +
                              "\"subtotal\":" + total + ",\"tax_amount\":0,\"total\":" + total + "," +
                              "\"reverses_doc_no\":" + (reverses == null ? "null" : "\"" + reverses + "\"") + "}",
                HappenedAt = At(hour), CreatedAt = At(hour)
            });

        [Test]
        public async Task dashboard_vendor_bill_payload_with_total_is_applied()
        {
            DashboardBill("b1", "DVB-2610-0001", 3005000);

            (await _pull.TickAsync(CancellationToken.None)).Should().Be(1);

            var ap = new PayablesRepository(_db).GetByJournalNo("DVB-2610-0001");
            ap.Amount.Should().Be(3005000);
            ap.GrossAmount.Should().Be(3005000);
            ap.DueDate.Should().Be("2026-11-08");
            Text("SELECT ref FROM payables_register").Should().Be("INV-b1");
        }

        [Test]
        public async Task dashboard_credit_note_lowers_the_bill_it_reverses_and_adds_no_payable()
        {
            DashboardBill("b1", "DVB-2610-0001", 3005000);
            var cn = DashboardBill("c1", "DCN-2610-0001", 560000, "credit_note", "DVB-2610-0001", hour: 13);

            (await _pull.TickAsync(CancellationToken.None)).Should().Be(2);
            _source.RowOf(cn.Id).AppliedAt = null; // replay
            await _pull.TickAsync(CancellationToken.None);

            Scalar("SELECT COUNT(*) FROM payables_register").Should().Be(1, "a credit note is not a payable of its own");
            new PayablesRepository(_db).GetByJournalNo("DVB-2610-0001").Amount.Should().Be(2445000);
            new PayablesRepository(_db).GetTotalUnpaidByVendor("V001").Should().Be(2445000);
            Text("SELECT remark FROM payables_register").Should().Be("Tagihan dashboard; NK DCN-2610-0001");
        }

        // D27 partial return math: dashboard 0076 credits 10 pcs @ Rp 2.800 as
        // 2 + 3 + 5 pcs with cumulative rounding (5.600 + 8.400,01 + 13.999,99).
        [Test]
        public async Task dashboard_partial_credit_notes_add_up_and_settle_the_bill()
        {
            DashboardBill("b1", "DVB-2610-0001", 2800000);
            DashboardBill("c1", "DCN-2610-0001", 560000, "credit_note", "DVB-2610-0001", hour: 13);
            (await _pull.TickAsync(CancellationToken.None)).Should().Be(2);
            var ap = new PayablesRepository(_db).GetByJournalNo("DVB-2610-0001");
            ap.Amount.Should().Be(2240000, "28.000 - 5.600: the bill it reverses is lowered");
            ap.IsPaid.Should().Be("N");

            DashboardBill("c2", "DCN-2610-0002", 840001, "credit_note", "DVB-2610-0001", hour: 14);
            DashboardBill("c3", "DCN-2610-0003", 1399999, "credit_note", "DVB-2610-0001", hour: 15);
            (await _pull.TickAsync(CancellationToken.None)).Should().Be(2);

            ap = new PayablesRepository(_db).GetByJournalNo("DVB-2610-0001");
            ap.Amount.Should().Be(0, "2 + 3 + 5 pcs credited = the whole bill, to the cent");
            ap.GrossAmount.Should().Be(0);
            ap.IsPaid.Should().Be("Y", "nothing left to pay");
            Scalar("SELECT COUNT(*) FROM payables_register").Should().Be(1);
            new PayablesRepository(_db).GetTotalUnpaidByVendor("V001").Should().Be(0);
            Text("SELECT remark FROM payables_register").Should()
                .Be("Tagihan dashboard; NK DCN-2610-0001; NK DCN-2610-0002; NK DCN-2610-0003");
        }

        [Test]
        public async Task dashboard_credit_note_replay_on_a_restored_db_is_not_applied_twice()
        {
            DashboardBill("b1", "DVB-2610-0001", 2800000);
            var c1 = DashboardBill("c1", "DCN-2610-0001", 560000, "credit_note", "DVB-2610-0001", hour: 13);
            var c2 = DashboardBill("c2", "DCN-2610-0002", 840001, "credit_note", "DVB-2610-0001", hour: 14);
            var c3 = DashboardBill("c3", "DCN-2610-0003", 700000, "credit_note", "DVB-2610-0001", hour: 15);
            (await _pull.TickAsync(CancellationToken.None)).Should().Be(4);

            // Restored DB: applied_requests lost, the Supabase marks cleared.
            Exec("DELETE FROM applied_requests");
            foreach (var r in new[] { c1, c2, c3 }) _source.RowOf(r.Id).AppliedAt = null;
            await _pull.TickAsync(CancellationToken.None);

            new PayablesRepository(_db).GetByJournalNo("DVB-2610-0001").Amount.Should().Be(2800000 - 560000 - 840001 - 700000,
                "every credit note, including the third (past the legacy 60-char remark), is booked once");
        }

        [Test]
        public async Task dashboard_credit_note_never_takes_the_bill_below_zero()
        {
            DashboardBill("b1", "DVB-2610-0001", 2800000);
            (await _pull.TickAsync(CancellationToken.None)).Should().Be(1);
            // the hub row is already lower than the dashboard bill (edited on the hub)
            Exec("UPDATE payables_register SET value = 300000, gross_amount = 300000 WHERE journal_no = 'DVB-2610-0001'");

            DashboardBill("c1", "DCN-2610-0001", 560000, "credit_note", "DVB-2610-0001", hour: 13);
            (await _pull.TickAsync(CancellationToken.None)).Should().Be(1);

            var ap = new PayablesRepository(_db).GetByJournalNo("DVB-2610-0001");
            ap.Amount.Should().Be(0, "never below zero");
            ap.GrossAmount.Should().Be(0);
            ap.IsPaid.Should().Be("Y");
            new PayablesRepository(_db).GetTotalUnpaidByVendor("V001").Should().Be(0);
        }

        [Test]
        public async Task dashboard_credit_note_on_a_partly_paid_bill_marks_it_paid_when_covered()
        {
            DashboardBill("b1", "DVB-2610-0001", 2800000);
            (await _pull.TickAsync(CancellationToken.None)).Should().Be(1);
            Exec("UPDATE payables_register SET payment_amount = 2240000 WHERE journal_no = 'DVB-2610-0001'");
            new PayablesRepository(_db).GetTotalUnpaidByVendor("V001").Should().Be(560000);

            DashboardBill("c1", "DCN-2610-0001", 560000, "credit_note", "DVB-2610-0001", hour: 13);
            (await _pull.TickAsync(CancellationToken.None)).Should().Be(1);

            var ap = new PayablesRepository(_db).GetByJournalNo("DVB-2610-0001");
            ap.Amount.Should().Be(2240000);
            ap.IsPaid.Should().Be("Y", "paid 22.400 + credited 5.600 covers the 28.000 bill");
            new PayablesRepository(_db).GetTotalUnpaidByVendor("V001").Should().Be(0);
        }

        [Test]
        public async Task dashboard_credit_note_before_its_bill_waits()
        {
            var cn = DashboardBill("c1", "DCN-2610-0001", 560000, "credit_note", "DVB-2610-0009");

            (await _pull.TickAsync(CancellationToken.None)).Should().Be(0);

            _pull.LastResult.Failed.Should().Equal(cn.Id);
            _source.RowOf(cn.Id).AppliedAt.Should().BeNull();
            Scalar("SELECT COUNT(*) FROM applied_requests").Should().Be(0);
        }

        [Test]
        public async Task dashboard_receipt_and_return_payload_keys_fill_order_ref_and_ref_no()
        {
            SeedStock("P001", 1000);
            _source.Add(new PosStockRequest
            {
                RequestKind = "PURCHASE", IdempotencyKey = "PURCHASE:rl-1", ProductCode = "P001", Qty = 600, UnitCost = 280000,
                VendorCode = "V001", DocNo = "DRC-2610-0001",
                PayloadJson = "{\"receipt_id\":\"r1\",\"receipt_line_id\":\"rl-1\",\"po_line_id\":\"pl-1\"," +
                              "\"po_doc_no\":\"DPO-2610-0001\",\"delivery_note\":null}",
                HappenedAt = At(10), CreatedAt = At(10)
            });
            _source.Add(new PosStockRequest
            {
                RequestKind = "RETURN_OUT", IdempotencyKey = "RETURN_OUT:rt-1", ProductCode = "P001", Qty = 200, UnitCost = 280000,
                VendorCode = "V001", DocNo = "DRT-2610-0001",
                PayloadJson = "{\"return_id\":\"t1\",\"return_line_id\":\"rt-1\",\"original_line\":\"rl-1\"," +
                              "\"original_doc_no\":\"DRC-2610-0001\",\"to_refund\":true}",
                HappenedAt = At(11), CreatedAt = At(11)
            });

            (await _pull.TickAsync(CancellationToken.None)).Should().Be(2);

            Text("SELECT order_ref FROM purchase_items WHERE journal_no = 'DRC-2610-0001'").Should().Be("DPO-2610-0001");
            Text("SELECT ref_no FROM purchases WHERE journal_no = 'DRT-2610-0001'").Should().Be("DRC-2610-0001");
            Text("SELECT order_ref FROM purchase_items WHERE journal_no = 'DRT-2610-0001'").Should().Be("DRC-2610-0001");
            OnHand("P001").Should().Be(1400);
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

        // D21: a NEW_PRODUCT queued AFTER its purchase (two dashboard transactions whose
        // now() crossed) is still applied first in the same tick: NEW_PRODUCT leads the batch.
        [Test]
        public async Task out_of_order_purchase_before_its_new_product_is_applied_after_it_in_the_same_tick()
        {
            var purchase = Purchase("np-line", "NP0002", 200, 150000, doc: "RCV-0003", createdAt: At(10));
            var product = NewProductRequest("NP0002", "{\"name\":\"KABEL ROL\"}", At(10, 5));

            (await _pull.TickAsync(CancellationToken.None)).Should().Be(2);
            _pull.LastResult.Applied.Should().Equal(product.Id, purchase.Id);
            OnHand("NP0002").Should().Be(200);
            _source.RowOf(purchase.Id).AppliedAt.Should().NotBeNull();
        }

        // The NEW_PRODUCT has not reached the hub yet (not fetched): the purchase waits,
        // writes nothing, and goes through on the tick after the product arrives.
        [Test]
        public async Task purchase_on_an_np_code_waits_until_its_new_product_arrives()
        {
            var purchase = Purchase("np-line", "NP0002", 200, 150000, doc: "RCV-0003", createdAt: At(10));

            (await _pull.TickAsync(CancellationToken.None)).Should().Be(0, "the product is not here yet");
            _pull.LastResult.Failed.Should().Equal(purchase.Id);
            _source.RowOf(purchase.Id).AppliedAt.Should().BeNull();
            _source.RowOf(purchase.Id).FailedAt.Should().BeNull("waiting is not a rejection");
            Scalar("SELECT COUNT(*) FROM purchases").Should().Be(0);

            NewProductRequest("NP0002", "{\"name\":\"KABEL ROL\"}", At(10, 5));
            (await _pull.TickAsync(CancellationToken.None)).Should().Be(2);
            OnHand("NP0002").Should().Be(200);
        }

        private PosStockRequest NewProductRequest(string code, string payload, DateTimeOffset createdAt) =>
            _source.Add(new PosStockRequest
            {
                RequestKind = "NEW_PRODUCT", IdempotencyKey = "NEW_PRODUCT:" + code, ProductCode = code,
                PayloadJson = payload, HappenedAt = createdAt, CreatedAt = createdAt
            });

        // An NP product that exists here without its NEW_PRODUCT having been applied (made
        // by hand before the POS refused NP codes) is not the dashboard's: stock waits.
        [Test]
        public async Task stock_on_an_np_code_waits_for_its_new_product_even_when_a_local_product_has_the_code()
        {
            SeedProduct("NP0009", "A", 100000);
            var purchase = Purchase("np9", "NP0009", 100, 100000, createdAt: At(10));
            var count = Opname("NP0009", 300, At(11), createdAt: At(12));

            (await _pull.TickAsync(CancellationToken.None)).Should().Be(0);
            _pull.LastResult.Failed.Should().Equal(purchase.Id);
            _pull.LastResult.HeldBack.Should().Equal(count.Id);
            OnHand("NP0009").Should().Be(0);
            Scalar("SELECT COUNT(*) FROM stock_movements").Should().Be(0);
        }

        // The hub's NP0009 is a different product: the NEW_PRODUCT is rejected (failed_at,
        // reason) and the dashboard's receipt / count on that code never lands on it.
        [Test]
        public async Task new_product_on_an_np_code_taken_by_another_product_is_rejected_and_its_stock_waits()
        {
            SeedProduct("NP0009", "A", 100000); // name "NP0009"
            var product = NewProductRequest("NP0009", "{\"name\":\"Sabun Cair Baru\"}", At(10));
            var purchase = Purchase("np9", "NP0009", 100, 100000, createdAt: At(10));

            (await _pull.TickAsync(CancellationToken.None)).Should().Be(0);
            _pull.LastResult.Rejected.Should().Equal(product.Id);
            _pull.LastResult.HeldBack.Should().Equal(purchase.Id);
            _source.RowOf(product.Id).FailedReason.Should()
                .Be("NEW_PRODUCT NEW_PRODUCT:NP0009: code NP0009 is already used on this register by \"NP0009\" (dashboard product \"SABUN CAIR BARU\")");
            new ProductRepository(_db).GetByCode("NP0009").Name.Should().Be("NP0009");

            (await _pull.TickAsync(CancellationToken.None)).Should().Be(0, "next tick: the product is still not the dashboard's");
            _pull.LastResult.Failed.Should().Equal(purchase.Id);
            OnHand("NP0009").Should().Be(0);
        }

        // Replay after a restore: the product is already here with the same name. The
        // NEW_PRODUCT is applied as a no-op and unblocks its stock.
        [Test]
        public async Task new_product_already_here_with_the_same_name_is_a_no_op_that_unblocks_its_stock()
        {
            new ProductRepository(_db).Insert(new Product
            {
                ProductCode = "NP0010", Name = "KABEL ROL", Price = 900000, CostPrice = 700000, Status = "A",
                OpenPrice = "N", VatFlag = "N", LuxuryTaxFlag = "N", IsConsignment = "N"
            });
            NewProductRequest("NP0010", "{\"name\":\"kabel rol\",\"price\":1}", At(10));
            Purchase("np10", "NP0010", 300, 700000, createdAt: At(10));

            (await _pull.TickAsync(CancellationToken.None)).Should().Be(2);
            new ProductRepository(_db).GetByCode("NP0010").Price.Should().Be(900000, "left as it is");
            OnHand("NP0010").Should().Be(300);
        }

        [Test]
        public async Task new_product_with_a_pack_size_sets_unit2_and_conversion1()
        {
            NewProductRequest("NP0011",
                "{\"name\":\"TEH KOTAK BARU\",\"dept_code\":\"11\",\"unit\":\"PCS\",\"unit2\":\"DUS\",\"conversion1\":2400,"
                + "\"price\":400000,\"buying_price\":300000,\"cost_price\":300000,\"vendor_code\":\"V001\",\"status\":\"A\",\"source\":\"direct_receipt\"}",
                At(10));

            (await _pull.TickAsync(CancellationToken.None)).Should().Be(1);

            Text("SELECT unit || '/' || unit2 || '/' || conversion1 || '/' || dept_code || '/' || vendor_code FROM products WHERE product_code = 'NP0011'")
                .Should().Be("PCS/DUS/2400/11/V001");
            var p = new ProductRepository(_db).GetByCode("NP0011");
            (p.Price, p.BuyingPrice, p.CostPrice, p.Status).Should().Be((400000L, 300000L, 300000L, "A"));
        }

        [Test]
        public async Task new_product_with_a_bad_pack_size_is_invalid_and_holds_its_stock_back()
        {
            var product = NewProductRequest("NP0012", "{\"name\":\"X\",\"unit2\":\"DUS\",\"conversion1\":50}", At(10));
            var purchase = Purchase("np12", "NP0012", 100, 100000, createdAt: At(10));

            (await _pull.TickAsync(CancellationToken.None)).Should().Be(0);
            _pull.LastResult.Failed.Should().Equal(product.Id);
            _pull.LastResult.HeldBack.Should().Equal(purchase.Id);
            new ProductRepository(_db).GetByCode("NP0012").Should().BeNull();
        }

        // create -> receive -> count, the D21 flow as the hub sees it.
        [Test]
        public async Task new_product_received_then_counted_applies_in_order()
        {
            var created = At(9);
            NewProductRequest("NP0013", "{\"name\":\"KERUPUK BARU\",\"cost_price\":150000,\"price\":200000}", created);
            Purchase("np13", "NP0013", 2400, 150000, doc: "RCV-0013", createdAt: At(10));
            Opname("NP0013", 2000, At(11), createdAt: At(12));

            (await _pull.TickAsync(CancellationToken.None)).Should().Be(3);

            OnHand("NP0013").Should().Be(2000, "24 received, 20 counted: a shortage of 4");
            Scalar("SELECT COUNT(*) FROM stock_movements WHERE product_code = 'NP0013' AND movement_type = 'OPNAME'").Should().Be(1);
            Scalar("SELECT COUNT(*) FROM applied_requests WHERE request_kind = 'NEW_PRODUCT' AND idempotency_key = 'NEW_PRODUCT:NP0013'").Should().Be(1);
        }

        [Test]
        public void apply_order_puts_every_new_product_first()
        {
            var late = new PosStockRequest { Id = Guid.NewGuid(), RequestKind = "NEW_PRODUCT", CreatedAt = At(12) };
            var early = new PosStockRequest { Id = Guid.NewGuid(), RequestKind = "PURCHASE", CreatedAt = At(9) };
            var status = new PosStockRequest { Id = Guid.NewGuid(), RequestKind = "PRODUCT_STATUS", CreatedAt = At(9) };
            new[] { early, status, late }.OrderBy(x => x, PosRequestKinds.ApplyOrder).Should().Equal(late, status, early);
            PosRequestKinds.IsDashboardProductCode(" np0001").Should().BeTrue();
            PosRequestKinds.IsDashboardProductCode("9001").Should().BeFalse();
            PosRequestKinds.NewProductKey(" NP0001 ").Should().Be("NEW_PRODUCT:NP0001");
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
        public async Task failure_after_partial_writes_rolls_the_whole_request_back()
        {
            // The receipt header and line are written before the movement; make the
            // movement insert fail and nothing of the request may remain.
            Exec(@"CREATE TEMP TRIGGER fail_movement BEFORE INSERT ON stock_movements
                   BEGIN SELECT RAISE(ABORT, 'disk full'); END;");
            var req = Purchase("a", "P001", 100, 300000);

            (await _pull.TickAsync(CancellationToken.None)).Should().Be(0);

            _pull.LastResult.Failed.Should().Equal(req.Id);
            Scalar("SELECT COUNT(*) FROM purchases").Should().Be(0);
            Scalar("SELECT COUNT(*) FROM purchase_items").Should().Be(0);
            Scalar("SELECT COUNT(*) FROM applied_requests").Should().Be(0);
            new ConfigRepository(_db).Get(PosRequestApplier.MovementIdSeqKey).Should().Be("5000000000", "the id is not burnt");
            _source.RowOf(req.Id).AppliedAt.Should().BeNull();

            Exec("DROP TRIGGER fail_movement;");
            (await _pull.TickAsync(CancellationToken.None)).Should().Be(1, "retried next tick");
            Scalar("SELECT id FROM stock_movements WHERE journal_no = 'RCV-0001'").Should().Be(Floor);
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
