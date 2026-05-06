namespace Kasir.Models
{
    public class Product
    {
        public int Id { get; set; }
        public string ProductCode { get; set; }
        public string AccountCode { get; set; }
        public string DeptCode { get; set; }
        public string Name { get; set; }
        public string Status { get; set; }
        public string Unit { get; set; }
        public string Unit1 { get; set; }
        public long Price { get; set; }
        public long Price1 { get; set; }
        public long Price2 { get; set; }
        public long Price3 { get; set; }
        public long Price4 { get; set; }
        public long BuyingPrice { get; set; }
        public string VendorCode { get; set; }
        public int QtyBreak2 { get; set; }
        public int QtyBreak3 { get; set; }
        public string OpenPrice { get; set; }
        public int DiscPct { get; set; }
        public int MarginPct { get; set; }
        public long CostPrice { get; set; }
        public int QtyMin { get; set; }
        public int QtyMax { get; set; }
        public int QtyOrder { get; set; }
        public string VatFlag { get; set; }
        public string LuxuryTaxFlag { get; set; }
        public string IsConsignment { get; set; }
        public string ProductType { get; set; }
        public int ChangedBy { get; set; }
        public string ChangedAt { get; set; }
    }
}
