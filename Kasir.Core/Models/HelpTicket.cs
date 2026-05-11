namespace Kasir.Models
{
    public class HelpTicket
    {
        public int Id { get; set; }
        public string TicketNo { get; set; }
        public string StoreId { get; set; }
        public string RegisterId { get; set; }
        public string CashierId { get; set; }
        public string Category { get; set; }
        public string Body { get; set; }
        public string AttachmentsJson { get; set; }
        public string Status { get; set; }
        public string ClientCreatedAt { get; set; }
        public string SentAt { get; set; }
        public int SyncAttempts { get; set; }
        public string LastError { get; set; }
    }
}
