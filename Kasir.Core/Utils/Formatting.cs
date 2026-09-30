using System;
using System.Globalization;
using System.Linq;

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
        // Parses a whole-rupiah amount ("100.000", "Rp 50.000"). Input with sen ("11.208,67")
        // is rejected rather than misread: use TryParseRupiahCents for fields that allow sen.
        public static bool TryParseRupiah(string? text, out long rupiah)
        {
            rupiah = 0;
            if (!TryParseRupiahCents(text, out long cents) || cents % 100 != 0) return false;
            rupiah = cents / 100;
            return true;
        }

        // Parses Indonesian money input into cents (x 100):
        //   "." = thousands separator, "," = decimal separator with at most 2 digits
        //   ("11.208,67" -> 1120867). A lone comma followed by exactly 3 digits and no dots
        //   ("50,000", "1,250,000") is read as a thousands separator, the common habit.
        // Anything ambiguous ("11.5", "1,2345", "1.20.000") is rejected, never guessed.
        public static bool TryParseRupiahCents(string? text, out long cents)
        {
            cents = 0;
            if (string.IsNullOrWhiteSpace(text)) return false;
            string s = text.Replace("Rp", "", StringComparison.OrdinalIgnoreCase).Replace(" ", "").Trim();
            bool negative = s.StartsWith("-");
            if (negative) s = s.Substring(1);
            if (s.Length == 0) return false;

            string intPart = s;
            string fracPart = "";
            string[] commaParts = s.Split(',');
            if (commaParts.Length > 1)
            {
                bool thousandsCommas = !s.Contains('.')
                    && commaParts[0].Length >= 1 && commaParts[0].Length <= 3
                    && commaParts.Skip(1).All(g => g.Length == 3);
                if (thousandsCommas)
                {
                    intPart = string.Concat(commaParts);
                }
                else if (commaParts.Length == 2 && commaParts[1].Length >= 1 && commaParts[1].Length <= 2)
                {
                    intPart = commaParts[0];
                    fracPart = commaParts[1];
                }
                else
                {
                    return false;
                }
            }

            string[] dotGroups = intPart.Split('.');
            if (dotGroups.Length > 1)
            {
                if (dotGroups[0].Length < 1 || dotGroups[0].Length > 3) return false;
                if (dotGroups.Skip(1).Any(g => g.Length != 3)) return false;
                intPart = string.Concat(dotGroups);
            }

            if (intPart.Length == 0 || !intPart.All(char.IsDigit) || !fracPart.All(char.IsDigit)) return false;
            if (!long.TryParse(intPart, NumberStyles.None, CultureInfo.InvariantCulture, out long whole)) return false;
            long frac = fracPart.Length == 0 ? 0 : long.Parse(fracPart.PadRight(2, '0'), CultureInfo.InvariantCulture);
            try
            {
                cents = checked(whole * 100 + frac);
            }
            catch (OverflowException)
            {
                return false;
            }
            if (negative) cents = -cents;
            return true;
        }

        // Formats cents for an input prefill: "11.208,67", or "11.208" when there are no sen.
        public static string FormatRupiahCentsInput(long cents)
        {
            long whole = Math.Abs(cents) / 100;
            long sen = Math.Abs(cents) % 100;
            string text = whole.ToString("N0", Indonesian) + (sen == 0 ? "" : "," + sen.ToString("00", CultureInfo.InvariantCulture));
            return cents < 0 ? "-" + text : text;
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
