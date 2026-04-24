using System;
using System.Globalization;

namespace Kasir.CloudSync.Mappers
{
    // Centralised date parsing for the SQLite -> Postgres mirror. Legacy rows
    // migrated from FoxPro may contain several formats; we allow-list the ones
    // we know about and return NULL (with a warning signal via the out param)
    // for anything else. Silent bad-parse is the worst failure mode here —
    // prefer NULL to garbage.
    public static class DateParser
    {
        private static readonly string[] AllowedFormats = new[]
        {
            "yyyy-MM-dd HH:mm:ss",
            "yyyy-MM-dd'T'HH:mm:ss",
            "yyyy-MM-dd'T'HH:mm:ssK",
            "yyyy-MM-dd'T'HH:mm:ss.fff",
            "yyyy-MM-dd'T'HH:mm:ss.fffK",
            "yyyy-MM-dd",
            "yyyyMMdd",
            "yyyyMMdd HH:mm:ss"
        };

        // Returns the parsed UTC DateTimeOffset, or null if the value is null,
        // empty, whitespace, or does not match any allowed format. warning is
        // true iff the input was non-empty but unparseable — callers log these
        // to _sync_warnings in Postgres for manual review.
        public static DateTimeOffset? TryParseIso(string raw, out bool warning)
        {
            warning = false;
            if (string.IsNullOrWhiteSpace(raw)) return null;

            if (DateTimeOffset.TryParseExact(
                    raw,
                    AllowedFormats,
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                    out var parsed))
            {
                return parsed;
            }

            warning = true;
            return null;
        }
    }
}
