using System;
using System.Globalization;
using Kasir.CloudSync.Mappers;
using Kasir.Data;
using Kasir.Data.Repositories;
using Kasir.Models;
using Kasir.Services;
using Microsoft.Data.Sqlite;
using Newtonsoft.Json.Linq;

namespace Kasir.CloudSync.Pull
{
    public enum ApplyOutcome
    {
        Applied,
        // Found in applied_requests: applied by an earlier tick whose Supabase mark
        // did not land. Nothing written; the caller only re-marks it.
        AlreadyApplied
    }

    // The request cannot be applied (yet). Nothing was written. Deferred = a
    // prerequisite is missing locally (e.g. the product of a NEW_PRODUCT not applied
    // yet) and a later tick may succeed; otherwise the request data is invalid.
    public sealed class PosRequestApplyException : Exception
    {
        public bool Deferred { get; }

        public PosRequestApplyException(string message, bool deferred) : base(message)
        {
            Deferred = deferred;
        }
    }

    // Applies one pos_stock_requests row to the local kasir.db in ONE SQLite
    // transaction: the documents, the stock movement(s) and the applied_requests row
    // commit together or not at all, so a crash or a failed Supabase mark can never
    // apply a request twice (WP-04 task 5).
    //
    // Reuses the POS engines instead of re-implementing them: InventoryService for
    // movements and the perpetual moving average (PR-K1), the PR-K6 count-time rule
    // for OPNAME (on-hand at the count = on-hand now minus movements after it), and
    // CalculateAverageCost for OPNAME cost (OB-14, StockOpnameService pattern).
    //
    // Every movement gets an id from the dashboard range (>= 5,000,000,000, OB-13;
    // config pull_movement_id_seq) and is stamped at the request's happened_at (WIB
    // wall clock), so later count-time comparisons and the cloud push filter
    // (WatermarkPusher skips id >= floor, PV-3) both see it correctly.
    public sealed class PosRequestApplier
    {
        public const string MovementIdSeqKey = "pull_movement_id_seq";
        // changed_by on rows written by the pull worker (no POS user is involved).
        public const int PullUserId = 0;
        private const int MaxNameLength = 75; // legacy NAME C(75)

        private readonly SqliteConnection _db;
        private readonly ConfigRepository _config;
        private readonly ProductRepository _products;
        private readonly StockMovementRepository _movements;
        private readonly PayablesRepository _payables;
        private readonly InventoryService _inventory;

        public PosRequestApplier(SqliteConnection db)
        {
            _db = db ?? throw new ArgumentNullException(nameof(db));
            _config = new ConfigRepository(db);
            _products = new ProductRepository(db);
            _movements = new StockMovementRepository(db);
            _payables = new PayablesRepository(db);
            _inventory = new InventoryService(db);
        }

        public bool IsApplied(PosStockRequest r)
        {
            return SqlHelper.ExecuteScalar<long>(_db,
                "SELECT COUNT(*) FROM applied_requests WHERE request_kind = @k AND idempotency_key = @key",
                SqlHelper.Param("@k", r.RequestKind),
                SqlHelper.Param("@key", r.IdempotencyKey)) > 0;
        }

        public ApplyOutcome Apply(PosStockRequest r)
        {
            if (r == null) throw new ArgumentNullException(nameof(r));
            if (string.IsNullOrWhiteSpace(r.RequestKind) || string.IsNullOrWhiteSpace(r.IdempotencyKey))
                throw Invalid(r, "request_kind and idempotency_key are required");

            using (var txn = _db.BeginTransaction())
            {
                try
                {
                    if (IsApplied(r))
                    {
                        txn.Rollback();
                        return ApplyOutcome.AlreadyApplied;
                    }

                    string journalNo = Dispatch(r);

                    SqlHelper.ExecuteNonQuery(_db,
                        @"INSERT INTO applied_requests (request_kind, idempotency_key, request_id, journal_no)
                          VALUES (@k, @key, @id, @jnl)",
                        SqlHelper.Param("@k", r.RequestKind),
                        SqlHelper.Param("@key", r.IdempotencyKey),
                        SqlHelper.Param("@id", r.Id.ToString()),
                        SqlHelper.Param("@jnl", journalNo));

                    txn.Commit();
                    return ApplyOutcome.Applied;
                }
                catch
                {
                    txn.Rollback();
                    throw;
                }
            }
        }

