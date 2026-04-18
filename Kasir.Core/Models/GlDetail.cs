namespace Kasir.Models
{
    public class GlDetail
    {
        public int Id { get; set; }
        public string AccountCode { get; set; }
        public string SubCode { get; set; }
        public string ProductCode { get; set; }
        public string AltSub { get; set; }
        public string Alias { get; set; }
        public string JournalNo { get; set; }
        public string SalesCode { get; set; }
        public string Remark { get; set; }
        public string VoucherNo { get; set; }
        public string Ref { get; set; }
        public string DocDate { get; set; }
        public long Debit { get; set; }
        public long Credit { get; set; }
        public long QtyIn { get; set; }
        public long QtyOut { get; set; }
        public string PeriodCode { get; set; }
    }
}
