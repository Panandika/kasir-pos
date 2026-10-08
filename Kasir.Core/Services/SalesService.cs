using System;
using System.Collections.Generic;
using Microsoft.Data.Sqlite;
using Kasir.Data.Repositories;
using Kasir.Models;
using Kasir.Utils;

namespace Kasir.Services
{
    public class SalesService
    {
        private readonly SqliteConnection _db;
        private readonly ProductRepository _productRepo;
        private readonly SaleRepository _saleRepo;
        private readonly CounterRepository _counterRepo;
        private readonly ConfigRepository _configRepo;
        private readonly ShiftRepository _shiftRepo;
        private readonly PricingEngine _pricingEngine;
        private readonly DiscountEngine _discountEngine;
        private readonly DiscountRepository _discountRepo;
        private readonly PaymentCalculator _paymentCalc;
        private readonly InventoryService _inventoryService;
        private readonly IClock _clock;

        private readonly List<SaleItem> _currentItems;
        private readonly PendingSaleRepository _pendingRepo;
        private readonly string _draftKey;
        private string _currentShift;
        private string _cashierAlias;
        private int _cashierUserId;

        public SalesService(SqliteConnection db, IClock clock)
        {
            _db = db;
            _productRepo = new ProductRepository(db);
            _saleRepo = new SaleRepository(db);
            _counterRepo = new CounterRepository(db);
            _configRepo = new ConfigRepository(db);
            _shiftRepo = new ShiftRepository(db);
            _pricingEngine = new PricingEngine();
            _discountEngine = new DiscountEngine();
            _discountRepo = new DiscountRepository(db);
            _paymentCalc = new PaymentCalculator();
            _inventoryService = new InventoryService(db);
            _pendingRepo = new PendingSaleRepository(db);
            _clock = clock;
            _currentItems = new List<SaleItem>();
            _currentShift = "1";
            _draftKey = "PENDING-" + (_configRepo.Get("register_id") ?? "01");
        }

        // Persist the current cart so a crash mid-sale can recover it (F36).
        private void PersistCart()
        {
            _pendingRepo.Save(_draftKey, _currentItems);
        }

        // Append a line and persist the cart. If the save fails (e.g. "database is locked")
        // the line is taken back out before rethrowing, so the in-memory cart never holds an
        // item the screen did not show but CompleteSale would still charge (F08).
        private void AddAndPersist(SaleItem item)
        {
            _currentItems.Add(item);
            try
            {
                PersistCart();
            }
            catch
            {
                _currentItems.RemoveAt(_currentItems.Count - 1);
                throw;
            }
        }

        // Recover a cart persisted by a previous (crashed) session. Product names are
        // re-looked-up since pending_sales stores only product_code. Returns the item count.
        public int RecoverPendingSale()
        {
            var recovered = _pendingRepo.Load(_draftKey);
            _currentItems.Clear();
            foreach (var it in recovered)
            {
                var product = _productRepo.GetByCode(it.ProductCode);
                it.ProductName = product != null ? product.Name : it.ProductCode;
                _currentItems.Add(it);
            }
            return _currentItems.Count;
        }

        public List<SaleItem> CurrentItems
        {
            get { return _currentItems; }
        }

        public void SetCashier(string alias, int userId)
        {
            _cashierAlias = alias;
            _cashierUserId = userId;
        }

        public void SetShift(string shift)
        {
            _currentShift = shift;
        }

        public SaleItem AddItem(string productCode, int qty)
        {
            return AddItem(productCode, qty, 0);
        }

        // Reserved product code for "Barang Tanpa Kode" — items without a catalog entry
        // but with a known price. Posts to department 100 (DLL) via the seeded product row.
        public const string MiscProductCode = "1";
        public const string MiscProductName = "Barang Tanpa Kode";

        // Category quick-key codes (ALAT LISTRIK, ALAT TULIS, PERABOT, PLASTIK, MAINAN,
        // LAIN-LAIN). Like code "1" they are non-stock: sold by price, never tracked in
        // stock_movements.
        public static readonly IReadOnlySet<string> CategoryKeyCodes =
            new HashSet<string>(StringComparer.Ordinal) { "AL", "AT", "PR", "PL", "MY", "LL" };

        // Non-stock lines are derived from the product code, not a cart flag, because
        // pending_sales only persists product_code: a crash-recovered cart must still be
        // recognised as non-stock.
        public static bool IsNonStockCode(string productCode)
        {
            return productCode == MiscProductCode || CategoryKeyCodes.Contains(productCode ?? "");
        }

