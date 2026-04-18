namespace Kasir.Models
{
    public class Order
    {
        public int Id { get; set; }
        public string DocType { get; set; }
        public string JournalNo { get; set; }
        public string DocDate { get; set; }
        public string AccountCode { get; set; }
        public string SubCode { get; set; }
        public string RefNo { get; set; }
        public string Remark { get; set; }
        public string Warehouse { get; set; }
        public int DiscPct { get; set; }
        public long TotalValue { get; set; }
        public string DueDate { get; set; }
        public int Control { get; set; }
        public string PeriodCode { get; set; }
        public string RegisterId { get; set; }
        public int ChangedBy { get; set; }
        public string ChangedAt { get; set; }
    }
}