        // Returns the local document written (journal_no), or null.
        private string Dispatch(PosStockRequest r)
        {
            switch (r.RequestKind)
            {
                case PosRequestKinds.Opname: return ApplyOpname(r);
                case PosRequestKinds.Purchase: return ApplyPurchase(r);
                case PosRequestKinds.ReturnOut: return ApplyReturnOut(r);
                case PosRequestKinds.VendorBill: return ApplyVendorBill(r);
                case PosRequestKinds.ProductStatus: return ApplyProductStatus(r);
                case PosRequestKinds.NewProduct: return ApplyNewProduct(r);
                case PosRequestKinds.BarcodeLink: return null; // POS has no barcode table (Migration_005)
                default: throw Invalid(r, "unknown request_kind " + r.RequestKind);
            }
        }

        // ---------- OPNAME ----------

        private string ApplyOpname(PosStockRequest r)
        {
            var product = RequireProduct(r);
            int counted = r.Qty ?? throw Invalid(r, "qty (counted qty x100) is required");
            if (counted < 0) throw Invalid(r, "counted qty must be >= 0");
            // Code "1" and the category keys carry no stock (PR-K4, review L3).
            if (SalesService.IsNonStockCode(product.ProductCode)) return null;

            DateTime countAt = Wib(r.HappenedAt);
            // PR-K6: the shelf was seen at countAt; movements written after it (sales,
            // receipts) happened on top of the counted qty.
            var since = _movements.GetMovementsSince(product.ProductCode, countAt);
            int systemAtCount = _inventory.GetStockOnHand(product.ProductCode) - (since.QtyIn - since.QtyOut);
            int variance = counted - systemAtCount;
            if (variance == 0) return null;

            // OB-14: always the local average; the request's unit_cost is ignored.
            long avgCost = _inventory.CalculateAverageCost(product.ProductCode);
            string docDate = DocDate(countAt);
            string journalNo = string.IsNullOrWhiteSpace(r.DocNo)
                ? "OPN-DB-" + countAt.ToString("yyMMdd", CultureInfo.InvariantCulture)
                : r.DocNo.Trim();
            int qty = Math.Abs(variance);
            long value = StockQty.Value(avgCost, qty);

            EnsureAdjustmentHeader(r, journalNo, docDate);
            // Like StockOpnameService: the adjustment line keeps the ledger qty (x100).
            SqlHelper.ExecuteNonQuery(_db,
                @"INSERT INTO stock_adjustment_items (journal_no, product_code, quantity, unit_price, value, remark)
                  VALUES (@jnl, @product, @qty, @cost, @val, @reason)",
                SqlHelper.Param("@jnl", journalNo),
                SqlHelper.Param("@product", product.ProductCode),
                SqlHelper.Param("@qty", qty),
                SqlHelper.Param("@cost", avgCost),
                SqlHelper.Param("@val", value),
                SqlHelper.Param("@reason", variance > 0 ? "SURPLUS" : "SHORTAGE"));
            // Touching the header re-queues it for LAN sync (trg_stock_adjustments_sync_u).
            SqlHelper.ExecuteNonQuery(_db,
                @"UPDATE stock_adjustments
                  SET total_value = (SELECT COALESCE(SUM(value), 0) FROM stock_adjustment_items WHERE journal_no = @jnl),
                      changed_at = datetime('now','localtime')
                  WHERE journal_no = @jnl",
                SqlHelper.Param("@jnl", journalNo));

            var placement = NextPlacement(countAt);
            if (variance > 0)
                _inventory.RecordStockIn(product.ProductCode, qty, avgCost, "OPNAME", journalNo, docDate, PullUserId, placement);
            else
                _inventory.RecordStockOut(product.ProductCode, qty, avgCost, "OPNAME", journalNo, docDate, PullUserId, placement);
            return journalNo;
        }

