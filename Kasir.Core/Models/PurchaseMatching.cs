using System.Collections.Generic;

namespace Kasir.Models
{
    // One product on a purchase order and how much of it has been received so far.
    public class OrderLineStatus
    {
        public string ProductCode { get; set; }
        public int Ordered { get; set; }
        public int Received { get; set; }
        public long UnitPrice { get; set; }
        public int Remaining => Ordered > Received ? Ordered - Received : 0;
    }

    // One product on a goods receipt (BPB) and how much of it has been billed so far.
    public class ReceiptLineStatus
    {
        public string ProductCode { get; set; }
        public int Received { get; set; }
        public int Invoiced { get; set; }
        public long UnitPrice { get; set; }
        public int Uninvoiced => Received > Invoiced ? Received - Invoiced : 0;
    }

    // Result of checking invoice lines against the PO / BPB they reference.
    // Errors block saving; warnings (e.g. price differs from the BPB) need user confirmation.
    public class InvoiceMatchResult
    {
        public List<string> Errors { get; } = new List<string>();
        public List<string> Warnings { get; } = new List<string>();
        public bool IsBlocked => Errors.Count > 0;
    }
}
