namespace Kasir.Models
{
    public class GiroEntry
    {
        public int Id { get; set; }
        public string AccountCode { get; set; }
        public string SubCode { get; set; }
        public string GiroNo { get; set; }
        public string GiroDate { get; set; }
        public string DocDate { get; set; }
        public string JournalNo { get; set; }
        public long Value { get; set; }
        public string Remark { get; set; }
        public string Direction { get; set; }
        public string Status { get; set; }
        public int ApprovedBy { get; set; }
        public int Control { get; set; }
        public int PrintCount { get; set; }
        public string PeriodCode { get; set; }
        public int ChangedBy { get; set; }
        public string ChangedAt { get; set; }
    }
}
