namespace Kasir.Models
{
    public class AccountBalance
    {
        public int Id { get; set; }
        public string AccountCode { get; set; }
        public string PeriodCode { get; set; }
        public long OpeningBalance { get; set; }
        public long DebitTotal { get; set; }
        public long CreditTotal { get; set; }
        public string Flag { get; set; }
    }
}
