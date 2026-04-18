using System;
using System.Collections.Generic;
using System.Data.SQLite;
using Kasir.Data.Repositories;
using Kasir.Models;
using Kasir.Utils;

namespace Kasir.Services
{
    public class PurchasingService
    {
        private readonly SQLiteConnection _db;
        private readonly OrderRepository _orderRepo;
        private readonly PurchaseRepository _purchaseRepo;
        private readonly PayablesRepository _payablesRepo;
        private readonly CounterRepository _counterRepo;
        private readonly ConfigRepository _configRepo;
        private readonly InventoryService _inventoryService;
        private readonly IClock _clock;

        public PurchasingService(SQLiteConnection db, IClock clock)
        {
            _db = db;
            _orderRepo = new OrderRepository(db);
            _purchaseRepo = new PurchaseRepository(db);
            _payablesRepo = new PayablesRepository(db);
            _counterRepo = new CounterRepository(db);
            _configRepo = new ConfigRepository(db);
            _inventoryService = new InventoryService(db);
            _clock = clock;
        }

        public string CreatePurchaseOrder(Order order, List<OrderItem> items, int userId)
        {
            string registerId = _configRepo.Get("register_id") ?? "01";
            string journalNo = _counterRepo.GetNext("OMS", registerId);
            string today = _clock.Now.ToString("yyyy-MM-dd");
            string period = _clock.Now.ToString("yyyyMM");

            order.DocType = "PURCHASE_ORDER";
            order.JournalNo = journalNo;
            order.DocDate = order.DocDate ?? today;
            order.PeriodCode = period;
            order.RegisterId = registerId;
            order.Control = 1;
            order.ChangedBy = userId;

            // Calculate total
            long total = 0;
            foreach (var item in items)
            {
                item.Value = (long)item.UnitPrice * item.Quantity;
                total += item.Value;
            }
            order.TotalValue = total;

            _orderRepo.Insert(order, items);
            return journalNo;
        }

        public string CreateGoodsReceipt(Purchase receipt, List<PurchaseItem> items, int userId)
        {
            string registerId = _configRepo.Get("register_id") ?? "01";
            string journalNo = _counterRepo.GetNext("BPB", registerId);
            string today = _clock.Now.ToString("yyyy-MM-dd");
            string period = _clock.Now.ToString("yyyyMM");

            receipt.DocType = "RECEIPT";
            receipt.JournalNo = journalNo;
            receipt.DocDate = receipt.DocDate ?? today;
            receipt.PeriodCode = period;
            receipt.RegisterId = registerId;
            receipt.Control = 1;
            receipt.ChangedBy = userId;

            // Calculate totals
            long gross = 0;
            foreach (var item in items)
            {
                item.Value = (long)item.UnitPrice * item.Quantity;
                gross += item.Value;
            }
            receipt.GrossAmount = gross;
            receipt.TotalValue = gross - receipt.TotalDisc + receipt.VatAmount;

            _purchaseRepo.Insert(receipt, items);

            // Create stock movements for each received item
            foreach (var item in items)
            {
                _inventoryService.RecordStockIn(
                    item.ProductCode,
                    item.Quantity,
                    item.UnitPrice,
                    "PURCHASE",
                    journalNo,
                    receipt.DocDate,
                    userId);
            }

            return journalNo;
        }

        public string CreatePurchaseInvoice(Purchase invoice, List<PurchaseItem> items, int userId)
        {
            string registerId = _configRepo.Get("register_id") ?? "01";
            string journalNo = _counterRepo.GetNext("MSK", registerId);
            string today = _clock.Now.ToString("yyyy-MM-dd");
            string period = _clock.Now.ToString("yyyyMM");

            invoice.DocType = "PURCHASE";
            invoice.JournalNo = journalNo;
            invoice.DocDate = invoice.DocDate ?? today;
            invoice.PeriodCode = period;
            invoice.RegisterId = registerId;
            invoice.Control = 1;
            invoice.ChangedBy = userId;

            // Calculate totals
            long gross = 0;
            foreach (var item in items)
            {
                item.Value = (long)item.UnitPrice * item.Quantity;
                gross += item.Value;
            }
            invoice.GrossAmount = gross;
            invoice.TotalValue = gross - invoice.TotalDisc + invoice.VatAmount;

            _purchaseRepo.Insert(invoice, items);

            // Create AP entry
            _payablesRepo.Insert(new PayablesEntry
            {
                SubCode = invoice.SubCode,
                JournalNo = journalNo,
                DocDate = invoice.DocDate,
                DueDate = invoice.DueDate,
                Direction = "D",
                GrossAmount = invoice.GrossAmount,
                Amount = invoice.TotalValue,
                PaymentAmount = 0,
                Control = 1,
                PeriodCode = period,
                ChangedBy = userId
            });

            return journalNo;
        }

        public string CreatePurchaseReturn(Purchase ret, List<PurchaseItem> items, bool hasInvoice, int userId)
        {
            string registerId = _configRepo.Get("register_id") ?? "01";
            string journalNo = _counterRepo.GetNext("RMS", registerId);
            string today = _clock.Now.ToString("yyyy-MM-dd");
            string period = _clock.Now.ToString("yyyyMM");

            ret.DocType = "PURCHASE_RETURN";
            ret.JournalNo = journalNo;
            ret.DocDate = ret.DocDate ?? today;
            ret.PeriodCode = period;
            ret.RegisterId = registerId;
            ret.Control = 1;
            ret.ChangedBy = userId;

            long gross = 0;
            foreach (var item in items)
            {
                item.Value = (long)item.UnitPrice * item.Quantity;
                gross += item.Value;
            }
            ret.GrossAmount = gross;
            ret.TotalValue = gross;

            _purchaseRepo.Insert(ret, items);

            // Stock out for returned items
            foreach (var item in items)
            {
                _inventoryService.RecordStockOut(
                    item.ProductCode,
                    item.Quantity,
                    item.UnitPrice,
                    "RETURN_OUT",
                    journalNo,
                    ret.DocDate,
                    userId);
            }

            // If with invoice, create offsetting AP entry
            if (hasInvoice && !string.IsNullOrEmpty(ret.RefNo))
            {
                _payablesRepo.RecordPayment(ret.RefNo, ret.TotalValue);
            }

            return journalNo;
        }
    }
}
