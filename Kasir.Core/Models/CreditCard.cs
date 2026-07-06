namespace Kasir.Models
{
    public class CreditCard
    {
        public int Id { get; set; }
        public string CardCode { get; set; }
        public string Name { get; set; }
        public string AccountCode { get; set; }
        public int FeePct { get; set; }
        public int MinValue { get; set; }
        public string CardType { get; set; } = "C";  // D=debit, C=credit, Q=QRIS
        public int ChangedBy { get; set; }
        public string ChangedAt { get; set; }
    }
}