        private void EnsureAdjustmentHeader(PosStockRequest r, string journalNo, string docDate)
        {
            string existingType = SqlHelper.ExecuteScalar<string>(_db,
                "SELECT doc_type FROM stock_adjustments WHERE journal_no = @jnl",
                SqlHelper.Param("@jnl", journalNo));
            if (existingType != null)
            {
                if (existingType != "OPNAME")
                    throw Invalid(r, "local document " + journalNo + " exists as " + existingType + ", not OPNAME");
                return;
            }
            SqlHelper.ExecuteNonQuery(_db,
                @"INSERT INTO stock_adjustments (doc_type, journal_no, doc_date, location_code, remark,
                  total_value, control, period_code, register_id, legacy_source, changed_by, changed_at)
                  VALUES ('OPNAME', @jnl, @date, @loc, @remark, 0, 1, @period, @reg, 'DASHBOARD', @by,
                  datetime('now','localtime'))",
                SqlHelper.Param("@jnl", journalNo),
                SqlHelper.Param("@date", docDate),
                SqlHelper.Param("@loc", InventoryService.DefaultLocationCode),
                SqlHelper.Param("@remark", "Opname dashboard"),
                SqlHelper.Param("@period", Period(docDate)),
                SqlHelper.Param("@reg", RegisterId()),
                SqlHelper.Param("@by", PullUserId));
        }

        // ---------- PURCHASE / RETURN_OUT ----------

        private string ApplyPurchase(PosStockRequest r)
        {
            var product = RequireProduct(r);
            int qty = RequirePositiveQty(r);
            long unitCost = r.UnitCost ?? throw Invalid(r, "unit_cost (x100 per stock unit) is required");
            if (unitCost < 0) throw Invalid(r, "unit_cost must be >= 0");
            string journalNo = RequireDocNo(r);
            DateTime at = Wib(r.HappenedAt);
            string docDate = DocDate(at);
            var payload = Payload(r);

            EnsurePurchaseHeader(r, journalNo, "RECEIPT", docDate, null);
            AddPurchaseLine(journalNo, product.ProductCode, qty, unitCost, Str(payload, "po_no"));

            // PR-K1 moving average runs inside RecordStockIn (when the cost engine owns cost_price).
            _inventory.RecordStockIn(product.ProductCode, qty, unitCost, "PURCHASE", journalNo, docDate,
                PullUserId, NextPlacement(at));
            return journalNo;
        }

        private string ApplyReturnOut(PosStockRequest r)
        {
            var product = RequireProduct(r);
            int qty = RequirePositiveQty(r);
            long unitCost = r.UnitCost ?? _inventory.CalculateAverageCost(product.ProductCode);
            if (unitCost < 0) throw Invalid(r, "unit_cost must be >= 0");
            string journalNo = RequireDocNo(r);
            DateTime at = Wib(r.HappenedAt);
            string docDate = DocDate(at);
            var payload = Payload(r);

            EnsurePurchaseHeader(r, journalNo, "PURCHASE_RETURN", docDate, Str(payload, "ref_no"));
            AddPurchaseLine(journalNo, product.ProductCode, qty, unitCost, Str(payload, "ref_no"));

            // A return never changes the average (RecordStockOut), as in PurchasingService.
            _inventory.RecordStockOut(product.ProductCode, qty, unitCost, "RETURN_OUT", journalNo, docDate,
                PullUserId, NextPlacement(at));
            return journalNo;
        }

