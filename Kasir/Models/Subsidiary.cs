namespace Kasir.Models
{
    public class Subsidiary
    {
        public int Id { get; set; }
        public string SubCode { get; set; }
        public string Name { get; set; }
        public string AccountCode { get; set; }
        public string ContactPerson { get; set; }
        public long CreditLimit { get; set; }
        public string Address { get; set; }
        public string City { get; set; }
        public string Country { get; set; }
        public string Npwp { get; set; }
        public string Phone { get; set; }
        public string Fax { get; set; }
        public string GroupCode { get; set; }
        public int DiscPct { get; set; }
        public string Status { get; set; }
        public string BankName { get; set; }
        public string BankAccountNo { get; set; }
        public int ChangedBy { get; set; }
        public string ChangedAt { get; set; }
    }
}
