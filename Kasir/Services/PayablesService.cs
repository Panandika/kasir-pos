using System;
using System.Collections.Generic;
using System.Data.SQLite;
using Kasir.Data;
using Kasir.Data.Repositories;
using Kasir.Models;

namespace Kasir.Services
{
    public class AgingBucket
    {
        public string VendorCode { get; set; }
        public string VendorName { get; set; }
        public long Current { get; set; }
        public long Days30 { get; set; }
        public long Days60 { get; set; }
        public long Days90 { get; set; }
        public long Days120Plus { get; set; }
        public long Total { get; set; }
    }

    public class PaymentAllocationResult
    {
        public long AmountAllocated { get; set; }
        public long AmountRemaining { get; set; }
        public int InvoicesPaid { get; set; }
        public int InvoicesPartiallyPaid { get; set; }
        public List<string> PaidJournalNos { get; set; }

        public PaymentAllocationResult()
        {
            PaidJournalNos = new List<string>();
        }
    }

    public class PayablesService
    {
        private readonly SQLiteConnection _db;
        private readonly PayablesRepository _payablesRepo;
        private readonly AccountingService _accountingService;
        private readonly CounterRepository _counterRepo;

        public PayablesService(SQLiteConnection db)
        {
            _db = db;
            _payablesRepo = new PayablesRepository(db);
            _accountingService = new AccountingService(db);
            _counterRepo = new CounterRepository(db);
        }

        public List<PayablesEntry> GetOutstanding(string vendorCode)
        {
            return _payablesRepo.GetUnpaidByVendor(vendorCode);
        }

        public long GetTotalOutstanding(string vendorCode)
        {
            return _payablesRepo.GetTotalUnpaidByVendor(vendorCode);
        }

        public PaymentAllocationResult AllocatePayment(string vendorCode, long paymentAmount,
            string cashAccountCode, string docDate, string periodCode, int changedBy,
            List<string> specificInvoices)
        {
            if (paymentAmount <= 0)
            {
                throw new ArgumentException("Payment amount must be positive");
            }

            var result = new PaymentAllocationResult();
            long remaining = paymentAmount;

            List<PayablesEntry> invoices;
            if (specificInvoices != null && specificInvoices.Count > 0)
            {
                invoices = new List<PayablesEntry>();
                foreach (var jnl in specificInvoices)
                {
                    var entry = _payablesRepo.GetByJournalNo(jnl);
                    if (entry != null && entry.IsPaid != "Y")
                    {
                        invoices.Add(entry);
                    }
                }
            }
            else
            {
                // Oldest first
                invoices = _payablesRepo.GetUnpaidByVendor(vendorCode);
            }

            using (var txn = _db.BeginTransaction())
            {
                try
                {
                    foreach (var invoice in invoices)
                    {
                        if (remaining <= 0) break;

                        long outstanding = invoice.Amount - invoice.PaymentAmount;
                        if (outstanding <= 0) continue;

                        long allocation = Math.Min(remaining, outstanding);
                        _payablesRepo.RecordPayment(invoice.JournalNo, allocation);

                        remaining -= allocation;
                        result.AmountAllocated += allocation;

                        if (allocation >= outstanding)
                        {
                            result.InvoicesPaid++;
                        }
                        else
                        {
                            result.InvoicesPartiallyPaid++;
                        }

                        result.PaidJournalNos.Add(invoice.JournalNo);
                    }

                    result.AmountRemaining = remaining;

                    // Post GL: debit AP, credit Cash/Bank
                    string paymentJnl = _counterRepo.GetNext("KKL", "01");
                    _accountingService.PostPaymentJournal(paymentJnl, docDate, periodCode,
                        vendorCode, result.AmountAllocated, cashAccountCode, changedBy);

                    txn.Commit();
                    return result;
                }
                catch
                {
                    txn.Rollback();
                    throw;
                }
            }
        }

        public List<AgingBucket> GetAgingReport(string asOfDate)
        {
            // Get all vendors with outstanding payables
            var vendors = SqlHelper.Query(_db,
                @"SELECT DISTINCT p.sub_code, s.name
                  FROM payables_register p
                  LEFT JOIN subsidiaries s ON s.sub_code = p.sub_code
                  WHERE p.is_paid = 'N' AND p.control != 3
                  ORDER BY p.sub_code",
                r => new { Code = SqlHelper.GetString(r, "sub_code"), Name = SqlHelper.GetString(r, "name") });

            var result = new List<AgingBucket>();

            foreach (var vendor in vendors)
            {
                var bucket = new AgingBucket
                {
                    VendorCode = vendor.Code,
                    VendorName = vendor.Name
                };

                var unpaid = _payablesRepo.GetUnpaidByVendor(vendor.Code);
                foreach (var entry in unpaid)
                {
                    long outstanding = entry.Amount - entry.PaymentAmount;
                    if (outstanding <= 0) continue;

                    int daysOld = CalculateDaysOld(entry.DueDate ?? entry.DocDate, asOfDate);

                    if (daysOld <= 0)
                    {
                        bucket.Current += outstanding;
                    }
                    else if (daysOld <= 30)
                    {
                        bucket.Days30 += outstanding;
                    }
                    else if (daysOld <= 60)
                    {
                        bucket.Days60 += outstanding;
                    }
                    else if (daysOld <= 90)
                    {
                        bucket.Days90 += outstanding;
                    }
                    else
                    {
                        bucket.Days120Plus += outstanding;
                    }

                    bucket.Total += outstanding;
                }

                if (bucket.Total > 0)
                {
                    result.Add(bucket);
                }
            }

            return result;
        }

        private static int CalculateDaysOld(string dateStr, string asOfDate)
        {
            DateTime dueDate;
            DateTime asOf;

            if (!DateTime.TryParse(dateStr, out dueDate))
            {
                return 0;
            }
            if (!DateTime.TryParse(asOfDate, out asOf))
            {
                return 0;
            }

            return (int)(asOf - dueDate).TotalDays;
        }
    }
}
