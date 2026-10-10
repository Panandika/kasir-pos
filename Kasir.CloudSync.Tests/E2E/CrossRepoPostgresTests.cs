using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Kasir.CloudSync.Outbox;
using Kasir.CloudSync.Pull;
using Kasir.CloudSync.Push;
using Kasir.CloudSync.Sinks;
using Kasir.CloudSync.Tests.TestHelpers;
using Kasir.Data;
using Kasir.Data.Repositories;
using Kasir.Models;
using Kasir.Services;
using Kasir.Utils;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Newtonsoft.Json.Linq;
using Npgsql;
using NUnit.Framework;

namespace Kasir.CloudSync.Tests.E2E
{
    // WP-11b cross-repo end to end: the REAL dashboard (sinar-makmur-dashboard
    // migrations 0058-0062 on a LOCAL Supabase stack, RPCs called over PostgREST with
    // user JWTs exactly like the browser) and the REAL hub worker (CloudSyncWorker:
    // OutboxRouter + WatermarkPusher push, PullService pull, GenericSink and
    // PostgresPosRequestSource against public.*) meet on one kasir.db.
    //
    //   count in dashboard -> apply -> pos_stock_requests -> hub applies OPNAME once
    //   POS sale after the count is taken off the counted qty    (PLAN edge 9, #12/#28)
    //   receipt between count and "Terapkan" is counted once     (PLAN edge 10, #29)
    //   dashboard receipt / bill / return / credit note -> POS once (#47 #50 #51 #53)
    //   POS purchasing stays locked while the dashboard purchases (#46)
    //   dashboard-made product (NP, D21) -> NEW_PRODUCT before its receipt and count
    //
    // [Explicit]: needs the dashboard's LOCAL stack (`pnpm db:reset:local` in
    // sinar-makmur-dashboard, which applies every migration + seed):
    //   export KASIR_CLOUDSYNC_TEST_PG="Host=127.0.0.1;Port=54322;Database=postgres;Username=postgres;Password=postgres"
    //   export KASIR_SUPABASE_URL=http://127.0.0.1:54321          # optional, this is the default
    //   export KASIR_SUPABASE_JWT_SECRET=<supabase status -o env JWT_SECRET>  # optional, CLI default
    //   dotnet test Kasir.CloudSync.Tests --filter "FullyQualifiedName~CrossRepoPostgresTests"
    //
    // Unlike the other *PostgresTests this suite writes to public (that is the point:
    // the dashboard RPCs only know public). It stays out of the way of other data:
    // every run uses its own product codes (X11B<run><n>), vendor (XV<run>) and users,
    // POS movement ids in a per-run window of 4.3e9..4.9e9 (above every legacy 32-bit
    // hash id, below the 5e9 dashboard range), and the pull only reads and marks the
    // requests of this run (ScopedPosRequestSource). Refuses non-local hosts.
    [TestFixture]
    [Explicit("Needs the dashboard's local Supabase stack via KASIR_CLOUDSYNC_TEST_PG")]
    [NonParallelizable]
    public class CrossRepoPostgresTests
    {
        private const long Floor = 5_000_000_000L;
        private static readonly TimeSpan StoreOffset = TimeSpan.FromHours(8); // WITA (D26)

        private string _pg;
        private DashboardApi _api;
        private string _run;
        private long _idWindow;
        private int _testNo;
        private string _vendor;
        private DashboardApi.User _owner;
        private DashboardApi.User _mgr1;
        private DashboardApi.User _mgr2;
        private Guid _session;
        private bool _createdSession;
        private readonly List<string> _products = new List<string>();

        private SqliteConnection _db;
        private ScopedPosRequestSource _source;
        private PullService _pull;
        private CloudSyncWorker _worker;
        private long _openingId;

        // ------------------------------------------------------------------ setup

