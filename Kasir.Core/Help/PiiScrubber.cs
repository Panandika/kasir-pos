using System.Collections.Generic;
using System.Text.RegularExpressions;
using Newtonsoft.Json.Linq;

namespace Kasir.Help
{
    /// <summary>
    /// Strips potential PII before help_tickets attachments leave the register.
    /// Operates recursively across JSON trees so nested fields (last_error,
    /// future activity_log_30m entries) are covered, not just top-level keys.
    /// </summary>
    public class PiiScrubber
    {
        // 13–19 digit PAN-like sequences. Allow optional spaces or dashes between.
        private static readonly Regex PanRe = new Regex(
            @"\b(?:\d[ -]?){12,18}\d\b",
            RegexOptions.Compiled);

        // Rupiah amounts: Rp 100.000  or Rp100,000  or RP 1000
        private static readonly Regex RupiahRe = new Regex(
            @"\bRp\s?[\d.,]+",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        // 4–6 digit standalone runs (PIN-like). Word-boundary on both sides.
        // Apply AFTER PAN match (longer pattern) so we don't double-redact.
        private static readonly Regex PinRe = new Regex(
            @"\b\d{4,6}\b",
            RegexOptions.Compiled);

        public int ReplacementCount { get; private set; }

        public string Scrub(string s)
        {
            if (string.IsNullOrEmpty(s)) return s;
            int before = ReplacementCount;
            string result = s;

            result = PanRe.Replace(result, _ => { ReplacementCount++; return "[redacted-pan]"; });
            result = RupiahRe.Replace(result, _ => { ReplacementCount++; return "[redacted-rp]"; });
            result = PinRe.Replace(result, _ => { ReplacementCount++; return "[redacted-num]"; });

            return result;
        }

        /// <summary>
        /// Scrub all string values inside a JSON tree in place. Returns the
        /// modified token (same instance unless root was a primitive). Replacement
        /// count accumulates across the call.
        /// </summary>
        public JToken ScrubJson(JToken token)
        {
            if (token == null) return null;
            switch (token.Type)
            {
                case JTokenType.String:
                    string before = (string)token;
                    string after = Scrub(before);
                    if (!ReferenceEquals(before, after) && before != after)
                    {
                        return new JValue(after);
                    }
                    return token;
                case JTokenType.Object:
                    var obj = (JObject)token;
                    foreach (var prop in obj.Properties())
                    {
                        prop.Value = ScrubJson(prop.Value);
                    }
                    return obj;
                case JTokenType.Array:
                    var arr = (JArray)token;
                    for (int i = 0; i < arr.Count; i++)
                    {
                        arr[i] = ScrubJson(arr[i]);
                    }
                    return arr;
                default:
                    return token;
            }
        }

        /// <summary>
        /// Convenience: scrub a JSON-serialized payload string and return scrubbed JSON.
        /// </summary>
        public string ScrubJsonString(string json)
        {
            var token = JToken.Parse(json);
            var scrubbed = ScrubJson(token);
            return scrubbed.ToString(Newtonsoft.Json.Formatting.None);
        }
    }
}
