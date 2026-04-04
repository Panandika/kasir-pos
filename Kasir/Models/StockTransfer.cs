namespace Kasir.Models
{
    public class StockTransfer
    {
        public int Id { get; set; }
        public string DocType { get; set; }
        public string JournalNo { get; set; }
        public string DocDate { get; set; }
        public string FromLocation { get; set; }
        public string ToLocation { get; set; }
        public string Remark { get; set; }
        public int Control { get; set; }
        public string PeriodCode { get; set; }
        public string RegisterId { get; set; }
        public int ChangedBy { get; set; }
        public string ChangedAt { get; set; }
    }

    public class StockTransferItem
    {
        public int Id { get; set; }
        public string JournalNo { get; set; }
        public string ProductCode { get; set; }
        public int Quantity { get; set; }
        public int CostPrice { get; set; }
        public long Value { get; set; }

        // Transient
        public string ProductName { get; set; }
    }
}