        [OneTimeSetUp]
        public async Task OneTimeSetUp()
        {
            _pg = Environment.GetEnvironmentVariable("KASIR_CLOUDSYNC_TEST_PG");
            if (string.IsNullOrWhiteSpace(_pg)) Assert.Ignore("KASIR_CLOUDSYNC_TEST_PG not set");
            var csb = new NpgsqlConnectionStringBuilder(_pg);
            if (csb.Host != "localhost" && csb.Host != "127.0.0.1") Assert.Ignore("refusing non-local host " + csb.Host);
            if (!Convert.ToBoolean(await Pg("SELECT to_regclass('public.pos_stock_requests') IS NOT NULL AND to_regprocedure('public.validate_receipt(uuid,boolean)') IS NOT NULL")))
                Assert.Ignore("dashboard migrations 0058/0059 are not applied on this stack (run pnpm db:reset:local)");

            string url = Environment.GetEnvironmentVariable("KASIR_SUPABASE_URL");
            _api = new DashboardApi(string.IsNullOrWhiteSpace(url) ? "http://127.0.0.1:54321" : url,
                Environment.GetEnvironmentVariable("KASIR_SUPABASE_JWT_SECRET"));

            var rnd = new Random();
            _run = Guid.NewGuid().ToString("N").Substring(0, 6).ToUpperInvariant();
            _idWindow = 4_300_000_000L + rnd.Next(0, 500_000) * 1_000L;
            _vendor = "XV" + _run;
            _owner = DashboardApi.Owner(Guid.NewGuid(), "x11b-owner-" + _run.ToLowerInvariant() + "@test.local");
            _mgr1 = DashboardApi.Manager(Guid.NewGuid(), "x11b-mgr1-" + _run.ToLowerInvariant() + "@test.local");
            _mgr2 = DashboardApi.Manager(Guid.NewGuid(), "x11b-mgr2-" + _run.ToLowerInvariant() + "@test.local");
            TestContext.Progress.WriteLine($"cross-repo run {_run}: vendor {_vendor}, POS id window {_idWindow}");

            foreach (var (u, role) in new[] { (_owner, "owner"), (_mgr1, "manager"), (_mgr2, "manager") })
            {
                await PgExec("INSERT INTO auth.users (id, email) VALUES (@id, @email) ON CONFLICT (id) DO NOTHING",
                    ("@id", u.Id), ("@email", u.Email));
                await PgExec(@"INSERT INTO public.dashboard_users (email, user_id, role) VALUES (@email, @id, @role)
                               ON CONFLICT (email) DO UPDATE SET user_id = excluded.user_id, role = excluded.role",
                    ("@id", u.Id), ("@email", u.Email), ("@role", role));
            }
            await PgExec("INSERT INTO public.subsidiaries (sub_code, name) VALUES (@v, @n) ON CONFLICT (sub_code) DO NOTHING",
                ("@v", _vendor), ("@n", "VENDOR UJI " + _run));

            // One open session at a time (D-rule); reuse one a Playwright run left open.
            var open = await Pg("SELECT id FROM public.stock_count_sessions WHERE status = 'open' ORDER BY created_at LIMIT 1");
            if (open is Guid g)
            {
                _session = g;
            }
            else
            {
                _session = Guid.Parse((string)await _api.Rpc(_owner, "create_count_session",
                    new { p_name = "Opname uji lintas repo " + _run, p_timing_mode = "exact" }));
                _createdSession = true;
            }
        }

        [OneTimeTearDown]
        public async Task OneTimeTearDown()
        {
            if (_api == null) return;
            if (_createdSession)
                await _api.Rpc(_owner, "close_session", new { p_id = _session });
            _api.Dispose();
        }

        // A fresh hub kasir.db per test, wired to public on the local stack.
        [SetUp]
        public void SetUp()
        {
            _testNo++;
            _db = TestDb.Create();
            var cfg = new ConfigRepository(_db);
            cfg.Set("register_id", "01");
            cfg.Set(InventoryService.CostEngineOwnsCostPriceKey, "true");
            _openingId = _idWindow + _testNo * 100L;

            var workerCfg = new CloudSyncConfig { PullBatchSize = 5000 };
            var sink = new GenericSink(_pg);
            _source = new ScopedPosRequestSource(_pg, r =>
                (r.ProductCode != null && _products.Contains(r.ProductCode)) || r.VendorCode == _vendor);
            _pull = new PullService(_db, _source, NullLogger<PullService>.Instance, workerCfg.PullBatchSize);
            _worker = new CloudSyncWorker(NullLogger<CloudSyncWorker>.Instance, Options.Create(workerCfg),
                new OutboxRouter(_db, new SyncQueueRepository(_db), sink, NullLogger<OutboxRouter>.Instance, workerCfg.OutboxTableList()),
                new WatermarkPusher(_db, sink, NullLogger<WatermarkPusher>.Instance),
                _pull);
        }

        [TearDown]
        public void TearDown()
        {
            _db?.Close();
            _db?.Dispose();
        }

