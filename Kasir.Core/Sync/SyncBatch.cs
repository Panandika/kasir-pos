using System.Collections.Generic;

namespace Kasir.Sync
{
    public class SyncBatch
    {
        public string RegisterId { get; set; }
        public int SchemaVersion { get; set; }
        public string Timestamp { get; set; }
        public string BatchId { get; set; }
        public string Signature { get; set; }
        public List<SyncEvent> Events { get; set; }

        public SyncBatch()
        {
            Events = new List<SyncEvent>();
        }
    }

    public class SyncEvent
    {
        public int QueueId { get; set; }
        public string TableName { get; set; }
        public string RecordKey { get; set; }
        public string Operation { get; set; }
        public Dictionary<string, object> Data { get; set; }

        public SyncEvent()
        {
            Data = new Dictionary<string, object>();
        }
    }
}
