namespace Kasir.Models
{
    public class SyncQueueEntry
    {
        public int Id { get; set; }
        public string RegisterId { get; set; }
        public string TableName { get; set; }
        public string RecordKey { get; set; }
        public string Operation { get; set; }
        public string Payload { get; set; }
        public string CreatedAt { get; set; }
        public string SyncedAt { get; set; }
        public string Status { get; set; }
        public int RetryCount { get; set; }
        public string LastError { get; set; }

        // Cloud sync bookkeeping (Phase 6). Independent of LAN Status/SyncedAt.
        // 0 = not yet mirrored to cloud; 1 = mirrored.
        public int CloudSynced { get; set; }
        public string CloudSyncedAt { get; set; }
    }
}
