using System;
using System.IO;
using System.Linq;
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
using Npgsql;
using NUnit.Framework;

namespace Kasir.CloudSync.Tests.Pull
{
    // WP-04 acceptance against a real (LOCAL) Supabase Postgres: requests written to
    // pos_stock_requests reach kasir.db through PostgresPosRequestSource, are applied
    // once and are marked applied in Postgres.
    //
    // [Explicit]: needs a LOCAL database, e.g. `supabase start` in sinar-makmur-dashboard:
    //   export KASIR_CLOUDSYNC_TEST_PG="Host=127.0.0.1;Port=54322;Database=postgres;Username=postgres;Password=postgres"
    //   dotnet test Kasir.CloudSync.Tests --filter "FullyQualifiedName~PullServicePostgresTests"
    // Each test runs in its own throw-away schema. pos_stock_requests is cloned from
    // public (the real dashboard 0066 shape, LIKE ... INCLUDING ALL) when the local
    // stack has it, else created from pos_stock_requests.fixture.sql (RALPLAN 4.1).
    // public is never written. Refuses non-local hosts. WP-11b re-runs this once the
    // dashboard migration is applied locally, so the clone path is exercised.
    [TestFixture]
    [Explicit("Needs a local Postgres via KASIR_CLOUDSYNC_TEST_PG")]
    public class PullServicePostgresTests
    {
        private string _baseConn;
        private string _schema;
        private string _conn;
        private SqliteConnection _db;
        private bool _clonedFromPublic;

