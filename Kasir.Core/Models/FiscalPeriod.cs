namespace Kasir.Models
{
    public class FiscalPeriod
    {
        public int Id { get; set; }
        public string PeriodCode { get; set; }
        public int Year { get; set; }
        public int Month { get; set; }
        public string Status { get; set; }
        public string OpenedAt { get; set; }
        public string ClosedAt { get; set; }
    }
}
