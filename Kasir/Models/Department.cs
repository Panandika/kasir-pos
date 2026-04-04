namespace Kasir.Models
{
    public class Department
    {
        public int Id { get; set; }
        public string DeptCode { get; set; }
        public string Name { get; set; }
        public int ChangedBy { get; set; }
        public string ChangedAt { get; set; }
    }
}
