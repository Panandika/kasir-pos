using Kasir.Services;

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
        // QtySystem / QtyActual (and so Variance) are ledger qty (x100, QLAST N(13,2) x 100);
        // CostPrice is x100 money per whole unit. StockQty.Value divides the qty scale back
        // out, as RecordStockIn / RecordStockOut do; a plain product is 100x too large.
        public int Variance { get { return QtyActual - QtySystem; } }
        public long VarianceValue
        {
            get
            {
                int v = Variance;
                return v >= 0 ? StockQty.Value(CostPrice, v) : -StockQty.Value(CostPrice, -v);
            }
        }
    }
}
