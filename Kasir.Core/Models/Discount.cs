namespace Kasir.Models
{
    public class Discount
    {
        public int Id { get; set; }
        public string ProductCode { get; set; }
        public string DeptCode { get; set; }
        public string SubCode { get; set; }
        public int DiscPct { get; set; }
        public int Disc2Pct { get; set; }
        public string DateStart { get; set; }
        public string DateEnd { get; set; }
        public int MinQty { get; set; }
        public int MaxQty { get; set; }
        public long PriceOverride { get; set; }
        public string Description { get; set; }
        public int Priority { get; set; }
        public int IsActive { get; set; }
        public string TimeStart { get; set; }
        public string TimeEnd { get; set; }
    }

    public class DiscountResult
    {
        public int DiscPct { get; set; }
        public int Disc2Pct { get; set; }
        public long DiscAmount { get; set; }
        public string Source { get; set; }

        public static DiscountResult None
        {
            get { return new DiscountResult { Source = "none" }; }
        }

        public long CalculateDiscount(long lineTotal)
        {
            if (DiscAmount > 0)
            {
                return DiscAmount;
            }

            long discount = 0;

            // Apply first discount percentage
            if (DiscPct > 0)
            {
                discount = lineTotal * DiscPct / 10000;
            }

            // Apply second cascading discount on the remainder
            if (Disc2Pct > 0)
            {
                long afterFirst = lineTotal - discount;
                discount += afterFirst * Disc2Pct / 10000;
            }

            return discount;
        }
    }
}
