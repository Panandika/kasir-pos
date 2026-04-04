namespace Kasir.Models
{
    public class SaleItem
    {
        public int Id { get; set; }
        public string JournalNo { get; set; }
        public string ProductCode { get; set; }
        public string Remark { get; set; }
        public int Quantity { get; set; }
        public long Value { get; set; }
        public long Cogs { get; set; }
        public int DiscPct { get; set; }
        public int UnitPrice { get; set; }
        public int PointValue { get; set; }
        public long DiscValue { get; set; }

        // Transient (not persisted, used during sale entry)
        public string ProductName { get; set; }
    }
}
