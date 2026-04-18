using System;
using System.Collections.Generic;
using Microsoft.Data.Sqlite;
using Kasir.Data;
using Kasir.Data.Repositories;
using Kasir.Models;
using Kasir.Utils;

namespace Kasir.Services
{
    public class PriceChangeService
    {
        private readonly SqliteConnection _db;
        private readonly ProductRepository _productRepo;
        private readonly PriceHistoryRepository _historyRepo;

        public PriceChangeService(SqliteConnection db)
        {
            _db = db;
            _productRepo = new ProductRepository(db);
            _historyRepo = new PriceHistoryRepository(db);
        }

        public int ApplyBatchPriceChange(List<PriceChangeEntry> changes, int userId)
        {
            int count = 0;
            string today = DateTime.Now.ToString("yyyy-MM-dd");
            string period = DateTime.Now.ToString("yyyyMM");

            using (var txn = _db.BeginTransaction())
            {
                try
                {
                    foreach (var change in changes)
                    {
                        var product = _productRepo.GetByCode(change.ProductCode);
                        if (product == null) continue;

                        int oldPrice = product.Price;
                        int newPrice = change.NewPrice;

                        if (oldPrice == newPrice) continue;

                        // Record history
                        _historyRepo.Insert(new PriceHistory
                        {
                            ProductCode = change.ProductCode,
                            DocDate = today,
                            Value = newPrice,
                            OldValue = oldPrice,
                            PeriodCode = period
                        });

                        // Update product price
                        product.Price = newPrice;
                        if (change.NewBuyingPrice > 0)
                        {
                            product.BuyingPrice = change.NewBuyingPrice;
                        }
                        product.ChangedBy = userId;
                        _productRepo.Update(product);

                        count++;
                    }

                    txn.Commit();
                }
                catch
                {
                    txn.Rollback();
                    throw;
                }
            }

            return count;
        }
    }

    public class PriceChangeEntry
    {
        public string ProductCode { get; set; }
        public int NewPrice { get; set; }
        public int NewBuyingPrice { get; set; }
    }
}
