using System;
using System.Collections.Generic;
using System.Globalization;
using Microsoft.Data.Sqlite;
using Kasir.Models;

namespace Kasir.Data.Repositories
{
    public class ShiftRepository
    {
        private readonly SqliteConnection _db;

        public ShiftRepository(SqliteConnection db)
        {
            _db = db;
        }

        /// <summary>
        /// Inserts a shift row using the ShiftNumber already assigned on the entity.
        /// Public for testability; production callers must use <see cref="OpenShiftAtomic"/>
        /// so NextShiftNumber + INSERT run under a single BEGIN IMMEDIATE transaction.
        /// </summary>
        public int OpenShift(Shift shift)
        {
            SqlHelper.ExecuteNonQuery(_db,
                @"INSERT INTO shifts (register_id, shift_number, cashier_id, opened_at, opening_cash, status)
                  VALUES (@reg, @shift, @cashier, @opened, @cash, 'O')",
                SqlHelper.Param("@reg", shift.RegisterId),
                SqlHelper.Param("@shift", shift.ShiftNumber),
                SqlHelper.Param("@cashier", shift.CashierId),
                SqlHelper.Param("@opened", shift.OpenedAt),
                SqlHelper.Param("@cash", shift.OpeningCash));

            return (int)SqlHelper.LastInsertRowId(_db);
        }

        /// <summary>
        /// Allocates the next shift_number and inserts the shift in a single
        /// BEGIN IMMEDIATE transaction so concurrent opens cannot collide on the
        /// same shift_number. Mirrors the <c>CounterRepository.GetNext</c> pattern.
        /// On rollback, <paramref name="shift"/>.ShiftNumber is restored to its
        /// pre-call value (dirty-state guard).
        /// </summary>
        public int OpenShiftAtomic(Shift shift, string dateYmd)
        {
            string originalShiftNumber = shift.ShiftNumber;
            bool joinExisting = false;
            SqliteTransaction txn = null;
            try
            {
                try
                {
                    txn = (SqliteTransaction)_db.BeginTransaction(System.Data.IsolationLevel.Serializable);
                }
                catch (InvalidOperationException)
                {
                    joinExisting = true;
                }

                shift.ShiftNumber = NextShiftNumber(shift.RegisterId, dateYmd);
                int id = OpenShift(shift);

                if (!joinExisting && txn != null)
                {
                    txn.Commit();
                }
                return id;
            }
            catch
            {
                shift.ShiftNumber = originalShiftNumber;
                if (!joinExisting && txn != null)
                {
                    try { txn.Rollback(); } catch { }
                }
                throw;
            }
            finally
            {
                if (txn != null) txn.Dispose();
            }
        }

        public long CloseShift(int id, long closingCash, long expectedCash)
        {
            long variance = closingCash - expectedCash;
            SqlHelper.ExecuteNonQuery(_db,
                @"UPDATE shifts SET status = 'C', closed_at = datetime('now','localtime'),
                  closing_cash = @closing, expected_cash = @expected, cash_variance = @variance
                  WHERE id = @id",
                SqlHelper.Param("@closing", closingCash),
                SqlHelper.Param("@expected", expectedCash),
                SqlHelper.Param("@variance", variance),
                SqlHelper.Param("@id", id));
            return variance;
        }

        public Shift GetOpenShift(string registerId)
        {
            return SqlHelper.QuerySingle(_db,
                "SELECT * FROM shifts WHERE register_id = @reg AND status = 'O' ORDER BY id DESC LIMIT 1",
                MapShift,
                SqlHelper.Param("@reg", registerId));
        }

        public Shift GetById(int id)
        {
            return SqlHelper.QuerySingle(_db,
                "SELECT * FROM shifts WHERE id = @id",
                MapShift,
                SqlHelper.Param("@id", id));
        }

        public List<Shift> GetByDateRange(string dateFrom, string dateTo)
        {
            string endExclusive = NextDayMidnight(dateTo);
            return SqlHelper.Query(_db,
                @"SELECT * FROM shifts
                  WHERE opened_at >= @from AND opened_at < @to
                  ORDER BY opened_at DESC",
                MapShift,
                SqlHelper.Param("@from", dateFrom),
                SqlHelper.Param("@to", endExclusive));
        }

        /// <summary>
        /// Returns the next shift_number for the given register on the given date
        /// (yyyy-MM-dd). Counts shifts opened on that date and returns count+1.
        /// Public for testability; production callers must use <see cref="OpenShiftAtomic"/>
        /// so this read is paired with the INSERT under a single transaction.
        /// </summary>
        public string NextShiftNumber(string registerId, string dateYmd)
        {
            string endExclusive = NextDayMidnight(dateYmd);
            long n = SqlHelper.ExecuteScalar<long>(_db,
                @"SELECT COUNT(*) FROM shifts
                  WHERE register_id = @reg
                    AND opened_at >= @start
                    AND opened_at <  @end",
                SqlHelper.Param("@reg", registerId),
                SqlHelper.Param("@start", dateYmd + " 00:00:00"),
                SqlHelper.Param("@end", endExclusive));
            return ((int)n + 1).ToString();
        }

        private static string NextDayMidnight(string dateYmd)
        {
            var d = DateTime.ParseExact(dateYmd, "yyyy-MM-dd", CultureInfo.InvariantCulture);
            return d.AddDays(1).ToString("yyyy-MM-dd") + " 00:00:00";
        }

        private static Shift MapShift(SqliteDataReader reader)
        {
            return new Shift
            {
                Id = SqlHelper.GetInt(reader, "id"),
                RegisterId = SqlHelper.GetString(reader, "register_id"),
                ShiftNumber = SqlHelper.GetString(reader, "shift_number"),
                CashierId = SqlHelper.GetInt(reader, "cashier_id"),
                OpenedAt = SqlHelper.GetString(reader, "opened_at"),
                ClosedAt = SqlHelper.GetString(reader, "closed_at"),
                OpeningCash = SqlHelper.GetLong(reader, "opening_cash"),
                ClosingCash = SqlHelper.GetLong(reader, "closing_cash"),
                ExpectedCash = SqlHelper.GetLong(reader, "expected_cash"),
                CashVariance = SqlHelper.GetNullableLong(reader, "cash_variance"),
                Status = SqlHelper.GetString(reader, "status")
            };
        }
    }
}
