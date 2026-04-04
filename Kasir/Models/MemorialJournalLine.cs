namespace Kasir.Models
{
    public class MemorialJournalLine
    {
        public int Id { get; set; }
        public string JournalNo { get; set; }
        public string SubCode { get; set; }
        public string AccountCode { get; set; }
        public string ProductCode { get; set; }
        public string AltSub { get; set; }
        public string Remark { get; set; }
        public string Name { get; set; }
        public int Quantity { get; set; }
        public int UnitPrice { get; set; }
        public string Direction { get; set; }
        public long Value { get; set; }
        public string GroupCode { get; set; }
        public long Roll { get; set; }
        public int StickerCount { get; set; }
        public int MaxSticker { get; set; }
    }
}
