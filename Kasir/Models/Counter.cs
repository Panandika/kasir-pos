namespace Kasir.Models
{
    public class Counter
    {
        public int Id { get; set; }
        public string Prefix { get; set; }
        public string RegisterId { get; set; }
        public int CurrentValue { get; set; }
        public string Format { get; set; }
    }
}
