namespace Kasir.Models
{
    public class StockAdjustment
    {
        public int Id { get; set; }
        public string DocType { get; set; }
        public string JournalNo { get; set; }
        public string DocDate { get; set; }
        public string LocationCode { get; set; }
        public string Remark { get; set; }
        public int Control { get; set; }
        public string PeriodCode { get; set; }
        public string RegisterId { get; set; }
        public int ChangedBy { get; set; }
        public string ChangedAt { get; set; }
    }

    public class StockAdjustmentItem
    {
        public int Id { get; set; }
        public string JournalNo { get; set; }
        public string ProductCode { get; set; }
        public int Quantity { get; set; }
        public long CostPrice { get; set; }
        public long Value { get; set; }
        public string Reason { get; set; }

        // Transient
        public string ProductName { get; set; }
        public string DocDate { get; set; }
        public string DocType { get; set; }
        public string LegacySource { get; set; }

        // Quantity scale: opname lines keep the ledger qty (x100, StockQty), from the POS
        // opname and the dashboard pull alike, and the FoxPro OTDTL import ('SM') is
        // x100 too. POS stock-outs (usage/damage/loss) hold plain units.
        public bool IsLedgerQty
        {
            get { return DocType == "OPNAME" || LegacySource == "SM"; }
        }
    }

    public class OpnameReportRow
    {
        public string ProductCode { get; set; }
        public string ProductName { get; set; }
        public int QtySystem { get; set; }
        public int QtyActual { get; set; }
        public long CostPrice { get; set; }
        public string DocDate { get; set; }
        public int Variance { get { return QtyActual - QtySystem; } }
        public long VarianceValue { get { return Variance * CostPrice; } }
    }
}
