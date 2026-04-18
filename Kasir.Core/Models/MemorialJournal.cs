namespace Kasir.Models
{
    public class MemorialJournal
    {
        public int Id { get; set; }
        public string DocType { get; set; }
        public string JournalNo { get; set; }
        public string DocDate { get; set; }
        public string Ref { get; set; }
        public string RefNo { get; set; }
        public string Remark { get; set; }
        public string GroupCode { get; set; }
        public int Control { get; set; }
        public int PrintCount { get; set; }
        public int ApprovedBy { get; set; }
        public string PeriodCode { get; set; }
        public string RegisterId { get; set; }
        public int ChangedBy { get; set; }
        public string ChangedAt { get; set; }
    }
}
