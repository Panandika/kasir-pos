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

        public static string CurrentPeriod()
        {
            return DateTime.Now.ToString("yyyyMM");
        }
    }
}