        // A product that exists in both the cloud mirror and the hub, with opening
        // stock on the hub only (a legacy-style row below the watermark, so it is not
        // pushed). Cost Rp 3.000 / pcs, price Rp 5.000.
        private async Task<string> Product(string suffix, int openingX100)
        {
            string code = "X11B" + _run + suffix;
            _products.Add(code);
            await PgExec(@"INSERT INTO public.products (product_code, name, dept_code, status, unit, price, buying_price, cost_price, vendor_code)
                           VALUES (@c, @n, '10', 'A', 'PCS', 500000, 300000, 300000, @v)
                           ON CONFLICT (product_code) DO NOTHING",
                ("@c", code), ("@n", "BARANG UJI " + code), ("@v", _vendor));
            new ProductRepository(_db).Insert(new Product
            {
                ProductCode = code, Name = "BARANG UJI " + code, DeptCode = "10", Unit = "PCS",
                Price = 500000, BuyingPrice = 300000, CostPrice = 300000, VendorCode = _vendor,
                Status = "A", OpenPrice = "N", VatFlag = "N", LuxuryTaxFlag = "N", IsConsignment = "N"
            });
            if (openingX100 > 0)
            {
                _openingId++;
                Exec($@"INSERT INTO stock_movements (id, product_code, journal_no, movement_type, doc_date, period_code,
                        qty_in, val_in, cost_price, changed_at, created_at)
                        VALUES ({_openingId}, '{code}', 'GSMRY-2609', 'PURCHASE', '2026-09-30', '202609', {openingX100},
                        {StockQty.Value(300000, openingX100)}, 300000, '2026-09-30 20:00:00', '2026-09-30 20:00:00')");
                // Opening stock is history the cloud already has (legacy sync): push from here on.
                new ConfigRepository(_db).Set(WatermarkPusher.StockMovementsWatermarkKey,
                    _openingId.ToString(CultureInfo.InvariantCulture));
            }
            return code;
        }

        // ------------------------------------------------------------------ tests

        [Test, Order(1)]
        public async Task Count_in_dashboard_apply_is_applied_once_by_the_hub_even_when_the_mark_is_lost()
        {
            string a = await Product("A", 5000); // 50 pcs on the hub

            // Two phones count the same product: 30 + 18 = 48 (#1, #10).
            var first = await Count(_mgr1, a, 3000, "Rak depan");
            await Count(_mgr2, a, 1800, "Gudang");
            // The RPCs run with the caller's JWT: a manager, or an owner without MFA, cannot apply.
            (await FluentActions.Awaiting(() => _api.Rpc(_mgr1, "apply_opname", new { p_session_id = _session, p_product_codes = new[] { a } }))
                .Should().ThrowAsync<DashboardRpcException>()).Which.Status.Should().Be(403);
            var ownerAal1 = DashboardApi.Manager(_owner.Id, _owner.Email);
            (await FluentActions.Awaiting(() => _api.Rpc(ownerAal1, "apply_opname", new { p_session_id = _session, p_product_codes = new[] { a } }))
                .Should().ThrowAsync<DashboardRpcException>()).Which.Status.Should().Be(403);
            (await Pg("SELECT count(*) FROM public.pos_stock_requests WHERE product_code = @p", ("@p", a))).Should().Be(0L);

            var applied = await Apply(a);
            applied["queued"].Value<int>().Should().Be(1);

            // The request row is the RALPLAN 4.1 / PosStockRequest contract.
            var req = await RequestOf("OPNAME", a);
            req["qty"].Value<long>().Should().Be(4800, "qty is the COUNTED qty, not a delta");
            req["unit_cost"].Type.Should().Be(JTokenType.Null, "the hub prices it at its own average (OB-14)");
            req["target_register"].Value<string>().Should().Be("ALL");
            req["idempotency_key"].Value<string>().Should().Be("OPNAME:" + _session + ":" + a);
            DateTimeOffset.Parse(req["happened_at"].Value<string>(), CultureInfo.InvariantCulture)
                .Should().Be(DateTimeOffset.Parse(first["counted_at"].Value<string>(), CultureInfo.InvariantCulture),
                    "happened_at = opname time = first active entry");

            // Tick 1: applied locally, then the link drops before applied_at is stamped.
            _source.FailMarks = 1;
            (await _worker.TickAsync(CancellationToken.None)).Should().BeFalse("the mark failed, so the tick failed");
            _pull.LastResult.Applied.Should().HaveCount(1);
            OnHand(a).Should().Be(4800);
            (await Pg("SELECT applied_at IS NULL FROM public.pos_stock_requests WHERE id = @id", ("@id", req.Id())))
                .Should().Be(true);

            // Tick 2: the same request comes back, is found in applied_requests and only re-marked.
            (await _worker.TickAsync(CancellationToken.None)).Should().BeTrue();
            _pull.LastResult.Applied.Should().BeEmpty();
            _pull.LastResult.Remarked.Should().HaveCount(1);
            (await Pg("SELECT applied_by_register FROM public.pos_stock_requests WHERE id = @id", ("@id", req.Id())))
                .Should().Be("01");

            // Tick 3: nothing pending.
            (await _worker.TickAsync(CancellationToken.None)).Should().BeTrue();
            _pull.LastResult.Fetched.Should().Be(0);

            Scalar($"SELECT COUNT(*) FROM stock_movements WHERE product_code = '{a}' AND movement_type = 'OPNAME'").Should().Be(1);
            var m = new StockMovementRepository(_db).GetByProduct(a, "2000-01-01", "2099-12-31").Single(x => x.MovementType == "OPNAME");
            m.Id.Should().BeGreaterThanOrEqualTo(Floor, "#51 dashboard-originated rows live in the reserved range");
            m.QtyOut.Should().Be(200, "50 on the hub, 48 on the shelf");
            m.CostPrice.Should().Be(300000, "local average, not a dashboard figure");
            OnHand(a).Should().Be(4800);

            // Pressing Terapkan again after the hub applied it queues nothing.
            var again = await Apply(a);
            again["queued"].Value<int>().Should().Be(0);
            again["already_applied"].Values<string>().Should().Contain(a);
            (await Pg("SELECT count(*) FROM public.pos_stock_requests WHERE request_kind = 'OPNAME' AND product_code = @p", ("@p", a)))
                .Should().Be(1L);

            // PV-3: the pulled movement never travels back to the cloud.
            (await Pg("SELECT count(*) FROM public.stock_movements WHERE product_code = @p", ("@p", a))).Should().Be(0L);
            (await Pg("SELECT applied_counted_qty FROM public.v_opname_product_summary WHERE session_id = @s AND product_code = @p",
                ("@s", _session), ("@p", a))).Should().Be(4800L);
        }

