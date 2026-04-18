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
        private readonly IClock _clock;

        public StockOpnameService(SqliteConnection db, IClock clock)
        {
            _db = db;
            _adjRepo = new StockAdjustmentRepository(db);
            _counterRepo = new CounterRepository(db);
            _configRepo = new ConfigRepository(db);
            _inventoryService = new InventoryService(db);
            _productRepo = new ProductRepository(db);
            _clock = clock;
        }

        public List<OpnameLine> GetOpnameSheet(int productLimit)
        {
            var products = _productRepo.GetAll(productLimit, 0);
            var lines = new List<OpnameLine>();

            foreach (var p in products)
            {
                lines.Add(new OpnameLine
                {
                    ProductCode = p.ProductCode,
                    ProductName = p.Name,
                    SystemQty = _inventoryService.GetStockOnHand(p.ProductCode),
                    PhysicalQty = 0
                });
            }

            return lines;
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

            _adjRepo.Insert(header, items);

            // Create ADJUSTMENT stock movements
            foreach (var item in items)
            {
                _inventoryService.RecordStockOut(
                    item.ProductCode, item.Quantity, item.CostPrice,
                    "ADJUSTMENT", journalNo, today, userId);
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

            foreach (var line in lines)
            {
                int variance = line.PhysicalQty - line.SystemQty;
                if (variance == 0) continue;

                long avgCost = _inventoryService.CalculateAverageCost(line.ProductCode);

                adjustItems.Add(new StockAdjustmentItem
                {
                    ProductCode = line.ProductCode,
                    Quantity = Math.Abs(variance),
                    CostPrice = avgCost,
                    Value = avgCost * Math.Abs(variance),
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
                _adjRepo.Insert(header, adjustItems);
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
        public int Variance { get { return PhysicalQty - SystemQty; } }
    }
}
