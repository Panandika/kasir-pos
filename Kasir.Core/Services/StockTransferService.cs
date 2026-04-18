using System;
using System.Collections.Generic;
using System.Data.SQLite;
using Kasir.Data.Repositories;
using Kasir.Models;
using Kasir.Utils;

namespace Kasir.Services
{
    public class StockTransferService
    {
        private readonly SQLiteConnection _db;
        private readonly StockTransferRepository _transferRepo;
        private readonly CounterRepository _counterRepo;
        private readonly ConfigRepository _configRepo;
        private readonly InventoryService _inventoryService;
        private readonly IClock _clock;

        public StockTransferService(SQLiteConnection db, IClock clock)
        {
            _db = db;
            _transferRepo = new StockTransferRepository(db);
            _counterRepo = new CounterRepository(db);
            _configRepo = new ConfigRepository(db);
            _inventoryService = new InventoryService(db);
            _clock = clock;
        }

        public string CreateTransfer(string fromLocation, string toLocation,
            List<StockTransferItem> items, int userId)
        {
            string registerId = _configRepo.Get("register_id") ?? "01";
            string journalNo = _counterRepo.GetNext("TRM", registerId);
            string today = _clock.Now.ToString("yyyy-MM-dd");
            string period = _clock.Now.ToString("yyyyMM");

            var header = new StockTransfer
            {
                DocType = "TRANSFER",
                JournalNo = journalNo,
                DocDate = today,
                FromLocation = fromLocation,
                ToLocation = toLocation,
                Control = 1,
                PeriodCode = period,
                RegisterId = registerId,
                ChangedBy = userId
            };

            // Set cost price for each item
            foreach (var item in items)
            {
                int avgCost = _inventoryService.CalculateAverageCost(item.ProductCode);
                item.CostPrice = avgCost;
                item.Value = (long)avgCost * item.Quantity;
            }

            _transferRepo.Insert(header, items);

            // Create paired stock movements
            foreach (var item in items)
            {
                // OUT from source
                var outMovement = new StockMovement
                {
                    ProductCode = item.ProductCode,
                    LocationCode = fromLocation,
                    JournalNo = journalNo,
                    MovementType = "TRANSFER_OUT",
                    DocDate = today,
                    PeriodCode = period,
                    QtyOut = item.Quantity,
                    ValOut = item.Value,
                    CostPrice = item.CostPrice,
                    ChangedBy = userId
                };
                new StockMovementRepository(_db).Insert(outMovement);

                // IN to destination
                var inMovement = new StockMovement
                {
                    ProductCode = item.ProductCode,
                    LocationCode = toLocation,
                    JournalNo = journalNo,
                    MovementType = "TRANSFER_IN",
                    DocDate = today,
                    PeriodCode = period,
                    QtyIn = item.Quantity,
                    ValIn = item.Value,
                    CostPrice = item.CostPrice,
                    ChangedBy = userId
                };
                new StockMovementRepository(_db).Insert(inMovement);
            }

            return journalNo;
        }
    }
}
