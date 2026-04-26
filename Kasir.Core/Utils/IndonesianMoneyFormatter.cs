#nullable enable
using System.Globalization;
using System.Text;

namespace Kasir.Utils
{
    /// <summary>
    /// Helpers for the Indonesian thousands-separator format used by money
    /// inputs ("1250000" → "1.250.000"). Display values are whole Rupiah; the
    /// caller multiplies by 100 to get INTEGER cents per the project pattern.
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
        /// Reformats <paramref name="text"/> while preserving the caret's
        /// distance from the right end of the string. Returns the formatted
        /// text and the new caret index.
        /// </summary>
        /// <param name="text">Current TextBox text.</param>
        /// <param name="caretIndex">Current caret index (0..text.Length).</param>
        public static (string Formatted, int CaretIndex) ReformatPreserveCaret(string? text, int caretIndex)
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

            string formatted = FormatText(original);
            if (formatted.Length == 0) return ("", 0);

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
            return (formatted, newCaret);
        }
    }
}
