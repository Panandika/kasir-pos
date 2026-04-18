namespace Kasir.Models
{
    public class Sale
    {
        public int Id { get; set; }
        public string DocType { get; set; }
        public string JournalNo { get; set; }
        public string DocDate { get; set; }
        public string AccountCode { get; set; }
        public string SubCode { get; set; }
        public string MemberCode { get; set; }
        public long PointValue { get; set; }
        public string CardCode { get; set; }
        public string Cashier { get; set; }
        public int DiscPct { get; set; }
        public int Disc2Pct { get; set; }
        public string Shift { get; set; }
        public long PaymentAmount { get; set; }
        public long CashAmount { get; set; }
        public long NonCash { get; set; }
        public long TotalValue { get; set; }
        public long ChangeAmount { get; set; }
        public long TotalDisc { get; set; }
        public string CardType { get; set; }
        public long GrossAmount { get; set; }
        public long VoucherAmount { get; set; }
        public long CreditAmount { get; set; }
        public int Control { get; set; }
        public int PrintCount { get; set; }
        public string PeriodCode { get; set; }
        public string RegisterId { get; set; }
        public int ChangedBy { get; set; }
        public string ChangedAt { get; set; }
    }
}
