using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Data.Sqlite;
using Kasir.Data.Repositories;
using Kasir.Models;
using Kasir.Utils;

namespace Kasir.Services
{
    public class PurchasingService
    {
        public const string OrderStatusOpen = "OPEN";
        public const string OrderStatusPartial = "PARTIAL";
        public const string OrderStatusDone = "DONE";
        public const string OrderStatusVoid = "VOID";

        private readonly SqliteConnection _db;
        private readonly OrderRepository _orderRepo;
        private readonly PurchaseRepository _purchaseRepo;
        private readonly PayablesRepository _payablesRepo;
        private readonly CounterRepository _counterRepo;
        private readonly ConfigRepository _configRepo;
        private readonly InventoryService _inventoryService;
        private readonly IClock _clock;

        public PurchasingService(SqliteConnection db, IClock clock)
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
                item.Value = item.UnitPrice * item.Quantity;
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
                item.Value = item.UnitPrice * item.Quantity;
                gross += item.Value;
            }
            receipt.GrossAmount = gross;
            receipt.TotalValue = gross - receipt.TotalDisc + receipt.VatAmount;

            // Lines linked to a PO must match it: same vendor, product on the PO, no over-receipt.
            var errors = new List<string>();
            ValidateAgainstOrders(receipt.SubCode, items.Where(i => !string.IsNullOrEmpty(i.OrderRef)), errors);
            if (errors.Count > 0) throw new PurchaseValidationException(errors);

            // Atomic: the receipt and its stock-in movements must all land or none (F19).
            using (var txn = _db.BeginTransaction())
            {
                try
                {
                    _purchaseRepo.InsertWithoutTransaction(receipt, items);

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

                    txn.Commit();
                }
                catch { txn.Rollback(); throw; }
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
                item.Value = item.UnitPrice * item.Quantity;
                gross += item.Value;
            }
            invoice.GrossAmount = gross;
            invoice.TotalValue = gross - invoice.TotalDisc + invoice.VatAmount;

            // Lines billing a BPB were already received; every other line is a combined
            // receive-and-bill (legacy MSK) and moves stock in here.
            var match = MatchInvoiceLines(invoice.SubCode, items, out var receiptRefs);
            if (match.IsBlocked) throw new PurchaseValidationException(match.Errors);

