namespace Kasir.Models
{
    public class Member
    {
        public int Id { get; set; }
        public string MemberCode { get; set; }
        public string Name { get; set; }
        public string Address { get; set; }
        public string City { get; set; }
        public string Phone { get; set; }
        public long OpeningBalance { get; set; }
        public long DebitTotal { get; set; }
        public long CreditTotal { get; set; }
        public int PointBalance { get; set; }
        public string GroupCode { get; set; }
        public string Status { get; set; }
    }
}