        // Category quick keys in picker order (number 1-6 on the sale screen). Seeded as
        // open-price products by Migration_013.
        public static readonly IReadOnlyList<(string Code, string Name)> CategoryKeys = new[]
        {
            ("AL", "ALAT LISTRIK"), ("AT", "ALAT TULIS"), ("PR", "PERABOT"),
            ("PL", "PLASTIK"), ("MY", "MAINAN"), ("LL", "LAIN-LAIN"),
        };

        // Margin assumed when a category row has no margin_pct: 25.00% (x100 scale).
        public const int DefaultCategoryMarginPct = 2500;

        // The category code a cashier typed ("al", " LL "), or null when it is not one.
        public static string ResolveCategoryKey(string typed)
        {
            string code = (typed ?? "").Trim().ToUpperInvariant();
            return CategoryKeyCodes.Contains(code) ? code : null;
        }

        public SaleItem AddMiscItem(int qty, long unitPrice)
        {
            return AddMiscItem(qty, unitPrice, null);
        }

        // categoryCode null = plain code "1" (COGS 0). A category line carries an estimated
        // COGS = price x (1 - margin), margin from the category's products.margin_pct.
        public SaleItem AddMiscItem(int qty, long unitPrice, string categoryCode)
        {
            if (qty <= 0) throw new ArgumentException("Qty harus > 0", nameof(qty));
            if (unitPrice <= 0) throw new ArgumentException("Harga harus > 0", nameof(unitPrice));

            string code = MiscProductCode;
            string name = MiscProductName;
            long cogs = 0;
            if (categoryCode != null)
            {
                code = ResolveCategoryKey(categoryCode)
                    ?? throw new ArgumentException("Kategori tidak dikenal: " + categoryCode, nameof(categoryCode));
                var category = _productRepo.GetByCode(code);
                int marginPct = category != null && category.MarginPct > 0 ? category.MarginPct : DefaultCategoryMarginPct;
                name = category?.Name ?? code;
                cogs = unitPrice * (10000 - marginPct) / 10000 * qty;
            }

            var item = new SaleItem
            {
                ProductCode = code,
                ProductName = name,
                Quantity = qty,
                UnitPrice = unitPrice,
                Value = unitPrice * qty,
                Cogs = cogs,
                DiscPct = 0,
                DiscValue = 0,
                PointValue = 0,
                IsPriceOverridden = true,
            };
            AddAndPersist(item);
            return item;
        }

        public SaleItem AddItem(string productCode, int qty, int overridePrice)
        {
            // Look up product strictly by product_code
            Product product = _productRepo.GetByCode(productCode);
            if (product == null)
            {
                return null; // Product not found
            }

            int effectiveQty = qty;

            // Resolve price
            long unitPrice = _pricingEngine.GetUnitPrice(
                product,
                effectiveQty,
                overridePrice: overridePrice);

            // Resolve discount
            string saleDateIso = _clock.Now.ToString("yyyy-MM-dd");
            string saleTimeHms = _clock.Now.ToString("HH:mm:ss");
            var activeDiscounts = _discountRepo.GetActiveForProduct(
                product.ProductCode, product.DeptCode ?? "", saleDateIso, saleTimeHms);

            var discountResult = _discountEngine.ResolveDiscount(
                product,
                saleDateIso,
                activeDiscounts,
                partnerDiscPct: 0,
                accountDiscPct: 0,
                accountDiscDateStart: null,
                accountDiscDateEnd: null,
                saleTimeHms: saleTimeHms,
                qty: effectiveQty);

            // Calculate line total
            long lineGross = unitPrice * effectiveQty;
            long lineDiscount = discountResult.CalculateDiscount(lineGross);
            long lineNet = lineGross - lineDiscount;

            var item = new SaleItem
            {
                ProductCode = product.ProductCode,
                ProductName = product.Name,
                Quantity = effectiveQty,
                UnitPrice = unitPrice,
                DiscPct = discountResult.DiscPct,
                DiscValue = lineDiscount,
                Value = lineNet,
                Cogs = product.CostPrice * effectiveQty,
                IsPriceOverridden = overridePrice > 0
            };

            AddAndPersist(item);
            return item;
        }

        public void RemoveItem(int index)
        {
            if (index >= 0 && index < _currentItems.Count)
            {
                _currentItems.RemoveAt(index);
                PersistCart();
            }
        }

