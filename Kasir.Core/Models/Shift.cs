namespace Kasir.Models
{
    public class Shift
    {
        public int Id { get; set; }
        public string RegisterId { get; set; }
        public string ShiftNumber { get; set; }
        public int CashierId { get; set; }
        public string OpenedAt { get; set; }
        public string ClosedAt { get; set; }
        public long OpeningCash { get; set; }
        public long ClosingCash { get; set; }
        public long ExpectedCash { get; set; }
        public string Status { get; set; }
    }
}