        [Test, Order(2)]
        public async Task Sale_on_the_pos_after_the_count_is_taken_off_the_counted_qty()
        {
            string b = await Product("B", 5000); // 50 pcs

            await Count(_mgr1, b, 4800, "Rak 3"); // 48 on the shelf at the count
            await Task.Delay(1500);               // the sale is strictly later (seconds on the POS clock)
            var sale = Sell(b, 2);                 // 2 sold before "Terapkan"

            // The push carries the sale to Supabase, x100 and at its store (WITA) time.
            (await _worker.TickAsync(CancellationToken.None)).Should().BeTrue();
            var cloudSale = await PgRow(@"SELECT id, qty_out, created_at FROM public.stock_movements
                                          WHERE product_code = @p AND journal_no = @j", ("@p", b), ("@j", sale.JournalNo));
            ((long)cloudSale["qty_out"]).Should().Be(200);
            ((long)cloudSale["id"]).Should().BeLessThan(Floor);

            // The dashboard review sees it on top of the count (expected = 48 - 2).
            var sum = await Summary(b);
            ((long)sum["post_count_net_qty"]).Should().Be(-200);
            ((long)sum["expected_on_hand_qty"]).Should().Be(4600);

            await Apply(b);
            (await _worker.TickAsync(CancellationToken.None)).Should().BeTrue();

            OnHand(b).Should().Be(4600, "count 48 at the count time, then 2 sold");
            var opname = new StockMovementRepository(_db).GetByProduct(b, "2000-01-01", "2099-12-31").Single(x => x.MovementType == "OPNAME");
            opname.QtyOut.Should().Be(200, "only the shelf shortage at the count time (50 -> 48)");
            (await Pg("SELECT count(*) FROM public.stock_movements WHERE product_code = @p", ("@p", b)))
                .Should().Be(1L, "only the sale is in the cloud; the OPNAME stays on the hub");
        }

        [Test, Order(3)]
        public async Task Receipt_validated_between_the_count_and_terapkan_is_counted_once()
        {
            string c = await Product("C", 5000); // 50 pcs

            await Count(_mgr1, c, 4800, "Rak 4");
            await Task.Delay(1500);

            // 10 pcs arrive and are received on the dashboard before the owner applies.
            Guid po = await CreatePo(new JArray(PoLine(c, 1000, 250000)));
            Guid receipt = await Confirm(po);
            var val = await _api.Rpc(_mgr1, "validate_receipt", new { p_id = receipt });
            val["requests_created"].Value<int>().Should().Be(1);

            // The hub pulls the receipt first (it polls every 30 s).
            (await _worker.TickAsync(CancellationToken.None)).Should().BeTrue();
            OnHand(c).Should().Be(6000);

            var sum = await Summary(c);
            ((long)sum["post_count_net_qty"]).Should().Be(1000, "the dashboard receipt after the count is added on top");
            ((long)sum["expected_on_hand_qty"]).Should().Be(5800);
            ((bool)sum["flag_cek_dobel"]).Should().BeTrue("a receipt within 2 h of the count is flagged (D18)");

            await Apply(c);
            (await _worker.TickAsync(CancellationToken.None)).Should().BeTrue();

            OnHand(c).Should().Be(5800, "48 counted + 10 received after the count, counted once");
            Scalar($"SELECT COUNT(*) FROM stock_movements WHERE product_code = '{c}' AND movement_type = 'PURCHASE' AND id >= {Floor}")
                .Should().Be(1);
            new StockMovementRepository(_db).GetByProduct(c, "2000-01-01", "2099-12-31").Single(x => x.MovementType == "OPNAME").QtyOut
                .Should().Be(200);
            (await Pg("SELECT count(*) FROM public.stock_movements WHERE product_code = @p", ("@p", c)))
                .Should().Be(0L, "the dashboard counts its own receipt; the hub's copy is not pushed back");
        }

        [Test, Order(4)]
        public async Task Dashboard_purchasing_reaches_the_pos_once_while_pos_purchasing_stays_locked()
        {
            var lockService = new PurchasingLockService(_db);
            lockService.IsLocked.Should().BeTrue("#46: purchasing is locked on a fresh register");

            string d = await Product("D", 0);
            string e = await Product("E", 0);

            // PO: 3 dus x 24 of D (#47) + 10 pcs of E; E arrives 6 of 10 (#35).
            Guid po = await CreatePo(new JArray(
                PoLine(d, 300, 2400000, "DUS", 2400),
                PoLine(e, 1000, 280000)));
            Guid receipt = await Confirm(po);
            Guid eLine = (Guid)await Pg("SELECT id FROM public.dashboard_receipt_lines WHERE receipt_id = @r AND product_code = @p",
                ("@r", receipt), ("@p", e));
            await _api.Rpc(_mgr1, "update_receipt_line", new { p_line_id = eLine, p_qty_received = 600 });
            var val = await _api.Rpc(_mgr1, "validate_receipt", new { p_id = receipt, p_create_backorder = true });
            Guid backorder = Guid.Parse(val["backorder_id"].Value<string>());
            string receiptNo = (string)await Pg("SELECT doc_no FROM public.dashboard_receipts WHERE id = @r", ("@r", receipt));
            string poNo = (string)await Pg("SELECT doc_no FROM public.dashboard_purchase_orders WHERE id = @o", ("@o", po));

            // #50: the link drops after the first local apply; nothing is applied twice.
            _source.FailMarks = 1;
            await _worker.TickAsync(CancellationToken.None);
            (await _worker.TickAsync(CancellationToken.None)).Should().BeTrue();
            (await _worker.TickAsync(CancellationToken.None)).Should().BeTrue();

            OnHand(d).Should().Be(7200, "#47 3 dus x 24 = 72 pcs");
            OnHand(e).Should().Be(600);
            Scalar($"SELECT COUNT(*) FROM stock_movements WHERE journal_no = '{receiptNo}'").Should().Be(2, "one movement per receipt line");
            Scalar($"SELECT COUNT(*) FROM stock_movements WHERE journal_no = '{receiptNo}' AND id >= {Floor}").Should().Be(2, "#51");
            Text($"SELECT doc_type || ' ' || sub_code || ' ' || legacy_source FROM purchases WHERE journal_no = '{receiptNo}'")
                .Should().Be("RECEIPT " + _vendor + " DASHBOARD");
            Text($"SELECT group_concat(product_code || ':' || quantity || ':' || unit_price || ':' || order_ref, ',') FROM (SELECT * FROM purchase_items WHERE journal_no = '{receiptNo}' ORDER BY product_code)")
                .Should().Be($"{d}:72:100000:{poNo},{e}:6:280000:{poNo}", "lines keep the PO number as order_ref");
            new ProductRepository(_db).GetByCode(d).CostPrice.Should().Be(100000, "Rp 24.000 / dus = Rp 1.000 / pcs (PR-K1)");

            // Backorder (4 pcs of E) arrives later.
            await _api.Rpc(_mgr1, "validate_receipt", new { p_id = backorder });
            (await _worker.TickAsync(CancellationToken.None)).Should().BeTrue();
            OnHand(e).Should().Be(1000);
            Scalar($"SELECT COUNT(*) FROM purchases WHERE doc_type = 'RECEIPT' AND sub_code = '{_vendor}'").Should().Be(2);

            // #53: posted bill -> payables_register on the hub.
            Guid bill = Guid.Parse((string)await _api.Rpc(_mgr1, "create_vendor_bill",
                new { p_vendor_code = _vendor, p_order_id = po, p_vendor_invoice_no = "INV-" + _run }));
            (await _api.Rpc(_owner, "post_vendor_bill", new { p_id = bill }))["status"].Value<string>().Should().Be("posted");
            var billRow = await PgRow("SELECT doc_no, total, due_date FROM public.dashboard_vendor_bills WHERE id = @b", ("@b", bill));
            string billNo = (string)billRow["doc_no"];
            long billTotal = (long)billRow["total"];
            (await _worker.TickAsync(CancellationToken.None)).Should().BeTrue();
            _pull.LastResult.Failed.Should().BeEmpty("the VENDOR_BILL the dashboard writes must be one the hub can apply");
            var ap = new PayablesRepository(_db).GetByJournalNo(billNo);
            ap.Should().NotBeNull();
            ap.SubCode.Should().Be(_vendor);
            ap.Amount.Should().Be(billTotal);
            ap.Direction.Should().Be("D");
            Text($"SELECT ref FROM payables_register WHERE journal_no = '{billNo}'").Should().Be("INV-" + _run);

            // Return 2 pcs of E to the vendor for a refund -> RETURN_OUT at the receipt cost.
            Guid ret = Guid.Parse((string)await _api.Rpc(_mgr1, "create_return", new
            {
                p_receipt_id = receipt,
                p_lines = new JArray(new JObject { ["receipt_line_id"] = eLine, ["qty"] = 200, ["to_refund"] = true })
            }));
            await _api.Rpc(_owner, "validate_return", new { p_id = ret });
            string returnNo = (string)await Pg("SELECT doc_no FROM public.dashboard_receipts WHERE id = @r", ("@r", ret));
            (await _worker.TickAsync(CancellationToken.None)).Should().BeTrue();
            OnHand(e).Should().Be(800);
            var rm = new StockMovementRepository(_db).GetByJournal(returnNo).Single();
            rm.MovementType.Should().Be("RETURN_OUT");
            rm.QtyOut.Should().Be(200);
            rm.CostPrice.Should().Be(280000);
            Text($"SELECT doc_type || ' ' || ref_no FROM purchases WHERE journal_no = '{returnNo}'")
                .Should().Be("PURCHASE_RETURN " + receiptNo, "the return points at the receipt it reverses");

            // Credit note for the refunded 2 pcs lowers what the hub owes the vendor.
            Guid eBillLine = (Guid)await Pg("SELECT id FROM public.dashboard_vendor_bill_lines WHERE bill_id = @b AND product_code = @p",
                ("@b", bill), ("@p", e));
            Guid cn = Guid.Parse((string)await _api.Rpc(_mgr1, "create_credit_note", new
            {
                p_bill_id = bill,
                p_lines = new JArray(new JObject { ["bill_line_id"] = eBillLine, ["qty"] = 200 })
            }));
            await _api.Rpc(_owner, "post_vendor_bill", new { p_id = cn });
            long cnTotal = (long)await Pg("SELECT total FROM public.dashboard_vendor_bills WHERE id = @b", ("@b", cn));
            cnTotal.Should().Be(560000, "2 pcs x Rp 2.800");
            (await _worker.TickAsync(CancellationToken.None)).Should().BeTrue();
            _pull.LastResult.Failed.Should().BeEmpty();
            new PayablesRepository(_db).GetTotalUnpaidByVendor(_vendor).Should().Be(billTotal - cnTotal,
                "the credit note reduces the payable (dashboard_payables: bill + negative credit note)");
            (await Pg("SELECT sum(amount) FROM public.dashboard_payables WHERE vendor_code = @v", ("@v", _vendor)))
                .Should().Be(billTotal - cnTotal);

            // Replay: every request of this test is applied and marked; another tick adds nothing.
            long movements = Scalar("SELECT COUNT(*) FROM stock_movements");
            (await _worker.TickAsync(CancellationToken.None)).Should().BeTrue();
            _pull.LastResult.Fetched.Should().Be(0);
            Scalar("SELECT COUNT(*) FROM stock_movements").Should().Be(movements);
            (await Pg(@"SELECT count(*) FROM public.pos_stock_requests
                        WHERE applied_at IS NULL AND (vendor_code = @v OR product_code = ANY(@p))",
                ("@v", _vendor), ("@p", new[] { d, e }))).Should().Be(0L);

            // The dashboard path never opened the POS purchasing screens.
            lockService.IsLocked.Should().BeTrue("pulling dashboard purchases does not unlock POS purchasing");
            Scalar($"SELECT COUNT(*) FROM config_audit WHERE key = '{PurchasingLockService.ConfigKey}'").Should().Be(0);
        }

        // D21 / WP-13 (dashboard 0074): a product made in the dashboard (NP code), received
        // by the dus on Barang Masuk Cepat and counted, reaches the hub in that order: the
        // NEW_PRODUCT first (with its pack), then the PURCHASE, then the OPNAME.
        [Test, Order(5)]
        public async Task New_product_made_in_the_dashboard_is_created_received_and_counted_on_the_hub()
        {
            if (!Convert.ToBoolean(await Pg("SELECT to_regprocedure('public.create_dashboard_product(text,text,text,bigint,bigint,text,bigint,text,text,uuid,text)') IS NOT NULL")))
                Assert.Ignore("dashboard migration 0074 is not applied on this stack (run pnpm db:reset:local)");

            string name = "PRODUK BARU UJI " + _run;
            var made = (JObject)await _api.Rpc(_mgr1, "create_dashboard_product", new
            {
                p_name = name.ToLowerInvariant(), p_dept_code = "10", p_unit = "pcs",
                p_cost_price = 300000L, p_price = 500000L, p_pack_unit = "dus", p_pack_qty = 1200L,
                p_vendor_code = _vendor, p_source = "direct_receipt", p_client_key = "x-np-" + _run
            });
            string np = made["product_code"].Value<string>();
            np.Should().MatchRegex("^NP[0-9]{4,}$");
            _products.Add(np);
            // A replay of the same submit makes no second product.
            var replay = (JObject)await _api.Rpc(_mgr1, "create_dashboard_product", new
            {
                p_name = name, p_dept_code = "10", p_unit = "PCS", p_cost_price = 300000L, p_price = 500000L,
                p_pack_unit = "DUS", p_pack_qty = 1200L, p_vendor_code = _vendor, p_source = "direct_receipt",
                p_client_key = "x-np-" + _run
            });
            replay["product_code"].Value<string>().Should().Be(np);
            replay["created"].Value<bool>().Should().BeFalse();

            // 2 dus x 12 on Barang Masuk Cepat, then a count of 20 pcs.
            var rc = (JObject)await _api.Rpc(_mgr1, "create_direct_receipt", new
            {
                p_vendor_code = _vendor,
                p_lines = new JArray(PoLine(np, 200, 3600000, "DUS", 1200))
            });
            rc["requests_created"].Value<int>().Should().Be(1);
            await Task.Delay(1500); // the count is strictly after the receipt
            await Count(_mgr1, np, 2000, "Rak produk baru");
            await Apply(np);

            var newReq = await RequestOf("NEW_PRODUCT", np);
            newReq["idempotency_key"].Value<string>().Should().Be("NEW_PRODUCT:" + np);
            newReq["payload"]["name"].Value<string>().Should().Be(name);

            (await _worker.TickAsync(CancellationToken.None)).Should().BeTrue();
            _pull.LastResult.Failed.Should().BeEmpty();
            _pull.LastResult.Applied.Should().HaveCount(3);
            _pull.LastResult.Applied[0].Should().Be(newReq.Id(), "the product lands before its receipt and count");

            var p = new ProductRepository(_db).GetByCode(np);
            p.Should().NotBeNull();
            (p.Name, p.DeptCode, p.Unit, p.Status, p.Price, p.VendorCode).Should().Be((name, "10", "PCS", "A", 500000L, _vendor));
            Text($"SELECT unit2 || ' x ' || conversion1 FROM products WHERE product_code = '{np}'").Should().Be("DUS x 1200");
            OnHand(np).Should().Be(2000, "24 received, 20 counted");
            var opn = new StockMovementRepository(_db).GetByProduct(np, "2000-01-01", "2099-12-31").Single(x => x.MovementType == "OPNAME");
            opn.QtyOut.Should().Be(400);
            opn.Id.Should().BeGreaterThanOrEqualTo(Floor);
            new ProductRepository(_db).GetByCode(np).CostPrice.Should().Be(300000, "Rp 36.000 / dus of 12");

            (await Pg(@"SELECT count(*) FROM public.pos_stock_requests WHERE product_code = @p AND applied_at IS NOT NULL",
                ("@p", np))).Should().Be(3L);
            // The hub's copy of the product, if pushed back, matches what the dashboard made.
            var cloud = await PgRow("SELECT name, unit2, conversion1, status FROM public.products WHERE product_code = @p", ("@p", np));
            ((string)cloud["name"], (string)cloud["unit2"], (long)cloud["conversion1"], (string)cloud["status"])
                .Should().Be((name, "DUS", 1200L, "A"));

            (await _worker.TickAsync(CancellationToken.None)).Should().BeTrue();
            _pull.LastResult.Fetched.Should().Be(0, "nothing left for this product");
        }

        // ------------------------------------------------------------------ dashboard helpers

        private async Task<JObject> Count(DashboardApi.User who, string product, int qtyX100, string location)
        {
            var row = await _api.Rpc(who, "insert_count_entry", new
            {
                p_session_id = _session,
                p_product_code = product,
                p_counted_qty = qtyX100,
                p_client_entry_id = Guid.NewGuid(),
                p_location_label = location,
                p_device_label = "wp11b-" + _run
            });
            return (JObject)row;
        }

        private async Task<JObject> Apply(string product) =>
            (JObject)await _api.Rpc(_owner, "apply_opname", new { p_session_id = _session, p_product_codes = new[] { product } });

        private async Task<Dictionary<string, object>> Summary(string product) =>
            await PgRow(@"SELECT post_count_net_qty, expected_on_hand_qty, flag_cek_dobel, flag_cek_ulang
                          FROM public.v_opname_product_summary WHERE session_id = @s AND product_code = @p",
                ("@s", _session), ("@p", product));

        private static JObject PoLine(string product, int qty, long unitPrice, string uom = null, int packQty = 0)
        {
            var o = new JObject { ["product_code"] = product, ["qty"] = qty, ["unit_price"] = unitPrice, ["tax_code"] = "NONE" };
            if (uom != null)
            {
                o["purchase_uom"] = uom;
                o["pack_qty"] = packQty;
            }
            return o;
        }

        private async Task<Guid> CreatePo(JArray lines) =>
            Guid.Parse((string)await _api.Rpc(_mgr1, "create_purchase_order", new { p_vendor_code = _vendor, p_lines = lines }));

        private async Task<Guid> Confirm(Guid po)
        {
            var res = await _api.Rpc(_owner, "confirm_purchase_order", new { p_id = po });
            res["status"].Value<string>().Should().Be("ordered");
            return Guid.Parse(res["receipt_id"].Value<string>());
        }

        private async Task<JObject> RequestOf(string kind, string product)
        {
            var row = await PgRow(@"SELECT row_to_json(r)::text AS j FROM public.pos_stock_requests r
                                    WHERE request_kind = @k AND product_code = @p", ("@k", kind), ("@p", product));
            return JObject.Parse((string)row["j"]);
        }

        // ------------------------------------------------------------------ POS helpers

        private sealed class StoreClock : IClock
        {
            public DateTime Now { get; set; }
            public DateTime UtcNow => Now - StoreOffset;
        }

        // A sale at the register's wall clock (WITA) right now.
        private Sale Sell(string product, int units)
        {
            var now = DateTimeOffset.UtcNow.ToOffset(StoreOffset).DateTime;
            now = new DateTime(now.Year, now.Month, now.Day, now.Hour, now.Minute, now.Second);
            var sales = new SalesService(_db, new StoreClock { Now = now });
            sales.SetCashier("ADM", 1);
            sales.AddItem(product, units);
            var sale = sales.CompleteSale(100000000, 0, 0, "", "", "");
            // The movement row takes SQLite localtime by default; pin it to the WITA clock
            // so the test does not depend on the machine's time zone.
            string ts = now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
            Exec($"UPDATE stock_movements SET created_at = '{ts}', changed_at = '{ts}' WHERE journal_no = '{sale.JournalNo}'");
            return sale;
        }

        private int OnHand(string code) => new InventoryService(_db).GetStockOnHand(code);

        private void Exec(string sql)
        {
            using var cmd = _db.CreateCommand();
            cmd.CommandText = sql;
            cmd.ExecuteNonQuery();
        }

        private long Scalar(string sql) => SqlHelper.ExecuteScalar<long>(_db, sql);
        private string Text(string sql) => SqlHelper.ExecuteScalar<string>(_db, sql);

        // ------------------------------------------------------------------ Postgres helpers

        private async Task<object> Pg(string sql, params (string, object)[] args)
        {
            await using var conn = new NpgsqlConnection(_pg);
            await conn.OpenAsync();
            await using var cmd = new NpgsqlCommand(sql, conn);
            foreach (var (n, v) in args) cmd.Parameters.AddWithValue(n, v);
            var r = await cmd.ExecuteScalarAsync();
            return r is DBNull ? null : r;
        }

        private async Task PgExec(string sql, params (string, object)[] args)
        {
            await using var conn = new NpgsqlConnection(_pg);
            await conn.OpenAsync();
            await using var cmd = new NpgsqlCommand(sql, conn);
            foreach (var (n, v) in args) cmd.Parameters.AddWithValue(n, v);
            await cmd.ExecuteNonQueryAsync();
        }

        private async Task<Dictionary<string, object>> PgRow(string sql, params (string, object)[] args)
        {
            await using var conn = new NpgsqlConnection(_pg);
            await conn.OpenAsync();
            await using var cmd = new NpgsqlCommand(sql, conn);
            foreach (var (n, v) in args) cmd.Parameters.AddWithValue(n, v);
            await using var r = await cmd.ExecuteReaderAsync();
            if (!await r.ReadAsync()) throw new AssertionException("no row for: " + sql);
            var row = new Dictionary<string, object>();
            for (int i = 0; i < r.FieldCount; i++) row[r.GetName(i)] = r.IsDBNull(i) ? null : r.GetValue(i);
            return row;
        }
    }

    internal static class CrossRepoJson
    {
        public static Guid Id(this JObject row) => Guid.Parse(row["id"].Value<string>());
    }
}