        public void UpdateItemQty(int index, int newQty)
        {
            if (index < 0 || index >= _currentItems.Count) return;

            var item = _currentItems[index];
            int oldQty = item.Quantity;
            item.Quantity = newQty;

            // Non-stock lines (code "1", category keys) keep their typed price and scale
            // their COGS estimate; the product row has no real price or cost to re-resolve.
            if (IsNonStockCode(item.ProductCode))
            {
                long unitCogs = oldQty != 0 ? item.Cogs / oldQty : 0;
                item.DiscPct = 0;
                item.DiscValue = 0;
                item.Value = item.UnitPrice * newQty;
                item.Cogs = unitCogs * newQty;
                PersistCart();
                return;
            }

            var product = _productRepo.GetByCode(item.ProductCode);
            if (product != null)
            {
                // Re-resolve price (unless manually overridden)
                if (!item.IsPriceOverridden)
                {
                    item.UnitPrice = _pricingEngine.GetUnitPrice(product, newQty);
                }

                // Re-resolve discount
                string dateIso = _clock.Now.ToString("yyyy-MM-dd");
                string timeHms = _clock.Now.ToString("HH:mm:ss");
                var discounts = _discountRepo.GetActiveForProduct(
                    product.ProductCode, product.DeptCode ?? "", dateIso, timeHms);
                var discResult = _discountEngine.ResolveDiscount(
                    product, dateIso, discounts,
                    partnerDiscPct: 0,
                    accountDiscPct: 0,
                    accountDiscDateStart: null,
                    accountDiscDateEnd: null,
                    saleTimeHms: timeHms,
                    qty: newQty);

                long lineGross = item.UnitPrice * newQty;
                long lineDiscount = discResult.CalculateDiscount(lineGross);
                item.DiscPct = discResult.DiscPct;
                item.DiscValue = lineDiscount;
                item.Value = lineGross - lineDiscount;
                item.Cogs = product.CostPrice * newQty;
            }
            else
            {
                // Fallback: recalc with existing discount
                long lineGross = item.UnitPrice * newQty;
                var discResult = new DiscountResult { DiscPct = item.DiscPct };
                item.DiscValue = discResult.CalculateDiscount(lineGross);
                item.Value = lineGross - item.DiscValue;
            }

            PersistCart();
        }

        public SaleTotals GetTotals()
        {
            long gross = 0;
            long discount = 0;
            int itemCount = 0;

            foreach (var item in _currentItems)
            {
                gross += item.UnitPrice * item.Quantity;
                discount += item.DiscValue;
                itemCount += item.Quantity;
            }

            long net = gross - discount;

            return new SaleTotals
            {
                GrossAmount = gross,
                TotalDiscount = discount,
                NetAmount = net,
                ItemCount = itemCount,
                LineCount = _currentItems.Count
            };
        }

