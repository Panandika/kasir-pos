using System;
using System.Data.SQLite;
using Kasir.Utils;

namespace Kasir.Data.Repositories
{
    public class CounterRepository
    {
        private readonly SQLiteConnection _db;
        private readonly IClock _clock;

        public CounterRepository(SQLiteConnection db) : this(db, null)
        {
        }

        public CounterRepository(SQLiteConnection db, IClock clock)
        {
            _db = db;
            _clock = clock ?? new ClockImpl();
        }

        public string GetNext(string prefix, string registerId)
        {
            // Atomic: BEGIN IMMEDIATE ensures exclusive write lock
            using (var txn = _db.BeginTransaction(System.Data.IsolationLevel.Serializable))
            {
                try
                {
                    // Try to get existing counter
                    int currentValue = 0;
                    string format = null;

                    using (var cmd = new SQLiteCommand(
                        "SELECT current_value, format FROM counters WHERE prefix = @prefix AND register_id = @reg",
                        _db))
                    {
                        cmd.Parameters.AddWithValue("@prefix", prefix);
                        cmd.Parameters.AddWithValue("@reg", registerId);

                        using (var reader = cmd.ExecuteReader())
                        {
                            if (reader.Read())
                            {
                                currentValue = reader.GetInt32(0);
                                format = reader.IsDBNull(1) ? null : reader.GetString(1);
                            }
                            else
                            {
                                // Create counter if it doesn't exist
                                SqlHelper.ExecuteNonQuery(_db,
                                    "INSERT INTO counters (prefix, register_id, current_value) VALUES (@prefix, @reg, 0)",
                                    SqlHelper.Param("@prefix", prefix),
                                    SqlHelper.Param("@reg", registerId));
                            }
                        }
                    }

                    // Increment
                    int nextValue = currentValue + 1;

                    SqlHelper.ExecuteNonQuery(_db,
                        "UPDATE counters SET current_value = @val WHERE prefix = @prefix AND register_id = @reg",
                        SqlHelper.Param("@val", nextValue),
                        SqlHelper.Param("@prefix", prefix),
                        SqlHelper.Param("@reg", registerId));

                    txn.Commit();

                    // Format the number
                    if (!string.IsNullOrEmpty(format))
                    {
                        return FormatNumber(format, prefix, registerId, nextValue);
                    }

                    // Default format: PREFIX-REG-YYMM-SEQ
                    return string.Format("{0}-{1}-{2}-{3}",
                        prefix,
                        registerId,
                        _clock.Now.ToString("yyMM"),
                        nextValue.ToString("D4"));
                }
                catch
                {
                    txn.Rollback();
                    throw;
                }
            }
        }

        public void Reset(string prefix, string registerId)
        {
            SqlHelper.ExecuteNonQuery(_db,
                "UPDATE counters SET current_value = 0 WHERE prefix = @prefix AND register_id = @reg",
                SqlHelper.Param("@prefix", prefix),
                SqlHelper.Param("@reg", registerId));
        }

        private static string FormatNumber(string format, string prefix, string registerId, int seq)
        {
            // Format string supports: {prefix}, {REG}, {YYMM}, {SEQ:04d}
            string result = format;
            result = result.Replace("{prefix}", prefix);
            result = result.Replace("{REG}", registerId);
            result = result.Replace("{YYMM}", _clock.Now.ToString("yyMM"));

            // Handle {SEQ:XXd} pattern
            int seqStart = result.IndexOf("{SEQ:", StringComparison.Ordinal);
            if (seqStart >= 0)
            {
                int seqEnd = result.IndexOf("}", seqStart);
                if (seqEnd > seqStart)
                {
                    string seqFormat = result.Substring(seqStart + 5, seqEnd - seqStart - 5);
                    // Parse "04d" → pad to 4 digits
                    int padWidth = 4;
                    string digits = seqFormat.Replace("d", "");
                    if (digits.StartsWith("0") && digits.Length > 1)
                    {
                        int.TryParse(digits.Substring(1), out padWidth);
                    }
                    else
                    {
                        int.TryParse(digits, out padWidth);
                    }

                    if (padWidth < 1) padWidth = 4;

                    string seqStr = seq.ToString("D" + padWidth);
                    result = result.Substring(0, seqStart) + seqStr + result.Substring(seqEnd + 1);
                }
            }
            else if (result.Contains("{SEQ}"))
            {
                result = result.Replace("{SEQ}", seq.ToString("D4"));
            }

            return result;
        }
    }
}
