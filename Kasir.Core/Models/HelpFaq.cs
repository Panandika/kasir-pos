namespace Kasir.Models
{
    public class HelpFaq
    {
        public int Id { get; set; }
        public string DocPath { get; set; }
        public string Anchor { get; set; }
        public string Title { get; set; }
        public string Content { get; set; }
        public string Tags { get; set; }
        public string UpdatedAt { get; set; }
    }

    public class HelpFaqHit
    {
        public int Id { get; set; }
        public string Title { get; set; }
        public string Content { get; set; }
        public string DocPath { get; set; }
        public string Anchor { get; set; }
        public double Score { get; set; }
    }
}
