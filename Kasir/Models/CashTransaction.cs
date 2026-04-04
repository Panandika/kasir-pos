namespace Kasir.Models
{
    public class CashTransaction
    {
        public int Id { get; set; }
        public string DocType { get; set; }
        public string JournalNo { get; set; }
        public string DocDate { get; set; }
        public string SubCode { get; set; }
        public string Ref { get; set; }
        public string Remark { get; set; }
        public long TotalValue { get; set; }
        public string IsPosted { get; set; }
        public string GroupCode { get; set; }
        public string Description { get; set; }
        public int Control { get; set; }
        public int PrintCount { get; set; }
        public int ApprovedBy { get; set; }
        public string PeriodCode { get; set; }
        public string RegisterId { get; set; }
        public int ChangedBy { get; set; }
        public string ChangedAt { get; set; }
    }
}
