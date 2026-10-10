using System;
using System.Globalization;
using System.Text.RegularExpressions;

namespace Kasir.CloudSync.Mappers
{
    // Centralised date parsing for the SQLite -> Postgres mirror. Legacy rows
    // migrated from FoxPro may contain several formats; we allow-list the ones
    // we know about and return NULL (with a warning signal via the out param)
    // for anything else. Silent bad-parse is the worst failure mode here —
    // prefer NULL to garbage.
    //
    // Time zone: the POS writes wall-clock text with datetime('now','localtime')
    // on registers that run on store time: WITA (Asia/Makassar, UTC+08:00, no DST,
    // D26), set by StoreTimeZone. A value with no offset is therefore store time,
    // not UTC. Treating it as UTC (the old AssumeUniversal) put every mirrored
    // timestamp hours late, which breaks the dashboard's exact-time opname rule
    // (D11). A value that carries its own offset ('Z' or '+08:00', e.g. from a
    // snapshot round trip) keeps it.
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

        // Trailing 'Z' or a numeric offset after a time part (date-only shapes never
        // end in one: "2026-04-25" has no sign followed by four digits at the end).
        private static readonly Regex ExplicitOffset =
            new Regex(@"(?:[Zz]|[+-]\d{2}:?\d{2})$", RegexOptions.CultureInvariant);

        // Returns the parsed instant as a UTC DateTimeOffset, or null if the value is
        // null, empty, whitespace, or does not match any allowed format. warning is
        // true iff the input was non-empty but unparseable — callers log these
        // to _sync_warnings in Postgres for manual review.
        public static DateTimeOffset? TryParseIso(string raw, out bool warning)
        {
            warning = false;
            if (string.IsNullOrWhiteSpace(raw)) return null;

            if (!DateTimeOffset.TryParseExact(
                    raw,
                    AllowedFormats,
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.AssumeUniversal,
                    out var parsed))
            {
                warning = true;
                return null;
            }

            if (HasExplicitOffset(raw)) return parsed.ToUniversalTime();

            // No offset in the text: the wall clock is store time.
            return StoreTimeZone.FromWallClock(parsed.DateTime).ToUniversalTime();
        }

        internal static bool HasExplicitOffset(string raw)
        {
            string s = raw.Trim();
            // Only look past the time part: a bare date has no offset.
            return s.Length > 10 && s.IndexOf(':') >= 0 && ExplicitOffset.IsMatch(s);
        }
    }
}
