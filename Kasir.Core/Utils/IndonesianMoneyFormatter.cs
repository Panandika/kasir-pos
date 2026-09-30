#nullable enable
using System.Globalization;
using System.Text;

namespace Kasir.Utils
{
    /// <summary>
    /// Helpers for the Indonesian thousands-separator format used by money
    /// inputs ("1250000" → "1.250.000"). FormatText is whole Rupiah; FormatTextWithDecimals
    /// keeps a ",dd" sen part for price fields (parse those with Formatting.TryParseRupiahCents).
    /// </summary>
    public static class IndonesianMoneyFormatter
    {
        private static readonly CultureInfo Indonesian = new CultureInfo("id-ID");

        /// <summary>
        /// Returns the digits-only contents of <paramref name="text"/>, dropping
        /// any thousands separators or stray characters.
        /// </summary>
        public static string DigitsOnly(string? text)
        {
            if (string.IsNullOrEmpty(text)) return "";
            var sb = new StringBuilder(text.Length);
            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];
                if (c >= '0' && c <= '9') sb.Append(c);
            }
            return sb.ToString();
        }

        /// <summary>
        /// Returns true if <paramref name="text"/> is non-empty and contains
        /// only ASCII digits 0-9.
        /// </summary>
        public static bool IsDigitsOnly(string? text)
        {
            if (string.IsNullOrEmpty(text)) return false;
            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];
                if (c < '0' || c > '9') return false;
            }
            return true;
        }

        /// <summary>
        /// Formats a non-negative whole-Rupiah value as Indonesian thousands
        /// (e.g. 1250000 → "1.250.000"). Negative values are formatted with a
        /// leading "-".
        /// </summary>
        public static string Format(long wholeRupiah)
        {
            return wholeRupiah.ToString("N0", Indonesian);
        }

        /// <summary>
        /// Formats free text by stripping non-digits and applying the thousands
        /// separator. Empty input returns "".
        /// </summary>
        public static string FormatText(string? text)
        {
            string digits = DigitsOnly(text);
            if (digits.Length == 0) return "";
            // Trim leading zeros so "00070000" → "70.000"; preserve a single "0".
            int i = 0;
            while (i < digits.Length - 1 && digits[i] == '0') i++;
            digits = digits.Substring(i);
            if (!long.TryParse(digits, NumberStyles.None, CultureInfo.InvariantCulture, out long v))
                return digits;
            return Format(v);
        }

        /// <summary>
        /// Like <see cref="FormatText"/> but keeps a decimal part after the last comma
        /// (at most 2 digits): "11208,67" → "11.208,67". For fields that allow sen.
        /// </summary>
        public static string FormatTextWithDecimals(string? text)
        {
            string raw = text ?? "";
            if (raw.IndexOf(',') < 0) return FormatText(raw);
            if (Formatting.IsCommaThousands(raw)) return FormatText(raw); // "50,000" = fifty thousand

            // Exactly one comma with 0-2 sen digits: format the whole part, keep the sen.
            // Anything else (a second comma, 3+ sen digits, stray characters) is left exactly
            // as typed: never drop or move a digit; the parser rejects it on save.
            int comma = raw.IndexOf(',');
            if (raw.IndexOf(',', comma + 1) >= 0) return raw;
            string whole = raw.Substring(0, comma);
            string frac = raw.Substring(comma + 1);
            if (frac.Length > 2 || !IsDigitsOnly(frac) && frac.Length > 0) return raw;
            foreach (char c in whole)
                if (!(c >= '0' && c <= '9') && c != '.') return raw;

            string formattedWhole = FormatText(whole);
            return (formattedWhole.Length == 0 ? "0" : formattedWhole) + "," + frac;
        }

        /// <summary>
        /// Reformats <paramref name="text"/> while preserving the caret's
        /// distance from the right end of the string. Returns the formatted
        /// text and the new caret index.
        /// </summary>
        /// <param name="text">Current TextBox text.</param>
        /// <param name="caretIndex">Current caret index (0..text.Length).</param>
        public static (string Formatted, int CaretIndex) ReformatPreserveCaret(string? text, int caretIndex, bool allowDecimals = false)
        {
            string original = text ?? "";
            if (caretIndex < 0) caretIndex = 0;
            if (caretIndex > original.Length) caretIndex = original.Length;

            // Count digits to the right of the caret in the original string —
            // that's the anchor we preserve across reformatting.
            int digitsRight = 0;
            for (int i = caretIndex; i < original.Length; i++)
            {
                char c = original[i];
                if (c >= '0' && c <= '9') digitsRight++;
            }

            string formatted = allowDecimals ? FormatTextWithDecimals(original) : FormatText(original);
            if (formatted.Length == 0) return ("", 0);

            // With sen, anchor the caret on the comma: left of it, keep the digit count up to
            // the comma; right of it, keep the offset from the comma. Otherwise an edit in the
            // whole part would push the caret into the sen.
            int oc = original.IndexOf(',');
            int fc = formatted.IndexOf(',');
            if (allowDecimals && oc >= 0 && fc >= 0)
            {
                if (caretIndex > oc)
                    return (formatted, System.Math.Min(formatted.Length, fc + (caretIndex - oc)));
                int digitsBeforeComma = 0;
                for (int i = caretIndex; i < oc; i++)
                    if (original[i] >= '0' && original[i] <= '9') digitsBeforeComma++;
                return (formatted, CaretForDigitsRight(formatted.Substring(0, fc), digitsBeforeComma));
            }

            return (formatted, CaretForDigitsRight(formatted, digitsRight));
        }

        // Caret index in <paramref name="formatted"/> with <paramref name="digitsRight"/>
        // digits to its right.
        private static int CaretForDigitsRight(string formatted, int digitsRight)
        {
            // Walk back from the right of the formatted string until we have
            // counted the same number of digits to the right of the new caret.
            int seen = 0;
            int newCaret = formatted.Length;
            for (int i = formatted.Length - 1; i >= 0; i--)
            {
                if (seen >= digitsRight) { newCaret = i + 1; break; }
                char c = formatted[i];
                if (c >= '0' && c <= '9') seen++;
                if (seen >= digitsRight) { newCaret = i; break; }
            }
            if (digitsRight == 0) newCaret = formatted.Length;
            if (newCaret < 0) newCaret = 0;
            if (newCaret > formatted.Length) newCaret = formatted.Length;
            return newCaret;
        }
    }
}
