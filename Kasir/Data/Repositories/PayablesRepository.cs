using System.Collections.Generic;
using System.Data.SQLite;
using Kasir.Models;

namespace Kasir.Data.Repositories
{
    public class PayablesRepository
    {
        private readonly SQLiteConnection _db;

        public PayablesRepository(SQLiteConnection db)
        {
            _db = db;
        }

        public int Insert(PayablesEntry entry)
        {
            SqlHelper.ExecuteNonQuery(_db,
                @"INSERT INTO payables_register (sub_code, journal_no, doc_date, due_date,
                  direction, gross_amount, amount, payment_amount, is_paid, control,
                  period_code, changed_by, changed_at)
                  VALUES (@sub, @jnl, @date, @due, @dir, @gross, @amount, @paid, 'N', @control,
                  @period, @changedBy, datetime('now','localtime'))",
                SqlHelper.Param("@sub", entry.SubCode),
                SqlHelper.Param("@jnl", entry.JournalNo),
                SqlHelper.Param("@date", entry.DocDate),
                SqlHelper.Param("@due", entry.DueDate ?? ""),
                SqlHelper.Param("@dir", entry.Direction ?? "D"),
                SqlHelper.Param("@gross", entry.GrossAmount),
                SqlHelper.Param("@amount", entry.Amount),
                SqlHelper.Param("@paid", entry.PaymentAmount),
                SqlHelper.Param("@control", entry.Control),
                SqlHelper.Param("@period", entry.PeriodCode),
                SqlHelper.Param("@changedBy", entry.ChangedBy));

            return (int)_db.LastInsertRowId;
        }

        public List<PayablesEntry> GetUnpaidByVendor(string vendorCode)
        {
            return SqlHelper.Query(_db,
                @"SELECT * FROM payables_register
                  WHERE sub_code = @sub AND is_paid = 'N' AND control != 3
                  ORDER BY due_date ASC",
                MapEntry, SqlHelper.Param("@sub", vendorCode));
        }

        public PayablesEntry GetByJournalNo(string journalNo)
        {
            return SqlHelper.QuerySingle(_db,
                "SELECT * FROM payables_register WHERE journal_no = @jnl",
                MapEntry, SqlHelper.Param("@jnl", journalNo));
        }

        public long GetTotalUnpaidByVendor(string vendorCode)
        {
            return SqlHelper.ExecuteScalar<long>(_db,
                @"SELECT COALESCE(SUM(amount - payment_amount), 0) FROM payables_register
                  WHERE sub_code = @sub AND is_paid = 'N' AND control != 3",
                SqlHelper.Param("@sub", vendorCode));
        }

        public void RecordPayment(string journalNo, long paymentAmount)
        {
            SqlHelper.ExecuteNonQuery(_db,
                @"UPDATE payables_register SET payment_amount = payment_amount + @paid,
                  is_paid = CASE WHEN payment_amount + @paid >= amount THEN 'Y' ELSE 'N' END,
                  changed_at = datetime('now','localtime')
                  WHERE journal_no = @jnl",
                SqlHelper.Param("@paid", paymentAmount),
                SqlHelper.Param("@jnl", journalNo));
        }

        private static PayablesEntry MapEntry(SQLiteDataReader r)
        {
            return new PayablesEntry
            {
                Id = SqlHelper.GetInt(r, "id"),
                SubCode = SqlHelper.GetString(r, "sub_code"),
                JournalNo = SqlHelper.GetString(r, "journal_no"),
                DocDate = SqlHelper.GetString(r, "doc_date"),
                DueDate = SqlHelper.GetString(r, "due_date"),
                Direction = SqlHelper.GetString(r, "direction"),
                GrossAmount = SqlHelper.GetLong(r, "gross_amount"),
                Amount = SqlHelper.GetLong(r, "amount"),
                PaymentAmount = SqlHelper.GetLong(r, "payment_amount"),
                IsPaid = SqlHelper.GetString(r, "is_paid"),
                Control = SqlHelper.GetInt(r, "control"),
                PeriodCode = SqlHelper.GetString(r, "period_code"),
                ChangedBy = SqlHelper.GetInt(r, "changed_by"),
                ChangedAt = SqlHelper.GetString(r, "changed_at")
            };
        }
    }
}
