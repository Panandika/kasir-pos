namespace Kasir.Models
{
    public class Location
    {
        public int Id { get; set; }
        public string LocationCode { get; set; }
        public string Name { get; set; }
        public string Remark { get; set; }
        public int ChangedBy { get; set; }
        public string ChangedAt { get; set; }
    }
}
