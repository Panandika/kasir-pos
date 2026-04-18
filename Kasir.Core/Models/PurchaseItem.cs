namespace Kasir.Models
{
    public class PurchaseItem
    {
        public int Id { get; set; }
        public string JournalNo { get; set; }
        public string ProductCode { get; set; }
        public string Remark { get; set; }
        public int Quantity { get; set; }
        public long Value { get; set; }
        public int UnitPrice { get; set; }
        public int DiscPct { get; set; }
        public long DiscValue { get; set; }
        public string Unit { get; set; }

        // Transient
        public string ProductName { get; set; }
    }
}
