namespace Kasir.Models
{
    public class Account
    {
        public int Id { get; set; }
        public string AccountCode { get; set; }
        public string AccountName { get; set; }
        public string ParentCode { get; set; }
        public int IsDetail { get; set; }
        public int Level { get; set; }
        public int AccountGroup { get; set; }
        public string NormalBalance { get; set; }
        public string VerifyFlag { get; set; }
        public int ChangedBy { get; set; }
        public string ChangedAt { get; set; }
    }
}
