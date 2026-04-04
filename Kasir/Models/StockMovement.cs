namespace Kasir.Models
{
    public class StockMovement
    {
        public int Id { get; set; }
        public string ProductCode { get; set; }
        public string VendorCode { get; set; }
        public string DeptCode { get; set; }
        public string LocationCode { get; set; }
        public string AccountCode { get; set; }
        public string SubCode { get; set; }
        public string JournalNo { get; set; }
        public string MovementType { get; set; }
        public string DocDate { get; set; }
        public string PeriodCode { get; set; }
        public int QtyIn { get; set; }
        public int QtyOut { get; set; }
        public long ValIn { get; set; }
        public long ValOut { get; set; }
        public int CostPrice { get; set; }
        public int IsPosted { get; set; }
        public int ChangedBy { get; set; }
        public string ChangedAt { get; set; }
    }
}
