namespace Kasir.Models
{
    public class PayablesEntry
    {
        public int Id { get; set; }
        public string SubCode { get; set; }
        public string JournalNo { get; set; }
        public string DocDate { get; set; }
        public string DueDate { get; set; }
        public string Direction { get; set; }
        public long GrossAmount { get; set; }
        public long Amount { get; set; }
        public long PaymentAmount { get; set; }
        public string IsPaid { get; set; }
        public int Control { get; set; }
        public string PeriodCode { get; set; }
        public int ChangedBy { get; set; }
        public string ChangedAt { get; set; }
    }
}
