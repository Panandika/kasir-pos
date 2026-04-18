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
        private readonly ProductBarcodeRepository _barcodeRepo;
        private readonly SaleRepository _saleRepo;
        private readonly CounterRepository _counterRepo;
        private readonly ConfigRepository _configRepo;
        private readonly PricingEngine _pricingEngine;
        private readonly DiscountEngine _discountEngine;
        private readonly DiscountRepository _discountRepo;
        private readonly PaymentCalculator _paymentCalc;
        private readonly InventoryService _inventoryService;
        private readonly IClock _clock;

        private readonly List<SaleItem> _currentItems;
        private string _currentShift;
        private string _cashierAlias;
        private int _cashierUserId;

        public SalesService(SqliteConnection db, IClock clock)
        {
            _db = db;
            _productRepo = new ProductRepository(db);
            _barcodeRepo = new ProductBarcodeRepository(db);
            _saleRepo = new SaleRepository(db);
            _counterRepo = new CounterRepository(db);
            _configRepo = new ConfigRepository(db);
            _pricingEngine = new PricingEngine();
            _discountEngine = new DiscountEngine();
            _discountRepo = new DiscountRepository(db);
            _paymentCalc = new PaymentCalculator();
            _inventoryService = new InventoryService(db);
            _clock = clock;
            _currentItems = new List<SaleItem>();
            _currentShift = "1";
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

        public SaleItem AddItem(string codeOrBarcode, int qty)
        {
            return AddItem(codeOrBarcode, qty, 0);
        }

        // Reserved product code for "Barang Tanpa Kode" — items without a catalog entry
        // but with a known price. Posts to department 100 (DLL) via the seeded product row.
        public const string MiscProductCode = "1";
        public const string MiscProductName = "Barang Tanpa Kode";

        public SaleItem AddMiscItem(int qty, long unitPrice)
        {
            if (qty <= 0) throw new ArgumentException("Qty harus > 0", nameof(qty));
            if (unitPrice <= 0) throw new ArgumentException("Harga harus > 0", nameof(unitPrice));

            var item = new SaleItem
            {
                ProductCode = MiscProductCode,
                ProductName = MiscProductName,
                Quantity = qty,
                UnitPrice = unitPrice,
                Value = unitPrice * qty,
                Cogs = 0,
                DiscPct = 0,
                DiscValue = 0,
                PointValue = 0,
                IsPriceOverridden = true,
            };
            _currentItems.Add(item);
            return item;
        }

        public SaleItem AddItem(string codeOrBarcode, int qty, int overridePrice)
        {
            // Look up product: try barcode table first, then product code/barcode
            long barcodeOverridePrice = 0;
            int barcodeQty = 0;
            Product product = null;

            var barcodeEntry = _barcodeRepo.GetByBarcode(codeOrBarcode);
            if (barcodeEntry != null)
            {
                product = _productRepo.GetByCode(barcodeEntry.ProductCode);
                barcodeOverridePrice = barcodeEntry.PriceOverride;
                barcodeQty = barcodeEntry.QtyPerScan;
            }

            if (product == null)
            {
                product = _productRepo.GetByCode(codeOrBarcode);
            }

            if (product == null)
            {
                product = _productRepo.GetByBarcode(codeOrBarcode);
            }

            if (product == null)
            {
                return null; // Product not found
            }

            // Use barcode qty if specified, otherwise use the passed qty
            int effectiveQty = barcodeQty > 0 ? barcodeQty : qty;

            // Resolve price
            long unitPrice = _pricingEngine.GetUnitPrice(
                product,
                effectiveQty,
                overridePrice: overridePrice,
                barcodeOverride: barcodeOverridePrice);

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
                IsPriceOverridden = overridePrice > 0 || barcodeOverridePrice > 0
            };

            _currentItems.Add(item);
            return item;
        }

        public void RemoveItem(int index)
        {
            if (index >= 0 && index < _currentItems.Count)
            {
                _currentItems.RemoveAt(index);
            }
        }

        public void UpdateItemQty(int index, int newQty)
        {
            if (index < 0 || index >= _currentItems.Count) return;

            var item = _currentItems[index];
            item.Quantity = newQty;

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

            string registerId = _configRepo.Get("register_id") ?? "01";
            string journalNo = _counterRepo.GetNext("KLR", registerId);
            string today = _clock.Now.ToString("yyyy-MM-dd");
            string period = _clock.Now.ToString("yyyyMM");

            int loyaltyPoints = _paymentCalc.CalculateLoyaltyPoints(totals.NetAmount);

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
                Shift = _currentShift ?? "1",
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
                    _saleRepo.InsertWithoutTransaction(sale, _currentItems);

                    // Create stock movements for each sold item
                    foreach (var item in _currentItems)
                    {
                        long costPrice = _inventoryService.CalculateAverageCost(item.ProductCode);
                        _inventoryService.RecordStockOut(
                            item.ProductCode,
                            item.Quantity,
                            costPrice,
                            "SALE",
                            journalNo,
                            today,
                            _cashierUserId);
                    }

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
            _saleRepo.VoidSale(journalNo, _cashierUserId);
        }

        public void ClearCurrentSale()
        {
            _currentItems.Clear();
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