        // One local purchases header per dashboard document; its lines arrive as
        // separate requests and are appended.
        private void EnsurePurchaseHeader(PosStockRequest r, string journalNo, string docType, string docDate, string refNo)
        {
            string vendor = (r.VendorCode ?? "").Trim();
            using (var cmd = _db.CreateCommand())
            {
                cmd.CommandText = "SELECT doc_type, sub_code FROM purchases WHERE journal_no = @jnl";
                cmd.Parameters.Add(SqlHelper.Param("@jnl", journalNo));
                using (var reader = cmd.ExecuteReader())
                {
                    if (reader.Read())
                    {
                        string type = reader.GetString(0);
                        string sub = reader.IsDBNull(1) ? "" : reader.GetString(1);
                        if (type != docType || sub != vendor)
                            throw Invalid(r, "local document " + journalNo + " exists as " + type + "/" + sub
                                + ", not " + docType + "/" + vendor);
                        return;
                    }
                }
            }

            new PurchaseRepository(_db).InsertWithoutTransaction(new Purchase
            {
                DocType = docType,
                JournalNo = journalNo,
                DocDate = docDate,
                SubCode = vendor,
                RefNo = refNo ?? "",
                Remark = "Dashboard",
                ReceivedDate = docType == "RECEIPT" ? docDate : "",
                Control = 1,
                PeriodCode = Period(docDate),
                RegisterId = RegisterId(),
                ChangedBy = PullUserId
            }, new System.Collections.Generic.List<PurchaseItem>());
            SqlHelper.ExecuteNonQuery(_db,
                "UPDATE purchases SET legacy_source = 'DASHBOARD' WHERE journal_no = @jnl",
                SqlHelper.Param("@jnl", journalNo));
        }

        // purchase_items.quantity is plain units (documents keep the POS's unit count);
        // the ledger qty stays exact on the movement. A fractional qty (e.g. 0.5 galon =
        // 50) is rounded on the document only.
        private void AddPurchaseLine(string journalNo, string productCode, int ledgerQty, long unitCost, string orderRef)
        {
            int units = (int)Math.Round((decimal)ledgerQty / StockQty.Scale, MidpointRounding.AwayFromZero);
            long value = StockQty.Value(unitCost, ledgerQty);
            SqlHelper.ExecuteNonQuery(_db,
                @"INSERT INTO purchase_items (journal_no, order_ref, product_code, remark, quantity, value, unit_price)
                  VALUES (@jnl, @ref, @product, @remark, @qty, @val, @price)",
                SqlHelper.Param("@jnl", journalNo),
                SqlHelper.Param("@ref", orderRef ?? ""),
                SqlHelper.Param("@product", productCode),
                SqlHelper.Param("@remark", ledgerQty % StockQty.Scale == 0 ? "" : "qty " + StockQty.Format(ledgerQty)),
                SqlHelper.Param("@qty", units),
                SqlHelper.Param("@val", value),
                SqlHelper.Param("@price", unitCost));
            // Touching the header re-queues it for LAN sync (trg_purchases_sync_u).
            SqlHelper.ExecuteNonQuery(_db,
                @"UPDATE purchases
                  SET gross_amount = (SELECT COALESCE(SUM(value), 0) FROM purchase_items WHERE journal_no = @jnl),
                      total_value  = (SELECT COALESCE(SUM(value), 0) FROM purchase_items WHERE journal_no = @jnl),
                      changed_at = datetime('now','localtime')
                  WHERE journal_no = @jnl",
                SqlHelper.Param("@jnl", journalNo));
        }

        // ---------- VENDOR_BILL ----------

