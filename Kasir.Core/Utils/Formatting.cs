using System;
using System.Globalization;

namespace Kasir.Utils
{
    public static class Formatting
    {
        private static readonly CultureInfo Indonesian = new CultureInfo("id-ID");

        public static string FormatCurrency(long amountCents)
        {
            long whole = amountCents / 100;
            return string.Format("Rp {0}", whole.ToString("N0", Indonesian));
        }

        public static string FormatCurrencyShort(long amountCents)
        {
            long whole = amountCents / 100;
            return whole.ToString("N0", Indonesian);
        }

        public static string FormatMoney(long amountCents)
        {
            return FormatCurrencyShort(amountCents);
        }

        public static string FormatDate(string isoDate)
        {
            if (string.IsNullOrEmpty(isoDate))
            {
                return "";
            }

            DateTime dt;
            if (DateTime.TryParseExact(isoDate, "yyyy-MM-dd",
                CultureInfo.InvariantCulture, DateTimeStyles.None, out dt))
            {
                return dt.ToString("dd-MM-yyyy");
            }

            return isoDate;
        }

        public static string FormatDateTime(DateTime dt)
        {
            return dt.ToString("dd-MM-yyyy HH:mm");
        }

        public static string FormatTime(DateTime dt)
        {
            return dt.ToString("HH:mm:ss");
        }

        public static string NowIso()
        {
            return DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
        }

        public static string TodayIso()
        {
            return DateTime.Now.ToString("yyyy-MM-dd");
        }

        // Parse Indonesian-formatted rupiah input (strips dots, commas, "Rp", whitespace).
        // Returns true if a non-negative integer rupiah amount could be parsed.
        public static bool TryParseRupiah(string? text, out long rupiah)
        {
            rupiah = 0;
            if (string.IsNullOrWhiteSpace(text)) return false;
            var cleaned = text
                .Replace("Rp", "", StringComparison.OrdinalIgnoreCase)
                .Replace(".", "")
                .Replace(",", "")
                .Replace(" ", "")
                .Trim();
            return long.TryParse(cleaned, NumberStyles.Integer, CultureInfo.InvariantCulture, out rupiah);
        }

        // Format an integer rupiah amount (NOT cents) for InputDialog prefill: "100.000".
        public static string FormatRupiahInput(long rupiah)
        {
            return rupiah.ToString("N0", Indonesian);
        }

        public static string CurrentPeriod()
        {
            return DateTime.Now.ToString("yyyyMM");
        }
    }
}
