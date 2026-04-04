namespace Kasir.Models
{
    public class ProductBarcode
    {
        public int Id { get; set; }
        public string ProductCode { get; set; }
        public string Barcode { get; set; }
        public string ProductName { get; set; }
        public int QtyPerScan { get; set; }
        public int PriceOverride { get; set; }
        public string CustomerCode { get; set; }
    }
}