        private string ApplyVendorBill(PosStockRequest r)
        {
            string journalNo = RequireDocNo(r);
            string vendor = (r.VendorCode ?? "").Trim();
            if (vendor.Length == 0) throw Invalid(r, "vendor_code is required");
            var payload = Payload(r);
            long amount = Long(payload, "amount") ?? throw Invalid(r, "payload.amount (x100) is required");
            long gross = Long(payload, "gross_amount") ?? amount;
            long disc = Long(payload, "disc_amount") ?? 0;
            string billDate = Date(r, payload, "bill_date") ?? DocDate(Wib(r.HappenedAt));
            string dueDate = Date(r, payload, "due_date");

            // payables_register has no unique key; the same bill already there (e.g. a
            // restored DB whose applied_requests is older) is not entered twice.
            long existing = SqlHelper.ExecuteScalar<long>(_db,
                "SELECT COUNT(*) FROM payables_register WHERE journal_no = @jnl AND sub_code = @sub",
                SqlHelper.Param("@jnl", journalNo), SqlHelper.Param("@sub", vendor));
            if (existing > 0) return journalNo;

            int id = _payables.Insert(new PayablesEntry
            {
                SubCode = vendor,
                JournalNo = journalNo,
                DocDate = billDate,
                DueDate = dueDate,
                Direction = "D",
                GrossAmount = gross,
                Amount = amount,
                PaymentAmount = 0,
                Control = 1,
                PeriodCode = Period(billDate),
                ChangedBy = PullUserId
            });
            SqlHelper.ExecuteNonQuery(_db,
                "UPDATE payables_register SET ref = @ref, remark = @remark, disc_amount = @disc WHERE id = @id",
                SqlHelper.Param("@ref", Truncate(Str(payload, "vendor_invoice_no") ?? "", 15)),
                SqlHelper.Param("@remark", "Tagihan dashboard"),
                SqlHelper.Param("@disc", disc),
                SqlHelper.Param("@id", id));
            return journalNo;
        }

        // ---------- PRODUCT_STATUS / NEW_PRODUCT ----------

        private string ApplyProductStatus(PosStockRequest r)
        {
            var product = RequireProduct(r);
            string status = (Str(Payload(r), "status") ?? "A").Trim().ToUpperInvariant();
            if (status != "A" && status != "I" && status != "D")
                throw Invalid(r, "payload.status must be A, I or D");
            if (product.Status == status) return null;
            SqlHelper.ExecuteNonQuery(_db,
                @"UPDATE products SET status = @status, changed_by = @by, changed_at = datetime('now','localtime')
                  WHERE product_code = @code",
                SqlHelper.Param("@status", status),
                SqlHelper.Param("@by", PullUserId),
                SqlHelper.Param("@code", product.ProductCode));
            return null;
        }

        private string ApplyNewProduct(PosStockRequest r)
        {
            string code = (r.ProductCode ?? "").Trim();
            if (code.Length == 0) throw Invalid(r, "product_code is required");
            if (_products.GetByCode(code) != null) return null; // already here: leave it as it is

            var payload = Payload(r);
            string name = (Str(payload, "name") ?? "").Trim().ToUpperInvariant();
            if (name.Length == 0) throw Invalid(r, "payload.name is required");
            if (name.Length > MaxNameLength) name = name.Substring(0, MaxNameLength);
            string status = (Str(payload, "status") ?? "A").Trim().ToUpperInvariant();
            if (status != "A" && status != "I") throw Invalid(r, "payload.status must be A or I");
            long cost = Long(payload, "cost_price") ?? 0;

            _products.Insert(new Product
            {
                ProductCode = code,
                Name = name,
                DeptCode = Str(payload, "dept_code"),
                Status = status,
                Unit = Str(payload, "unit") ?? "PCS",
                Price = Long(payload, "price") ?? 0,
                BuyingPrice = Long(payload, "buying_price") ?? cost,
                CostPrice = cost,
                VendorCode = Str(payload, "vendor_code") ?? r.VendorCode,
                OpenPrice = "N",
                VatFlag = "N",
                LuxuryTaxFlag = "N",
                IsConsignment = "N",
                ChangedBy = PullUserId
            });
            return null;
        }

        // ---------- helpers ----------

