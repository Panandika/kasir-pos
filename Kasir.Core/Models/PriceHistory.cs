namespace Kasir.Models
{
    public class PriceHistory
    {
        public int Id { get; set; }
        public string ProductCode { get; set; }
        public string DocDate { get; set; }
        public int Quantity { get; set; }
        public long Value { get; set; }
        public string OldDate { get; set; }
        public int OldQuantity { get; set; }
        public long OldValue { get; set; }
        public string JournalNo { get; set; }
        public string PeriodCode { get; set; }
    }
}