        public Sale CompleteSale(long cashAmount, long cardAmount, long voucherAmount,
            string cardCode, string cardType, string memberCode)
        {
            if (_currentItems.Count == 0)
            {
                throw new InvalidOperationException("Cannot complete sale with no items");
            }

            var totals = GetTotals();

            var validation = _paymentCalc.ValidatePayment(
                totals.NetAmount, cashAmount, cardAmount, voucherAmount);

            if (!validation.IsValid)
            {
                throw new InvalidOperationException(
                    string.Format("Insufficient payment. Due: {0}, Paid: {1}",
                        totals.NetAmount, cashAmount + cardAmount + voucherAmount));
            }

            // A card tender must name its card (legacy JUAL1: "Jenis card harus di-isi");
            // the card type decides the GL clearing account. A card picked with no card
            // amount is a cash sale and is stored without card data (#19).
            if (cardAmount > 0 && string.IsNullOrWhiteSpace(cardCode))
            {
                throw new InvalidOperationException("Jenis kartu harus dipilih untuk pembayaran kartu.");
            }
            if (cardAmount == 0)
            {
                cardCode = "";
                cardType = "";
            }

            string registerId = _configRepo.Get("register_id") ?? "01";
            // Resolve active shift; fall back to _currentShift only when no shift
            // has been opened (e.g. tests). Real prod path always opens a shift first.
            var openShift = _shiftRepo.GetOpenShift(registerId);
            string activeShift = openShift != null ? openShift.ShiftNumber : (_currentShift ?? "1");
            // Journal number is allocated INSIDE the sale transaction below so a rolled-back
            // or crashed sale does not burn a KLR number (F52). GetNext joins the ambient
            // transaction, so its counter increment rolls back with the sale.
            string journalNo = null;
            string today = _clock.Now.ToString("yyyy-MM-dd");
            string period = _clock.Now.ToString("yyyyMM");

            // Stickers are for members only (legacy JUAL1 a_total: no member card -> 0).
            int loyaltyPoints = string.IsNullOrWhiteSpace(memberCode)
                ? 0
                : _paymentCalc.CalculateLoyaltyPoints(totals.NetAmount);

            var sale = new Sale
            {
                DocType = "SALE",
                JournalNo = journalNo,
                DocDate = today,
                MemberCode = memberCode ?? "",
                PointValue = loyaltyPoints,
                CardCode = cardCode ?? "",
                CardType = cardType ?? "",
                Cashier = _cashierAlias ?? "",
                Shift = activeShift,
                PaymentAmount = cashAmount + cardAmount + voucherAmount,
                CashAmount = cashAmount,
                NonCash = cardAmount,
                TotalValue = totals.NetAmount,
                ChangeAmount = validation.Change,
                TotalDisc = totals.TotalDiscount,
                GrossAmount = totals.GrossAmount,
                VoucherAmount = voucherAmount,
                CreditAmount = 0,
                Control = 1,
                PeriodCode = period,
                RegisterId = registerId,
                ChangedBy = _cashierUserId
            };

            using (var txn = _db.BeginTransaction())
            {
                try
                {
                    // Journal number is allocated inside the sale transaction so a
                    // rolled-back sale does not burn a KLR number (F52).
                    journalNo = _counterRepo.GetNext("KLR", registerId);
                    sale.JournalNo = journalNo;

                    // COGS uses the perpetual moving-average cost (products.cost_price, kept
                    // current by every stock-in) so the GL COGS matches the inventory ledger
                    // (F20/F40). Capture the per-unit cost once and use it for BOTH the
                    // stored line COGS and the stock-out movement.
                    var unitCosts = new List<long>(_currentItems.Count);
                    foreach (var item in _currentItems)
                    {
                        // Non-stock lines keep the COGS set when they were added (0 or an
                        // estimate); they have no stock ledger to average.
                        if (IsNonStockCode(item.ProductCode))
                        {
                            unitCosts.Add(0);
                            continue;
                        }
                        long avgCost = _inventoryService.CalculateAverageCost(item.ProductCode);
                        unitCosts.Add(avgCost);
                        item.Cogs = avgCost * item.Quantity;
                    }

                    _saleRepo.InsertWithoutTransaction(sale, _currentItems);

                    // Create stock movements at the same weighted-average cost.
                    for (int i = 0; i < _currentItems.Count; i++)
                    {
                        var item = _currentItems[i];
                        if (IsNonStockCode(item.ProductCode)) continue;
                        _inventoryService.RecordStockOut(
                            item.ProductCode,
                            item.Quantity,
                            unitCosts[i],
                            "SALE",
                            journalNo,
                            today,
                            _cashierUserId);
                    }

                    // Clear the persisted draft cart atomically with the sale so a crash
                    // right after commit does not recover an already-completed cart (F36).
                    _pendingRepo.Clear(_draftKey);

                    txn.Commit();
                }
                catch
                {
                    txn.Rollback();
                    throw;
                }
            }

            return sale;
        }

        public void VoidSale(string journalNo)
        {
            var sale = _saleRepo.GetByJournalNo(journalNo);
            if (sale == null)
            {
                throw new InvalidOperationException("Penjualan tidak ditemukan: " + journalNo);
            }
            if (sale.Control == 3)
            {
                return; // already voided — idempotent
            }

            // A sale whose GL journal was already posted must be reversed with a proper
            // return/credit note, not silently voided — otherwise the GL and the sale
            // diverge (F13). Block it here.
            if (sale.IsPosted == "Y")
            {
                throw new InvalidOperationException(
                    "Tidak bisa void penjualan yang sudah diposting ke jurnal; buat retur penjualan.");
            }

            var items = _saleRepo.GetItemsByJournalNo(journalNo);

            using (var txn = _db.BeginTransaction())
            {
                try
                {
                    _saleRepo.VoidSale(journalNo, _cashierUserId);

                    // Return the sold stock to inventory — a plain control=3 flip left the
                    // stock permanently understated (F35).
                    foreach (var item in items)
                    {
                        if (IsNonStockCode(item.ProductCode)) continue; // never stocked out
                        // item.Cogs is the line total (unit cost × qty); the movement needs the unit cost.
                        long unitCost = item.Quantity != 0 ? item.Cogs / item.Quantity : 0;
                        _inventoryService.RecordStockIn(
                            item.ProductCode, item.Quantity, unitCost,
                            "RETURN_IN", journalNo, sale.DocDate, _cashierUserId);
                    }

                    txn.Commit();
                }
                catch { txn.Rollback(); throw; }
            }
        }

        public void ClearCurrentSale()
        {
            _currentItems.Clear();
            PersistCart(); // also clears the persisted draft (F36)
        }
    }

    public class SaleTotals
    {
        public long GrossAmount { get; set; }
        public long TotalDiscount { get; set; }
        public long NetAmount { get; set; }
        public int ItemCount { get; set; }
        public int LineCount { get; set; }
    }
}
