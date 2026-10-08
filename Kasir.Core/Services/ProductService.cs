using System;
using System.Collections.Generic;
using Microsoft.Data.Sqlite;
using Kasir.Data;
using Kasir.Data.Repositories;
using Kasir.Models;
using Kasir.Utils;

namespace Kasir.Services
{
    public class QuickProductResult
    {
        public string ProductCode { get; set; }
        public string JournalNo { get; set; }
    }

    // PR-K5 "Barang Masuk Cepat": marketplace stock without a barcode gets an internal code,
    // a product row with its cost, and a PURCHASE stock movement in one transaction.
    public class ProductService
    {
        // Internal codes for quick intake. Codes already in use (any status) are skipped.
        public const int QuickCodeFirst = 9000;
        public const int QuickCodeLast = 9999;
        // Counter prefix of the stock-in document (BMC-01-2610-0001).
        public const string QuickIntakePrefix = "BMC";
        private const int MaxNameLength = 75; // legacy NAME C(75)

        private readonly SqliteConnection _db;
        private readonly ProductRepository _productRepo;
        private readonly CounterRepository _counterRepo;
        private readonly ConfigRepository _configRepo;
        private readonly InventoryService _inventoryService;
        private readonly IClock _clock;

        public ProductService(SqliteConnection db, IClock clock)
        {
            _db = db;
            _productRepo = new ProductRepository(db);
            _counterRepo = new CounterRepository(db);
            _configRepo = new ConfigRepository(db);
            _inventoryService = new InventoryService(db);
            _clock = clock;
        }

        // Lowest 4-digit code in 9000-9999 that no product (active or not) uses.
        public string NextFreeCode()
        {
            var used = new HashSet<string>(SqlHelper.Query(_db,
                @"SELECT product_code FROM products
                  WHERE length(product_code) = 4 AND product_code GLOB '9[0-9][0-9][0-9]'",
                r => SqlHelper.GetString(r, "product_code")));
            for (int code = QuickCodeFirst; code <= QuickCodeLast; code++)
            {
                string candidate = code.ToString();
                if (!used.Contains(candidate)) return candidate;
            }
            throw new InvalidOperationException("Kode barang 9000-9999 sudah habis terpakai.");
        }

        // cost / price are x100 money (per unit); qty in units. Category = one of the
        // quick-key codes (AL/AT/PR/PL/MY/LL); the product takes that category's department.
        public QuickProductResult CreateQuickProduct(string name, string categoryCode, long cost, long price,
            int qty, int userId)
        {
            string cleanName = (name ?? "").Trim().ToUpperInvariant();
            if (cleanName.Length == 0) throw new ArgumentException("Nama barang harus diisi.", nameof(name));
            if (cleanName.Length > MaxNameLength)
                throw new ArgumentException("Nama barang maksimal " + MaxNameLength + " huruf.", nameof(name));
            string category = SalesService.ResolveCategoryKey(categoryCode)
                ?? throw new ArgumentException("Kategori tidak dikenal: " + categoryCode, nameof(categoryCode));
            if (cost <= 0) throw new ArgumentException("Harga modal harus > 0.", nameof(cost));
            if (price <= 0) throw new ArgumentException("Harga jual harus > 0.", nameof(price));
            if (qty <= 0) throw new ArgumentException("Qty harus > 0.", nameof(qty));

            var categoryRow = _productRepo.GetByCode(category);
            string registerId = _configRepo.Get("register_id") ?? "01";
            string today = _clock.Now.ToString("yyyy-MM-dd");

            using (var txn = _db.BeginTransaction())
            {
                try
                {
                    string code = NextFreeCode();
                    _productRepo.Insert(new Product
                    {
                        ProductCode = code,
                        Name = cleanName,
                        DeptCode = categoryRow?.DeptCode ?? "100",
                        Status = "A",
                        Unit = "PCS",
                        Price = price,
                        BuyingPrice = cost,
                        CostPrice = cost,
                        OpenPrice = "N",
                        VatFlag = "N",
                        LuxuryTaxFlag = "N",
                        IsConsignment = "N",
                        ChangedBy = userId
                    });

                    string journalNo = _counterRepo.GetNext(QuickIntakePrefix, registerId);
                    _inventoryService.RecordStockIn(code, qty, cost, "PURCHASE", journalNo, today, userId);

                    txn.Commit();
                    return new QuickProductResult { ProductCode = code, JournalNo = journalNo };
                }
                catch
                {
                    txn.Rollback();
                    throw;
                }
            }
        }
    }
}
