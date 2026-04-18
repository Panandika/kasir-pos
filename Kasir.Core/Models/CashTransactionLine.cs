namespace Kasir.Models
{
    public class CashTransactionLine
    {
        public int Id { get; set; }
        public string JournalNo { get; set; }
        public string SubCode { get; set; }
        public string AccountCode { get; set; }
        public string RefNo { get; set; }
        public string Remark { get; set; }
        public string GiroNo { get; set; }
        public string GiroDate { get; set; }
        public string GiroStatus { get; set; }
        public string Direction { get; set; }
        public long Value { get; set; }
        public string LinkJournal { get; set; }
    }
}