        [SetUp]
        public async Task SetUp()
        {
            _baseConn = Environment.GetEnvironmentVariable("KASIR_CLOUDSYNC_TEST_PG");
            if (string.IsNullOrWhiteSpace(_baseConn)) Assert.Ignore("KASIR_CLOUDSYNC_TEST_PG not set");
            var csb = new NpgsqlConnectionStringBuilder(_baseConn);
            if (csb.Host != "localhost" && csb.Host != "127.0.0.1") Assert.Ignore("refusing non-local host " + csb.Host);

            _schema = "wp04_" + Guid.NewGuid().ToString("N").Substring(0, 12);
            csb.SearchPath = _schema;
            _conn = csb.ToString();

            await using var pg = new NpgsqlConnection(_baseConn);
            await pg.OpenAsync();
            await ExecAsync(pg, $"CREATE SCHEMA {_schema};");
            _clonedFromPublic = Convert.ToBoolean(await ScalarAsync(pg,
                "SELECT to_regclass('public.pos_stock_requests') IS NOT NULL;"));
            if (_clonedFromPublic)
            {
                await ExecAsync(pg, $"CREATE TABLE {_schema}.pos_stock_requests (LIKE public.pos_stock_requests INCLUDING ALL);");
            }
            else
            {
                string fixture = Path.Combine(TestContext.CurrentContext.TestDirectory,
                    "..", "..", "..", "Pull", "pos_stock_requests.fixture.sql");
                await ExecAsync(pg, $"SET search_path TO {_schema}; " + File.ReadAllText(fixture) + " RESET search_path;");
            }
            TestContext.Progress.WriteLine("pos_stock_requests shape: " + (_clonedFromPublic ? "public (dashboard migration)" : "fixture"));

            _db = TestDb.Create();
            var cfg = new ConfigRepository(_db);
            cfg.Set("register_id", "01");
            cfg.Set(InventoryService.CostEngineOwnsCostPriceKey, "true");
            foreach (var code in new[] { "P001", "P002" })
            {
                new ProductRepository(_db).Insert(new Product
                {
                    ProductCode = code, Name = code, Price = 500000, CostPrice = 300000,
                    Status = "A", OpenPrice = "N", VatFlag = "N", LuxuryTaxFlag = "N", IsConsignment = "N"
                });
            }
            Exec(@"INSERT INTO stock_movements (product_code, journal_no, movement_type, doc_date, period_code, qty_in, changed_at)
                   VALUES ('P001', 'GSMRY-2609', 'PURCHASE', '2026-09-30', '202609', 5000, '2026-09-30 20:00:00')");
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

        private async Task<object> Pg(string sql)
        {
            await using var pg = new NpgsqlConnection(_conn);
            await pg.OpenAsync();
            return await ScalarAsync(pg, sql);
        }

        private void Exec(string sql)
        {
            using var cmd = _db.CreateCommand();
            cmd.CommandText = sql;
            cmd.ExecuteNonQuery();
        }

        private PullService Pull() =>
            new PullService(_db, new PostgresPosRequestSource(_conn), NullLogger<PullService>.Instance);

        [Test]
        public async Task OpnameRequest_CreatesMovementAboveTheFloor_AndIsMarkedApplied()
        {
            await Pg(@"INSERT INTO pos_stock_requests (request_kind, idempotency_key, product_code, qty, unit_cost, doc_no, happened_at)
                       VALUES ('OPNAME', 'OPNAME:sess:P001', 'P001', 4800, NULL, 'OPN-DB-OKT26', '2026-10-09 09:00:00+08')");

            (await Pull().TickAsync(CancellationToken.None)).Should().Be(1);

            SqlHelper.ExecuteScalar<long>(_db, "SELECT id FROM stock_movements WHERE movement_type = 'OPNAME'")
                .Should().BeGreaterThanOrEqualTo(5_000_000_000L);
            new InventoryService(_db).GetStockOnHand("P001").Should().Be(4800);
            SqlHelper.ExecuteScalar<long>(_db, "SELECT cost_price FROM stock_movements WHERE movement_type = 'OPNAME'")
                .Should().Be(300000, "cost is the local average");
            (await Pg("SELECT applied_at IS NOT NULL FROM pos_stock_requests WHERE idempotency_key = 'OPNAME:sess:P001'"))
                .Should().Be(true);
            (await Pg("SELECT applied_by_register FROM pos_stock_requests WHERE idempotency_key = 'OPNAME:sess:P001'"))
                .Should().Be("01");
        }

        // qty is BIGINT in Supabase: a value above int range is read (no OverflowException
        // on every tick) and the request is marked failed with a reason.
        [Test]
        public async Task OpnameRequest_WithQtyAboveIntRange_IsReadAndMarkedFailed()
        {
            await Pg(@"INSERT INTO pos_stock_requests (request_kind, idempotency_key, product_code, qty, doc_no, happened_at)
                       VALUES ('OPNAME', 'OPNAME:big:P001', 'P001', 2400000000, 'OPN-DB-OKT26', '2026-10-09 09:00:00+08')");

            var fetched = await new PostgresPosRequestSource(_conn).FetchPendingAsync(10, CancellationToken.None);
            fetched.Single().Qty.Should().Be(2_400_000_000L);

            var pull = Pull();
            (await pull.TickAsync(CancellationToken.None)).Should().Be(0);
            pull.LastResult.Rejected.Should().HaveCount(1);
            (await Pg("SELECT failed_at IS NOT NULL FROM pos_stock_requests WHERE idempotency_key = 'OPNAME:big:P001'"))
                .Should().Be(true);
            ((string)await Pg("SELECT failed_reason FROM pos_stock_requests WHERE idempotency_key = 'OPNAME:big:P001'"))
                .Should().Contain("out of range");
            new InventoryService(_db).GetStockOnHand("P001").Should().Be(5000);
        }

        [Test]
        public async Task PurchaseRequest_CreatesMovementAndPurchase_AndRepollingAddsNothing()
        {
            await Pg(@"INSERT INTO pos_stock_requests (request_kind, idempotency_key, product_code, qty, unit_cost, vendor_code, doc_no, payload)
                       VALUES ('PURCHASE', 'PURCHASE:line-1', 'P002', 7200, 100000, 'V001', 'RCV-0001', '{""po_no"":""PO-7""}')");

            var pull = Pull();
            (await pull.TickAsync(CancellationToken.None)).Should().Be(1);
            await Pg("UPDATE pos_stock_requests SET applied_at = NULL, applied_by_register = NULL"); // re-poll
            (await pull.TickAsync(CancellationToken.None)).Should().Be(0);
            pull.LastResult.Remarked.Should().HaveCount(1);

            new InventoryService(_db).GetStockOnHand("P002").Should().Be(7200);
            SqlHelper.ExecuteScalar<long>(_db, "SELECT COUNT(*) FROM stock_movements WHERE journal_no = 'RCV-0001'").Should().Be(1);
            SqlHelper.ExecuteScalar<string>(_db, "SELECT doc_type || ' ' || sub_code FROM purchases WHERE journal_no = 'RCV-0001'")
                .Should().Be("RECEIPT V001");
            SqlHelper.ExecuteScalar<long>(_db, "SELECT quantity FROM purchase_items WHERE journal_no = 'RCV-0001'").Should().Be(72);
            (await Pg("SELECT count(*) FROM pos_stock_requests WHERE applied_at IS NULL")).Should().Be(0L);
        }

        [Test]
        public async Task SqlOrder_PutsNewProductFirst_AndOtherRegistersAreSkipped()
        {
            await Pg(@"INSERT INTO pos_stock_requests (request_kind, idempotency_key, product_code, qty, unit_cost, vendor_code, doc_no, created_at)
                       VALUES ('PURCHASE', 'PURCHASE:np', 'NP0001', 500, 250000, 'V001', 'RCV-2', '2026-10-09 10:00:00+08');
                       INSERT INTO pos_stock_requests (request_kind, idempotency_key, product_code, payload, created_at)
                       VALUES ('NEW_PRODUCT', 'NEW_PRODUCT:NP0001', 'NP0001', '{""name"":""Lampu""}', '2026-10-09 10:00:00+08');
                       INSERT INTO pos_stock_requests (request_kind, idempotency_key, product_code, payload, target_register)
                       VALUES ('PRODUCT_STATUS', 'PRODUCT_STATUS:P001', 'P001', '{""status"":""I""}', 'KLR-02');");

            (await Pull().TickAsync(CancellationToken.None)).Should().Be(2);

            new InventoryService(_db).GetStockOnHand("NP0001").Should().Be(500);
            new ProductRepository(_db).GetByCode("P001").Status.Should().Be("A", "a KLR-02 request is not the hub's");
            (await Pg("SELECT applied_at IS NULL FROM pos_stock_requests WHERE target_register = 'KLR-02'")).Should().Be(true);
        }

        // D21: a NEW_PRODUCT queued after the receipt that needs it is still fetched
        // first, even when the batch limit only takes one row.
        [Test]
        public async Task SqlOrder_FetchesNewProductFirst_EvenWhenQueuedLater_UnderABatchLimit()
        {
            await Pg(@"INSERT INTO pos_stock_requests (request_kind, idempotency_key, product_code, qty, unit_cost, vendor_code, doc_no, created_at)
                       VALUES ('PURCHASE', 'PURCHASE:np3', 'NP0003', 500, 250000, 'V001', 'RCV-3', '2026-10-09 10:00:00+08');
                       INSERT INTO pos_stock_requests (request_kind, idempotency_key, product_code, payload, created_at)
                       VALUES ('NEW_PRODUCT', 'NEW_PRODUCT:NP0003', 'NP0003', '{""name"":""Lampu 3""}', '2026-10-09 10:05:00+08');");
            var source = new PostgresPosRequestSource(_conn);

            var first = await source.FetchPendingAsync(1, CancellationToken.None);
            first.Select(r => r.RequestKind).Should().Equal("NEW_PRODUCT");

            var pull = new PullService(_db, source, NullLogger<PullService>.Instance, 1);
            (await pull.TickAsync(CancellationToken.None)).Should().Be(1);
            (await pull.TickAsync(CancellationToken.None)).Should().Be(1);
            new InventoryService(_db).GetStockOnHand("NP0003").Should().Be(500);
        }

        [Test]
        public async Task Mark_DoesNotOverwriteAnEarlierApply()
        {
            await Pg(@"INSERT INTO pos_stock_requests (id, request_kind, idempotency_key, applied_at, applied_by_register)
                       VALUES ('00000000-0000-0000-0000-000000000001', 'BARCODE_LINK', 'BARCODE_LINK:x', '2026-10-01 00:00:00+00', 'KLR-03')");
            var source = new PostgresPosRequestSource(_conn);

            (await source.MarkAppliedAsync(Guid.Parse("00000000-0000-0000-0000-000000000001"), "01",
                DateTimeOffset.UtcNow, CancellationToken.None)).Should().BeFalse();
            (await Pg("SELECT applied_by_register FROM pos_stock_requests")).Should().Be("KLR-03");
        }

        // Follow-up item 7 (K1/K4): a stock request on a non-stock code is marked failed
        // with its reason (dashboard 0072 columns) and is no longer fetched.
        [Test]
        public async Task NonStockRequest_IsMarkedFailedWithReason_AndNotFetchedAgain()
        {
            await Pg(@"INSERT INTO pos_stock_requests (request_kind, idempotency_key, product_code, qty, unit_cost, vendor_code, doc_no, payload)
                       VALUES ('PURCHASE', 'PURCHASE:ns-1', 'AL', 100, 500000, 'V001', 'RCV-NS', '{""po_no"":""PO-9""}');
                       INSERT INTO pos_stock_requests (request_kind, idempotency_key, product_code, qty, doc_no, happened_at)
                       VALUES ('OPNAME', 'OPNAME:sess:44', '44', 300, 'OPN-DB-OKT26', '2026-10-09 09:00:00+08');");

            var pull = Pull();
            (await pull.TickAsync(CancellationToken.None)).Should().Be(0);
            pull.LastResult.Rejected.Should().HaveCount(2);

            (await Pg("SELECT count(*) FROM pos_stock_requests WHERE failed_at IS NOT NULL AND applied_at IS NULL")).Should().Be(2L);
            (await Pg("SELECT failed_reason FROM pos_stock_requests WHERE idempotency_key = 'PURCHASE:ns-1'")).Should()
                .Be("PURCHASE PURCHASE:ns-1: AL is not a stock item (manual price code / category key); the POS never counts, buys or returns it");
            (await Pg("SELECT failed_by_register FROM pos_stock_requests WHERE idempotency_key = 'OPNAME:sess:44'")).Should().Be("01");
            SqlHelper.ExecuteScalar<long>(_db, "SELECT COUNT(*) FROM stock_movements WHERE id >= 5000000000").Should().Be(0);
            SqlHelper.ExecuteScalar<long>(_db, "SELECT COUNT(*) FROM purchases").Should().Be(0);

            (await new PostgresPosRequestSource(_conn).FetchPendingAsync(200, CancellationToken.None)).Should().BeEmpty();
            (await pull.TickAsync(CancellationToken.None)).Should().Be(0);
            pull.LastResult.Fetched.Should().Be(0);
        }

        [Test]
        public async Task MarkFailed_DoesNotTouchAnAppliedOrFailedRow()
        {
            await Pg(@"INSERT INTO pos_stock_requests (id, request_kind, idempotency_key, applied_at, applied_by_register)
                       VALUES ('00000000-0000-0000-0000-000000000002', 'BARCODE_LINK', 'BARCODE_LINK:y', '2026-10-01 00:00:00+00', 'KLR-03');
                       INSERT INTO pos_stock_requests (id, request_kind, idempotency_key, failed_at, failed_reason)
                       VALUES ('00000000-0000-0000-0000-000000000003', 'OPNAME', 'OPNAME:z', '2026-10-01 00:00:00+00', 'first');");
            var source = new PostgresPosRequestSource(_conn);

            (await source.MarkFailedAsync(Guid.Parse("00000000-0000-0000-0000-000000000002"), "01", DateTimeOffset.UtcNow,
                "x", CancellationToken.None)).Should().BeFalse();
            (await source.MarkFailedAsync(Guid.Parse("00000000-0000-0000-0000-000000000003"), "01", DateTimeOffset.UtcNow,
                "second", CancellationToken.None)).Should().BeFalse();
            (await Pg("SELECT failed_at IS NULL FROM pos_stock_requests WHERE idempotency_key = 'BARCODE_LINK:y'")).Should().Be(true);
            (await Pg("SELECT failed_reason FROM pos_stock_requests WHERE idempotency_key = 'OPNAME:z'")).Should().Be("first");
        }
    }
}
