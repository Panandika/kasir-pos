using System;
using System.Data.SQLite;
using Kasir.Data;
using Kasir.Data.Repositories;
using Kasir.Models;

namespace Kasir.Services
{
    public class InventoryService
    {
        private readonly SQLiteConnection _db;
        private readonly StockMovementRepository _movementRepo;
        private readonly ConfigRepository _configRepo;

        public InventoryService(SQLiteConnection db)
        {
            _db = db;
            _movementRepo = new StockMovementRepository(db);
            _configRepo = new ConfigRepository(db);
        }

        public int GetStockOnHand(string productCode)
        {
            return _movementRepo.GetStockOnHand(productCode);
        }

        public int GetStockOnHandByLocation(string productCode, string locationCode)
        {
            return _movementRepo.GetStockOnHandByLocation(productCode, locationCode);
        }

        public long GetCostPrice(string productCode, int qty)
        {
            string method = _configRepo.Get("costing_method") ?? "AVG";
            if (method == "FIFO")
            {
                return CalculateFifoCost(productCode, qty);
            }
            return CalculateAverageCost(productCode) * qty;
        }

        public long CalculateFifoCost(string productCode, int qtyNeeded)
        {
            var purchases = _movementRepo.GetPurchaseMovements(productCode);

            // Calculate how much has already been consumed (sold/transferred out)
            int totalIn = 0;
            int totalOut = _movementRepo.GetStockOnHand(productCode);
            // totalOut here is net stock. consumed = totalPurchased - netStock
            foreach (var p in purchases)
            {
                totalIn += p.QtyIn;
            }
            int consumed = totalIn - (totalOut < 0 ? 0 : totalOut + qtyNeeded);
            // Actually, simpler: walk layers, skip already-consumed qty

            // Recalculate: total sold/out = totalIn - currentOnHand
            int currentOnHand = _movementRepo.GetStockOnHand(productCode);
            int alreadyConsumed = totalIn - currentOnHand;

            long totalCost = 0;
            int remaining = qtyNeeded;
            int skipped = 0;

            foreach (var lot in purchases)
            {
                int lotQty = lot.QtyIn;

                // Skip already consumed lots
                if (skipped < alreadyConsumed)
                {
                    int toSkip = Math.Min(lotQty, alreadyConsumed - skipped);
                    skipped += toSkip;
                    lotQty -= toSkip;
                }

                if (lotQty <= 0) continue;

                int take = Math.Min(lotQty, remaining);
                long unitCost = lot.QtyIn > 0 ? lot.ValIn / lot.QtyIn : 0;
                totalCost += unitCost * take;
                remaining -= take;

                if (remaining <= 0) break;
            }

            return totalCost;
        }

        public int CalculateAverageCost(string productCode)
        {
            // Weighted average: total purchase value / total purchase qty
            long totalVal = SqlHelper.ExecuteScalar<long>(_db,
                @"SELECT COALESCE(SUM(val_in), 0) FROM stock_movements
                  WHERE product_code = @code AND movement_type = 'PURCHASE'",
                SqlHelper.Param("@code", productCode));

            int totalQty = SqlHelper.ExecuteScalar<int>(_db,
                @"SELECT COALESCE(SUM(qty_in), 0) FROM stock_movements
                  WHERE product_code = @code AND movement_type = 'PURCHASE'",
                SqlHelper.Param("@code", productCode));

            if (totalQty <= 0) return 0;
            return (int)(totalVal / totalQty);
        }

        public void RecordStockIn(string productCode, int qty, int unitCost,
            string movementType, string journalNo, string docDate, int changedBy)
        {
            var movement = new StockMovement
            {
                ProductCode = productCode,
                JournalNo = journalNo,
                MovementType = movementType,
                DocDate = docDate,
                PeriodCode = docDate.Length >= 7 ? docDate.Substring(0, 4) + docDate.Substring(5, 2) : "",
                QtyIn = qty,
                QtyOut = 0,
                ValIn = (long)unitCost * qty,
                ValOut = 0,
                CostPrice = unitCost,
                ChangedBy = changedBy
            };

            _movementRepo.Insert(movement);
        }

        public void RecordStockOut(string productCode, int qty, int costPrice,
            string movementType, string journalNo, string docDate, int changedBy)
        {
            var movement = new StockMovement
            {
                ProductCode = productCode,
                JournalNo = journalNo,
                MovementType = movementType,
                DocDate = docDate,
                PeriodCode = docDate.Length >= 7 ? docDate.Substring(0, 4) + docDate.Substring(5, 2) : "",
                QtyIn = 0,
                QtyOut = qty,
                ValIn = 0,
                ValOut = (long)costPrice * qty,
                CostPrice = costPrice,
                ChangedBy = changedBy
            };

            _movementRepo.Insert(movement);
        }

        public StockVariance CalculateVariance(string productCode, int physicalQty)
        {
            int systemQty = GetStockOnHand(productCode);
            int avgCost = CalculateAverageCost(productCode);
            int variance = physicalQty - systemQty;

            return new StockVariance
            {
                ProductCode = productCode,
                SystemQty = systemQty,
                PhysicalQty = physicalQty,
                Variance = variance,
                VarianceCost = (long)avgCost * Math.Abs(variance)
            };
        }
    }

    public class StockVariance
    {
        public string ProductCode { get; set; }
        public int SystemQty { get; set; }
        public int PhysicalQty { get; set; }
        public int Variance { get; set; }
        public long VarianceCost { get; set; }
    }
}
