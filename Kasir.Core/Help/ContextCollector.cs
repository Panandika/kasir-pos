using Newtonsoft.Json.Linq;

namespace Kasir.Help
{
    /// <summary>
    /// Auto-attached context payload for Bantuan tickets. v1 fields:
    /// register_id, store_id, app_version, last_invoice, last_error.
    /// All string values pass through PiiScrubber before serialization.
    /// </summary>
    public class ContextCollector
    {
        private readonly PiiScrubber _scrubber;

        public ContextCollector(PiiScrubber scrubber)
        {
            _scrubber = scrubber;
        }

        public string BuildAttachmentsJson(
            string storeId,
            string registerId,
            string appVersion,
            string lastInvoice,
            string lastError)
        {
            var attachments = new JObject
            {
                ["store_id"] = storeId,
                ["register_id"] = registerId,
                ["app_version"] = appVersion,
                ["last_invoice"] = lastInvoice ?? "",
                ["last_error"] = lastError ?? ""
            };

            var scrubbed = _scrubber.ScrubJson(attachments);
            return scrubbed.ToString(Newtonsoft.Json.Formatting.None);
        }
    }
}
