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
        // Internal codes for quick intake: each register has its own block so two registers
        // never hand out the same code before the next LAN sync (review H2). 9900-9999 is
        // reserved. Codes come from a monotonic per-register counter, so a code freed by a
        // hard delete is never reused; codes already in products (any status, e.g. the
        // legacy 9003+ codes) are skipped.
        public const string QuickCodeCounterPrefix = "QPC";
        private const int QuickCodeBlockStart = 9000;
        private const int QuickCodeBlockSize = 300;
        private static readonly string[] QuickCodeRegisters = { "01", "02", "03" };
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

        // (First, Last) of the register's code block, or null when it has none.
        public static (int First, int Last)? QuickCodeBlock(string registerId)
        {
            int index = Array.IndexOf(QuickCodeRegisters, registerId ?? "");
            if (index < 0) return null;
            int first = QuickCodeBlockStart + index * QuickCodeBlockSize;
            return (first, first + QuickCodeBlockSize - 1);
        }

        // Next unused code of this register's block. Call inside the save transaction: the
        // counter advance rolls back with a failed save.
        private string NextQuickCode(string registerId)
        {
            var block = QuickCodeBlock(registerId)
                ?? throw new InvalidOperationException(
                    "Barang Masuk Cepat hanya untuk kasir 01-03 (kasir " + registerId + " tidak punya blok kode).");
            string range = block.First + "-" + block.Last;
            while (true)
            {
                int code = block.First + _counterRepo.NextValue(QuickCodeCounterPrefix, registerId) - 1;
                if (code > block.Last)
                    throw new InvalidOperationException(
                        "Kode barang " + range + " untuk kasir " + registerId + " sudah habis terpakai.");
                string candidate = code.ToString();
                if (_productRepo.GetByCode(candidate) == null) return candidate;
            }
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
                    string code = NextQuickCode(registerId);
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
                    // qty is a plain unit count; the ledger is x100 (StockQty).
                    _inventoryService.RecordStockIn(code, StockQty.ToLedger(qty), cost, "PURCHASE", journalNo, today, userId);

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