        // Next id in the dashboard range: the stored sequence, but never at or below an
        // id already in the table (a restored or hand-edited DB cannot cause a clash).
        internal long NextMovementId()
        {
            long seq = long.TryParse(_config.Get(MovementIdSeqKey), NumberStyles.Integer, CultureInfo.InvariantCulture, out var v)
                ? v : StockMovementRepository.DashboardIdFloor;
            long maxUsed = SqlHelper.ExecuteScalar<long>(_db,
                "SELECT COALESCE(MAX(id), 0) FROM stock_movements WHERE id >= @floor",
                SqlHelper.Param("@floor", StockMovementRepository.DashboardIdFloor));
            long next = Math.Max(Math.Max(seq, StockMovementRepository.DashboardIdFloor), maxUsed + 1);
            _config.Set(MovementIdSeqKey, (next + 1).ToString(CultureInfo.InvariantCulture));
            return next;
        }

        private MovementPlacement NextPlacement(DateTime wibAt)
        {
            return new MovementPlacement
            {
                Id = NextMovementId(),
                MovedAt = wibAt.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture)
            };
        }

        private Product RequireProduct(PosStockRequest r)
        {
            string code = (r.ProductCode ?? "").Trim();
            if (code.Length == 0) throw Invalid(r, "product_code is required");
            return _products.GetByCode(code)
                ?? throw new PosRequestApplyException(Describe(r) + ": product " + code + " not in kasir.db yet", deferred: true);
        }

        private static int RequirePositiveQty(PosStockRequest r)
        {
            int qty = r.Qty ?? throw Invalid(r, "qty (x100) is required");
            if (qty <= 0) throw Invalid(r, "qty must be > 0");
            return qty;
        }

        private static string RequireDocNo(PosStockRequest r)
        {
            string doc = (r.DocNo ?? "").Trim();
            if (doc.Length == 0) throw Invalid(r, "doc_no is required");
            return doc;
        }

        private string RegisterId() => _config.Get("register_id") ?? "01";

        // Register wall clock (WIB) of an instant; the POS writes local time everywhere.
        internal static DateTime Wib(DateTimeOffset at) =>
            DateTime.SpecifyKind(at.ToOffset(DateParser.Wib).DateTime, DateTimeKind.Unspecified);

        private static string DocDate(DateTime wib) => wib.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

        private static string Period(string docDate) => docDate.Substring(0, 4) + docDate.Substring(5, 2);

        private static string Truncate(string s, int max) => s.Length <= max ? s : s.Substring(0, max);

        private static JObject Payload(PosStockRequest r)
        {
            if (string.IsNullOrWhiteSpace(r.PayloadJson)) return new JObject();
            try
            {
                return JObject.Parse(r.PayloadJson);
            }
            catch (Newtonsoft.Json.JsonException ex)
            {
                throw Invalid(r, "payload is not a JSON object: " + ex.Message);
            }
        }

        private static string Str(JObject o, string name)
        {
            var t = o[name];
            if (t == null || t.Type == JTokenType.Null) return null;
            string s = t.ToString().Trim();
            return s.Length == 0 ? null : s;
        }

        private static long? Long(JObject o, string name)
        {
            var t = o[name];
            if (t == null || t.Type == JTokenType.Null) return null;
            if (t.Type == JTokenType.Integer) return t.Value<long>();
            if (long.TryParse(t.ToString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var v)) return v;
            throw new FormatException("payload." + name + " must be an integer (x100)");
        }

        private static string Date(PosStockRequest r, JObject o, string name)
        {
            string s = Str(o, name);
            if (s == null) return null;
            if (s.Length >= 10 && DateTime.TryParseExact(s.Substring(0, 10), "yyyy-MM-dd",
                    CultureInfo.InvariantCulture, DateTimeStyles.None, out _))
                return s.Substring(0, 10);
            throw Invalid(r, "payload." + name + " must be YYYY-MM-DD");
        }

        private static string Describe(PosStockRequest r) => r.RequestKind + " " + r.IdempotencyKey;

        private static PosRequestApplyException Invalid(PosStockRequest r, string why) =>
            new PosRequestApplyException(Describe(r) + ": " + why, deferred: false);
    }
}
