using System;
using System.Collections.Generic;

namespace Kasir.CloudSync
{
    // The store's time zone (D26): WITA, Asia/Makassar, UTC+08:00, no DST.
    //
    // The POS writes wall-clock text with datetime('now','localtime') on registers
    // whose clock runs on store time, so a timestamp with no offset is store time.
    // CloudSync turns those into instants (DateParser) and turns dashboard instants
    // back into register wall-clock text (PosRequestApplier). Both read the offset
    // here, so the zone is set in one place: CloudSync:StoreTimeZone (appsettings,
    // KASIR_CLOUDSYNC_CloudSync__StoreTimeZone, or --CloudSync:StoreTimeZone),
    // applied once at startup. Keep it in step with the dashboard's
    // public.store_tz() and VITE_STORE_TZ.
    //
    // A fixed offset rather than a TimeZoneInfo kept around: Indonesia has no DST,
    // and the zone ids differ between Windows and Unix.
    public static class StoreTimeZone
    {
        public const string DefaultId = "Asia/Makassar";

        // Indonesian zones by IANA id (and the Windows ids the hub may report).
        private static readonly Dictionary<string, TimeSpan> Known =
            new Dictionary<string, TimeSpan>(StringComparer.OrdinalIgnoreCase)
            {
                ["Asia/Jakarta"] = TimeSpan.FromHours(7),
                ["Asia/Pontianak"] = TimeSpan.FromHours(7),
                ["SE Asia Standard Time"] = TimeSpan.FromHours(7),
                ["Asia/Makassar"] = TimeSpan.FromHours(8),
                ["Singapore Standard Time"] = TimeSpan.FromHours(8),
                ["Asia/Jayapura"] = TimeSpan.FromHours(9),
                ["Tokyo Standard Time"] = TimeSpan.FromHours(9),
            };

        private static readonly object Gate = new object();
        private static string _id = DefaultId;
        private static TimeSpan _offset = TimeSpan.FromHours(8);

        public static string Id
        {
            get { lock (Gate) return _id; }
        }

        public static TimeSpan Offset
        {
            get { lock (Gate) return _offset; }
        }

        // Resolve a zone id to its fixed UTC offset. Accepts the Indonesian ids above,
        // or any id the OS knows that has no DST. False for blank, unknown or DST zones.
        public static bool TryResolve(string id, out TimeSpan offset)
        {
            offset = default;
            if (string.IsNullOrWhiteSpace(id)) return false;
            string key = id.Trim();
            if (Known.TryGetValue(key, out offset)) return true;
            try
            {
                var tz = TimeZoneInfo.FindSystemTimeZoneById(key);
                if (tz.SupportsDaylightSavingTime) return false;
                offset = tz.BaseUtcOffset;
                return true;
            }
            catch (TimeZoneNotFoundException)
            {
                return false;
            }
            catch (InvalidTimeZoneException)
            {
                return false;
            }
        }

        // Set the store zone. Blank keeps the default (Asia/Makassar). Throws on an
        // id that cannot be resolved, so a typo fails the start instead of shifting
        // every timestamp.
        public static void Configure(string id)
        {
            if (string.IsNullOrWhiteSpace(id))
            {
                Set(DefaultId, TimeSpan.FromHours(8));
                return;
            }
            if (!TryResolve(id, out var offset))
                throw new ArgumentException(
                    "CloudSync:StoreTimeZone '" + id + "' is not a known time zone without DST (e.g. Asia/Makassar)",
                    nameof(id));
            Set(id.Trim(), offset);
        }

        // Back to the default; for tests that change the zone.
        public static void Reset() => Set(DefaultId, TimeSpan.FromHours(8));

        // Store wall clock of an instant, as an Unspecified DateTime (what the POS writes).
        public static DateTime WallClock(DateTimeOffset at) =>
            DateTime.SpecifyKind(at.ToOffset(Offset).DateTime, DateTimeKind.Unspecified);

        // The instant of a store wall-clock time.
        public static DateTimeOffset FromWallClock(DateTime wallClock) =>
            new DateTimeOffset(DateTime.SpecifyKind(wallClock, DateTimeKind.Unspecified), Offset);

        private static void Set(string id, TimeSpan offset)
        {
            lock (Gate)
            {
                _id = id;
                _offset = offset;
            }
        }
    }
}
