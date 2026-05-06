namespace Kasir.Models
{
    public class Purchase
    {
        public int Id { get; set; }
        public string DocType { get; set; }
        public string JournalNo { get; set; }
        public string DocDate { get; set; }
        public string AccountCode { get; set; }
        public string SubCode { get; set; }
        public string RefNo { get; set; }
        public string TaxInvoice { get; set; }
        public string DeliveryNote { get; set; }
        public string TaxInvDate { get; set; }
        public string Remark { get; set; }
        public string Warehouse { get; set; }
        public int DiscPct { get; set; }
        public int Disc2Pct { get; set; }
        public string VatFlag { get; set; }
        public long GrossAmount { get; set; }
        public long TotalDisc { get; set; }
        public long VatAmount { get; set; }
        public long TotalValue { get; set; }
        public string DueDate { get; set; }
        public string ReceivedDate { get; set; }
        public int Terms { get; set; }
        public int Control { get; set; }
        public string PeriodCode { get; set; }
        public string RegisterId { get; set; }
        public int ChangedBy { get; set; }
        public string ChangedAt { get; set; }
    }
}
