using System;
using Microsoft.Data.Sqlite;

namespace Kasir.Help
{
    /// <summary>
    /// Generates collision-free ticket numbers across registers and days.
    /// Format: TKT-{store_short}-{reg2}-{yymmdd}-{seq4}
    /// e.g. TKT-SM-01-260507-0001
    ///
    /// Reuses the existing `counters` table (per CounterRepository) so the
    /// per-register sequence persists across restarts.
    /// </summary>
    public class TicketNumberGenerator
    {
        public const string CounterPrefix = "TKT";

        private readonly SqliteConnection _db;
        private readonly string _storeShort;
        private readonly string _registerId;
        private readonly Func<DateTime> _now;

        public TicketNumberGenerator(
            SqliteConnection db, string storeShort, string registerId, Func<DateTime> now = null)
        {
            _db = db;
            _storeShort = storeShort;
            _registerId = registerId;
            _now = now ?? (() => DateTime.Now);
        }

        public string Next()
        {
            int seq = AdvanceCounter();
            return string.Format(
                "TKT-{0}-{1}-{2}-{3}",
                _storeShort,
                _registerId.PadLeft(2, '0'),
                _now().ToString("yyMMdd"),
                seq.ToString("D4"));
        }

        private int AdvanceCounter()
        {
            // Get-and-increment in a transaction, joining any active outer txn.
            bool joined = false;
            SqliteTransaction txn = null;
            try
            {
                try { txn = _db.BeginTransaction(System.Data.IsolationLevel.Serializable); }
                catch (InvalidOperationException) { joined = true; }

                int current = 0;
                using (var cmd = _db.CreateCommand())
                {
                    cmd.CommandText = "SELECT current_value FROM counters WHERE prefix = @p AND register_id = @r";
                    cmd.Parameters.AddWithValue("@p", CounterPrefix);
                    cmd.Parameters.AddWithValue("@r", _registerId);
                    using (var r = cmd.ExecuteReader())
                    {
                        if (r.Read()) current = r.GetInt32(0);
                    }
                }
                if (current == 0)
                {
                    using (var cmd = _db.CreateCommand())
                    {
                        cmd.CommandText = "INSERT OR IGNORE INTO counters (prefix, register_id, current_value) VALUES (@p, @r, 0)";
                        cmd.Parameters.AddWithValue("@p", CounterPrefix);
                        cmd.Parameters.AddWithValue("@r", _registerId);
                        cmd.ExecuteNonQuery();
                    }
                }

                int next = current + 1;
                using (var cmd = _db.CreateCommand())
                {
                    cmd.CommandText = "UPDATE counters SET current_value = @v WHERE prefix = @p AND register_id = @r";
                    cmd.Parameters.AddWithValue("@v", next);
                    cmd.Parameters.AddWithValue("@p", CounterPrefix);
                    cmd.Parameters.AddWithValue("@r", _registerId);
                    cmd.ExecuteNonQuery();
                }

                if (!joined && txn != null) txn.Commit();
                return next;
            }
            catch
            {
                if (!joined && txn != null) try { txn.Rollback(); } catch { }
                throw;
            }
            finally
            {
                txn?.Dispose();
            }
        }
    }
}
