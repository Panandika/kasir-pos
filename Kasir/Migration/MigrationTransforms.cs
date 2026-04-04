using System;
using System.Globalization;
using System.Text;

namespace Kasir.Migration
{
    public static class MigrationTransforms
    {
        public static long MoneyToInteger(object value)
        {
            if (value == null || value == DBNull.Value) return 0;

            double dVal;
            if (value is double)
            {
                dVal = (double)value;
            }
            else if (value is float)
            {
                dVal = (float)value;
            }
            else if (value is decimal)
            {
                dVal = (double)(decimal)value;
            }
            else if (!double.TryParse(value.ToString(), NumberStyles.Any, CultureInfo.InvariantCulture, out dVal))
            {
                return 0;
            }

            return (long)Math.Round(dVal * 100);
        }

        public static int PercentToInteger(object value)
        {
            if (value == null || value == DBNull.Value) return 0;

            double dVal;
            if (value is double)
            {
                dVal = (double)value;
            }
            else if (!double.TryParse(value.ToString(), NumberStyles.Any, CultureInfo.InvariantCulture, out dVal))
            {
                return 0;
            }

            return (int)Math.Round(dVal * 100);
        }

        public static string DateToIso(object value)
        {
            if (value == null || value == DBNull.Value) return "";

            if (value is DateTime)
            {
                return ((DateTime)value).ToString("yyyy-MM-dd");
            }

            string s = value.ToString().Trim();
            if (string.IsNullOrEmpty(s)) return "";

            DateTime dt;
            // Try common FoxPro date formats
            string[] formats = new[]
            {
                "yyyy-MM-dd", "yyyyMMdd", "MM/dd/yyyy", "dd/MM/yyyy",
                "MM-dd-yyyy", "dd-MM-yyyy", "MM/dd/yy", "dd/MM/yy"
            };

            if (DateTime.TryParseExact(s, formats, CultureInfo.InvariantCulture,
                DateTimeStyles.None, out dt))
            {
                return dt.ToString("yyyy-MM-dd");
            }

            // Last resort
            if (DateTime.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.None, out dt))
            {
                return dt.ToString("yyyy-MM-dd");
            }

            return "";
        }

        public static string TrimDosString(object value)
        {
            if (value == null || value == DBNull.Value) return "";
            return value.ToString().Trim();
        }

        public static string ConvertDosEncoding(byte[] bytes)
        {
            if (bytes == null || bytes.Length == 0) return "";
            var dosEncoding = Encoding.GetEncoding(437);
            return dosEncoding.GetString(bytes).Trim();
        }

        public static string PeriodFromFilename(string filename)
        {
            // acc_0126.dbf → extract MMYY → convert to YYYYMM
            // Pattern: prefix_MMYY.dbf
            if (string.IsNullOrEmpty(filename)) return "";

            int underscoreIdx = filename.LastIndexOf('_');
            int dotIdx = filename.LastIndexOf('.');
            if (underscoreIdx < 0 || dotIdx < 0 || dotIdx <= underscoreIdx) return "";

            string mmyy = filename.Substring(underscoreIdx + 1, dotIdx - underscoreIdx - 1);
            if (mmyy.Length != 4) return "";

            string mm = mmyy.Substring(0, 2);
            string yy = mmyy.Substring(2, 2);

            int year;
            if (!int.TryParse(yy, out year)) return "";
            year += (year >= 90) ? 1900 : 2000; // 90-99 → 1990s, 00-89 → 2000s

            int month;
            if (!int.TryParse(mm, out month) || month < 1 || month > 12) return "";

            return year.ToString() + month.ToString("D2");
        }

        public static string MapStatusCode(object value, string defaultStatus)
        {
            string s = TrimDosString(value);
            if (string.IsNullOrEmpty(s)) return defaultStatus;

            // Common FoxPro status mappings
            switch (s.ToUpper())
            {
                case "A": return "A"; // Active
                case "I": return "I"; // Inactive
                case "D": return "I"; // Deleted → Inactive
                case "Y": return "Y";
                case "N": return "N";
                case "T": return "Y"; // True → Yes
                case "F": return "N"; // False → No
                default: return s;
            }
        }

        public static int MapControl(object value)
        {
            // FoxPro status → control code
            string s = TrimDosString(value);
            switch (s.ToUpper())
            {
                case "": return 1;
                case "N": return 1; // Normal
                case "P": return 2; // Posted/Printed
                case "D": return 3; // Deleted
                case "E": return 4; // Edited
                case "R": return 5; // Replaced
                default:
                    int result;
                    if (int.TryParse(s, out result)) return result;
                    return 1;
            }
        }
    }
}