            // Atomic: the invoice, its AP entry and any stock-in must all land or none (F19).
            using (var txn = _db.BeginTransaction())
            {
                try
                {
                    _purchaseRepo.InsertWithoutTransaction(invoice, items);

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

                    foreach (var item in items)
                    {
                        if (!string.IsNullOrEmpty(item.OrderRef) && receiptRefs.Contains(item.OrderRef))
                            continue;

                        _inventoryService.RecordStockIn(
                            item.ProductCode,
                            item.Quantity,
                            item.UnitPrice,
                            "PURCHASE",
                            journalNo,
                            invoice.DocDate,
                            userId);
                    }

                    txn.Commit();
                }
                catch { txn.Rollback(); throw; }
            }

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
                item.Value = item.UnitPrice * item.Quantity;
                gross += item.Value;
            }
            ret.GrossAmount = gross;
            ret.TotalValue = gross;

            // Atomic: the return, its stock-out movements, and the AP offset must all
            // land or none (F19).
            using (var txn = _db.BeginTransaction())
            {
                try
                {
                    _purchaseRepo.InsertWithoutTransaction(ret, items);

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

                    if (hasInvoice && !string.IsNullOrEmpty(ret.RefNo))
                    {
                        _payablesRepo.RecordPayment(ret.RefNo, ret.TotalValue);
                    }

                    txn.Commit();
                }
                catch { txn.Rollback(); throw; }
            }

            return journalNo;
        }

        // ---------- PO → BPB → invoice matching ----------

        // Ordered vs received per product. Receipts (BPB) and combined invoices (MSK) that
        // reference the PO both count as received; voided documents are ignored.
        public List<OrderLineStatus> GetOrderReceiptStatus(string orderNo)
        {
            var received = _purchaseRepo.GetLinkedQuantities(orderNo, "RECEIPT", "PURCHASE");
            var lines = new List<OrderLineStatus>();
            foreach (var group in _orderRepo.GetItems(orderNo).GroupBy(i => i.ProductCode))
            {
                received.TryGetValue(group.Key, out int got);
                lines.Add(new OrderLineStatus
                {
                    ProductCode = group.Key,
                    Ordered = group.Sum(i => i.Quantity),
                    Received = got,
                    UnitPrice = group.First().UnitPrice
                });
            }
            return lines;
        }

        public string GetOrderStatus(string orderNo)
        {
            var order = _orderRepo.GetByJournalNo(orderNo);
            if (order == null || order.Control == 3) return OrderStatusVoid;

            var lines = GetOrderReceiptStatus(orderNo);
            if (lines.All(l => l.Remaining == 0)) return OrderStatusDone;
            if (lines.Any(l => l.Received > 0)) return OrderStatusPartial;
            return OrderStatusOpen;
        }

        // Purchase orders for the vendor that still have something left to receive.
        public List<Order> GetOpenPurchaseOrders(string vendorCode)
        {
            return _orderRepo.GetActivePurchaseOrders(vendorCode)
                .Where(o => GetOrderReceiptStatus(o.JournalNo).Any(l => l.Remaining > 0))
                .ToList();
        }

        // Received vs billed per product on a goods receipt.
        public List<ReceiptLineStatus> GetReceiptInvoiceStatus(string receiptNo)
        {
            var invoiced = _purchaseRepo.GetLinkedQuantities(receiptNo, "PURCHASE");
            var lines = new List<ReceiptLineStatus>();
            foreach (var group in _purchaseRepo.GetItems(receiptNo).GroupBy(i => i.ProductCode))
            {
                invoiced.TryGetValue(group.Key, out int billed);
                lines.Add(new ReceiptLineStatus
                {
                    ProductCode = group.Key,
                    Received = group.Sum(i => i.Quantity),
                    Invoiced = billed,
                    UnitPrice = group.First().UnitPrice
                });
            }
            return lines;
        }

        // Goods receipts for the vendor that have not been fully billed yet.
        public List<Purchase> GetUninvoicedReceipts(string vendorCode)
        {
            return _purchaseRepo.GetActiveByVendor(vendorCode, "RECEIPT")
                .Where(r => GetReceiptInvoiceStatus(r.JournalNo).Any(l => l.Uninvoiced > 0))
                .ToList();
        }

        // "PURCHASE_ORDER" or "RECEIPT" when refNo is a document an invoice/receipt line can
        // link to; null otherwise.
        public string GetLinkableDocType(string refNo)
        {
            var order = _orderRepo.GetByJournalNo(refNo);
            if (order != null) return order.DocType == "PURCHASE_ORDER" ? order.DocType : null;
            var purchase = _purchaseRepo.GetByJournalNo(refNo);
            return purchase?.DocType == "RECEIPT" ? purchase.DocType : null;
        }

        // Checks invoice lines against the documents they reference (order_ref):
        //   BPB  → bill a receipt: qty must not exceed the unbilled qty; price differences warn.
        //   PO   → receive and bill against the PO: same rules as a PO-linked receipt.
        //   none → plain combined receive-and-bill, nothing to match.
        public InvoiceMatchResult CheckInvoiceMatch(string vendorCode, List<PurchaseItem> items)
        {
            return MatchInvoiceLines(vendorCode, items, out _);
        }

        private InvoiceMatchResult MatchInvoiceLines(string vendorCode, List<PurchaseItem> items,
            out HashSet<string> receiptRefs)
        {
            var result = new InvoiceMatchResult();
            receiptRefs = new HashSet<string>();
            var orderLines = new List<PurchaseItem>();

            foreach (var byRef in items.Where(i => !string.IsNullOrEmpty(i.OrderRef)).GroupBy(i => i.OrderRef))
            {
                if (_orderRepo.GetByJournalNo(byRef.Key) != null)
                {
                    orderLines.AddRange(byRef);
                    continue;
                }

                var receipt = _purchaseRepo.GetByJournalNo(byRef.Key);
                if (receipt == null || receipt.DocType != "RECEIPT")
                {
                    result.Errors.Add($"Dokumen {byRef.Key} tidak ditemukan (harus PO atau BPB).");
                    continue;
                }
                receiptRefs.Add(byRef.Key);
                if (receipt.Control == 3)
                {
                    result.Errors.Add($"BPB {byRef.Key} sudah dibatalkan.");
                    continue;
                }
                if (receipt.SubCode != vendorCode)
                {
                    result.Errors.Add($"BPB {byRef.Key} milik supplier lain ({receipt.SubCode}).");
                    continue;
                }

                var status = GetReceiptInvoiceStatus(byRef.Key).ToDictionary(l => l.ProductCode);
                foreach (var byProduct in byRef.GroupBy(i => i.ProductCode))
                {
                    if (!status.TryGetValue(byProduct.Key, out var line))
                    {
                        result.Errors.Add($"{byProduct.Key} tidak ada di BPB {byRef.Key}.");
                        continue;
                    }
                    int qty = byProduct.Sum(i => i.Quantity);
                    if (qty > line.Uninvoiced)
                    {
                        result.Errors.Add($"{byProduct.Key}: ditagih {qty} melebihi sisa belum ditagih di BPB {byRef.Key} ({line.Uninvoiced}).");
                    }
                    foreach (var item in byProduct.Where(i => i.UnitPrice != line.UnitPrice))
                    {
                        result.Warnings.Add($"{byProduct.Key}: harga nota {Formatting.FormatCurrencyShort(item.UnitPrice)} berbeda dari BPB {byRef.Key} ({Formatting.FormatCurrencyShort(line.UnitPrice)}).");
                    }
                }
            }

            ValidateAgainstOrders(vendorCode, orderLines, result.Errors);
            return result;
        }

        // Validates PO-linked lines (receipt or combined invoice) and stamps each line's
        // QtyOrder with the ordered qty for that product.
        private void ValidateAgainstOrders(string vendorCode, IEnumerable<PurchaseItem> lines, List<string> errors)
        {
            foreach (var byRef in lines.GroupBy(i => i.OrderRef))
            {
                var order = _orderRepo.GetByJournalNo(byRef.Key);
                if (order == null || order.DocType != "PURCHASE_ORDER")
                {
                    errors.Add($"PO {byRef.Key} tidak ditemukan.");
                    continue;
                }
                if (order.Control == 3)
                {
                    errors.Add($"PO {byRef.Key} sudah dibatalkan.");
                    continue;
                }
                if (order.SubCode != vendorCode)
                {
                    errors.Add($"PO {byRef.Key} milik supplier lain ({order.SubCode}).");
                    continue;
                }

                var status = GetOrderReceiptStatus(byRef.Key).ToDictionary(l => l.ProductCode);
                foreach (var byProduct in byRef.GroupBy(i => i.ProductCode))
                {
                    if (!status.TryGetValue(byProduct.Key, out var line))
                    {
                        errors.Add($"{byProduct.Key} tidak ada di PO {byRef.Key}.");
                        continue;
                    }
                    int qty = byProduct.Sum(i => i.Quantity);
                    if (qty > line.Remaining)
                    {
                        errors.Add($"{byProduct.Key}: diterima {qty} melebihi sisa PO {byRef.Key} ({line.Remaining}).");
                    }
                    foreach (var item in byProduct) item.QtyOrder = line.Ordered;
                }
            }
        }
    }
}
