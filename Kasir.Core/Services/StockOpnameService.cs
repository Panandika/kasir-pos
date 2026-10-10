using System;
using System.Collections.Generic;
using Microsoft.Data.Sqlite;
using Kasir.Data.Repositories;
using Kasir.Models;
using Kasir.Utils;

namespace Kasir.Services
{
    public class StockOpnameService
    {
        private readonly SqliteConnection _db;
        private readonly StockAdjustmentRepository _adjRepo;
        private readonly CounterRepository _counterRepo;
        private readonly ConfigRepository _configRepo;
        private readonly InventoryService _inventoryService;
        private readonly ProductRepository _productRepo;
        private readonly StockMovementRepository _movementRepo;
        private readonly IClock _clock;

        public StockOpnameService(SqliteConnection db, IClock clock)
        {
            _db = db;
            _adjRepo = new StockAdjustmentRepository(db);
            _counterRepo = new CounterRepository(db);
            _configRepo = new ConfigRepository(db);
            _inventoryService = new InventoryService(db);
            _productRepo = new ProductRepository(db);
            _movementRepo = new StockMovementRepository(db);
            _clock = clock;
        }

        // Lines start uncounted: the system qty is taken per line when it is counted
        // (RecordCount), not for the whole sheet up front (PR-K6).
        public List<OpnameLine> GetOpnameSheet(int productLimit)
        {
            var products = _productRepo.GetAll(productLimit, 0);
            var lines = new List<OpnameLine>();

            foreach (var p in products)
            {
                // Code 1/2/44/99 and the category keys have no stock to count (review L3, K1/K4).
                if (SalesService.IsNonStockItem(p.ProductCode)) continue;
                lines.Add(new OpnameLine
                {
                    ProductCode = p.ProductCode,
                    ProductName = p.Name,
                    SystemQty = 0,
                    PhysicalQty = 0
                });
            }

            return lines;
        }

        // The shelf was counted now: stamp the time and take the system qty at this moment.
        // physicalQty is a LEDGER qty (x100, StockQty) like SystemQty, so the variance is
        // never mixed-scale; OpnameView converts the typed unit count with ToLedger.
        public void RecordCount(OpnameLine line, int physicalQty)
        {
            line.PhysicalQty = physicalQty;
            line.CountTime = _clock.Now;
            line.SystemQty = _inventoryService.GetStockOnHand(line.ProductCode);
        }

        public string CreateStockOut(string docType, string locationCode,
            List<StockAdjustmentItem> items, int userId)
        {
            string registerId = _configRepo.Get("register_id") ?? "01";
            string journalNo = _counterRepo.GetNext("OTM", registerId);
            string today = _clock.Now.ToString("yyyy-MM-dd");
            string period = _clock.Now.ToString("yyyyMM");

            var header = new StockAdjustment
            {
                DocType = docType, // USAGE, DAMAGE, or LOSS
                JournalNo = journalNo,
                DocDate = today,
                LocationCode = locationCode,
                Control = 1,
                PeriodCode = period,
                RegisterId = registerId,
                ChangedBy = userId
            };

            foreach (var item in items)
            {
                long avgCost = _inventoryService.CalculateAverageCost(item.ProductCode);
                item.CostPrice = avgCost;
                item.Value = avgCost * item.Quantity;
            }

            // Atomic: the adjustment document and its stock-out movements must all land
            // or none (F21).
            using (var txn = _db.BeginTransaction())
            {
                try
                {
                    _adjRepo.InsertWithoutTransaction(header, items);

                    foreach (var item in items)
                    {
                        // The document holds plain units; the ledger is x100 (StockQty).
                        _inventoryService.RecordStockOut(
                            item.ProductCode, StockQty.ToLedger(item.Quantity), item.CostPrice,
                            "ADJUSTMENT", journalNo, today, userId);
                    }

                    txn.Commit();
                }
                catch { txn.Rollback(); throw; }
            }

            return journalNo;
        }

        public string CreateOpnameAdjustment(List<OpnameLine> lines, int userId)
        {
            string registerId = _configRepo.Get("register_id") ?? "01";
            string journalNo = _counterRepo.GetNext("OPN", registerId);
            string today = _clock.Now.ToString("yyyy-MM-dd");
            string period = _clock.Now.ToString("yyyyMM");

            var adjustItems = new List<StockAdjustmentItem>();

            // Atomic: the OPNAME stock movements and the adjustment document must all land
            // or none — otherwise a failure leaves orphaned OPNAME movements with no
            // adjustment header (F21).
            using (var txn = _db.BeginTransaction())
            {
                try
                {
                    foreach (var line in lines)
                    {
                        // Belum dihitung: an uncounted product keeps its stock as it is.
                        if (!line.IsCounted) continue;

                        // Sales/receipts since the count happened after the shelf was seen:
                        // compare the count with the on-hand AT count time, not now.
                        var since = _movementRepo.GetMovementsSince(line.ProductCode, line.CountTime.Value);
                        line.SystemQty = _inventoryService.GetStockOnHand(line.ProductCode)
                            - (since.QtyIn - since.QtyOut);
                        int variance = line.Variance;
                        if (variance == 0) continue;

                        long avgCost = _inventoryService.CalculateAverageCost(line.ProductCode);

                        // variance is ledger x100; the adjustment document keeps the ledger
                        // qty (a counted shelf against x100 legacy stock can differ by a
                        // fraction of a unit) and its money value divides the scale out.
                        adjustItems.Add(new StockAdjustmentItem
                        {
                            ProductCode = line.ProductCode,
                            Quantity = Math.Abs(variance),
                            CostPrice = avgCost,
                            Value = StockQty.Value(avgCost, Math.Abs(variance)),
                            Reason = variance > 0 ? "SURPLUS" : "SHORTAGE"
                        });

                        // Create OPNAME stock movement
                        if (variance > 0)
                        {
                            _inventoryService.RecordStockIn(
                                line.ProductCode, variance, avgCost,
                                "OPNAME", journalNo, today, userId);
                        }
                        else
                        {
                            _inventoryService.RecordStockOut(
                                line.ProductCode, Math.Abs(variance), avgCost,
                                "OPNAME", journalNo, today, userId);
                        }
                    }

                    if (adjustItems.Count > 0)
                    {
                        var header = new StockAdjustment
                        {
                            DocType = "OPNAME",
                            JournalNo = journalNo,
                            DocDate = today,
                            Control = 1,
                            PeriodCode = period,
                            RegisterId = registerId,
                            ChangedBy = userId
                        };
                        _adjRepo.InsertWithoutTransaction(header, adjustItems);
                    }

                    txn.Commit();
                }
                catch { txn.Rollback(); throw; }
            }

            return journalNo;
        }
    }

    public class OpnameLine
    {
        public string ProductCode { get; set; }
        public string ProductName { get; set; }
        public int SystemQty { get; set; }
        public int PhysicalQty { get; set; }
        // When the shelf was counted (PR-K6); null = belum dihitung, never adjusted.
        public DateTime? CountTime { get; set; }
        public bool IsCounted { get { return CountTime.HasValue; } }
        public int Variance { get { return PhysicalQty - SystemQty; } }
    }
}
