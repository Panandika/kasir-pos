using System.Collections.Generic;
using Microsoft.Data.Sqlite;
using Kasir.Models;

namespace Kasir.Data.Repositories
{
    public class CashTransactionRepository
    {
        private readonly SqliteConnection _db;

        public CashTransactionRepository(SqliteConnection db)
        {
            _db = db;
        }

        public int Insert(CashTransaction txn, List<CashTransactionLine> lines)
        {
            using (var dbTxn = _db.BeginTransaction())
            {
                try
                {
                    SqlHelper.ExecuteNonQuery(_db,
                        @"INSERT INTO cash_transactions (doc_type, journal_no, doc_date, sub_code,
                          ref, remark, total_value, is_posted, group_code, description, control,
                          period_code, register_id, changed_by, changed_at)
                          VALUES (@type, @jnl, @date, @sub, @ref, @remark, @total, 'N', @group,
                          @desc, @control, @period, @reg, @changedBy, datetime('now','localtime'))",
                        SqlHelper.Param("@type", txn.DocType),
                        SqlHelper.Param("@jnl", txn.JournalNo),
                        SqlHelper.Param("@date", txn.DocDate),
                        SqlHelper.Param("@sub", txn.SubCode ?? ""),
                        SqlHelper.Param("@ref", txn.Ref ?? ""),
                        SqlHelper.Param("@remark", txn.Remark ?? ""),
                        SqlHelper.Param("@total", txn.TotalValue),
                        SqlHelper.Param("@group", txn.GroupCode ?? ""),
                        SqlHelper.Param("@desc", txn.Description ?? ""),
                        SqlHelper.Param("@control", txn.Control),
                        SqlHelper.Param("@period", txn.PeriodCode),
                        SqlHelper.Param("@reg", txn.RegisterId ?? "01"),
                        SqlHelper.Param("@changedBy", txn.ChangedBy));

                    foreach (var line in lines)
                    {
                        SqlHelper.ExecuteNonQuery(_db,
                            @"INSERT INTO cash_transaction_lines (journal_no, sub_code, account_code,
                              ref_no, remark, giro_no, giro_date, giro_status, direction, value,
                              link_journal)
                              VALUES (@jnl, @sub, @acc, @refNo, @remark, @giro, @giroDate,
                              @giroStatus, @dir, @val, @link)",
                            SqlHelper.Param("@jnl", txn.JournalNo),
                            SqlHelper.Param("@sub", line.SubCode ?? ""),
                            SqlHelper.Param("@acc", line.AccountCode),
                            SqlHelper.Param("@refNo", line.RefNo ?? ""),
                            SqlHelper.Param("@remark", line.Remark ?? ""),
                            SqlHelper.Param("@giro", line.GiroNo ?? ""),
                            SqlHelper.Param("@giroDate", line.GiroDate ?? ""),
                            SqlHelper.Param("@giroStatus", line.GiroStatus ?? ""),
                            SqlHelper.Param("@dir", line.Direction),
                            SqlHelper.Param("@val", line.Value),
                            SqlHelper.Param("@link", line.LinkJournal ?? ""));
                    }

                    dbTxn.Commit();
                    return (int)SqlHelper.LastInsertRowId(_db);
                }
                catch { dbTxn.Rollback(); throw; }
            }
        }

        public CashTransaction GetByJournalNo(string journalNo)
        {
            return SqlHelper.QuerySingle(_db,
                "SELECT * FROM cash_transactions WHERE journal_no = @jnl",
                MapTransaction, SqlHelper.Param("@jnl", journalNo));
        }

        public List<CashTransaction> GetByPeriodAndType(string periodCode, string docType)
        {
            return SqlHelper.Query(_db,
                @"SELECT * FROM cash_transactions
                  WHERE period_code = @period AND doc_type = @type AND control != 3
                  ORDER BY journal_no",
                MapTransaction,
                SqlHelper.Param("@period", periodCode),
                SqlHelper.Param("@type", docType));
        }

        public List<CashTransaction> GetUnpostedByPeriod(string periodCode)
        {
            return SqlHelper.Query(_db,
                @"SELECT * FROM cash_transactions
                  WHERE period_code = @period AND is_posted = 'N' AND control != 3
                  ORDER BY journal_no",
                MapTransaction, SqlHelper.Param("@period", periodCode));
        }

        public List<CashTransactionLine> GetLines(string journalNo)
        {
            return SqlHelper.Query(_db,
                "SELECT * FROM cash_transaction_lines WHERE journal_no = @jnl ORDER BY id",
                MapLine, SqlHelper.Param("@jnl", journalNo));
        }

        public void MarkPosted(string journalNo)
        {
            SqlHelper.ExecuteNonQuery(_db,
                "UPDATE cash_transactions SET is_posted = 'Y' WHERE journal_no = @jnl",
                SqlHelper.Param("@jnl", journalNo));
        }

        private static CashTransaction MapTransaction(SqliteDataReader r)
        {
            return new CashTransaction
            {
                Id = SqlHelper.GetInt(r, "id"),
                DocType = SqlHelper.GetString(r, "doc_type"),
                JournalNo = SqlHelper.GetString(r, "journal_no"),
                DocDate = SqlHelper.GetString(r, "doc_date"),
                SubCode = SqlHelper.GetString(r, "sub_code"),
                Ref = SqlHelper.GetString(r, "ref"),
                Remark = SqlHelper.GetString(r, "remark"),
                TotalValue = SqlHelper.GetLong(r, "total_value"),
                IsPosted = SqlHelper.GetString(r, "is_posted"),
                GroupCode = SqlHelper.GetString(r, "group_code"),
                Description = SqlHelper.GetString(r, "description"),
                Control = SqlHelper.GetInt(r, "control"),
                PrintCount = SqlHelper.GetInt(r, "print_count"),
                PeriodCode = SqlHelper.GetString(r, "period_code"),
                RegisterId = SqlHelper.GetString(r, "register_id"),
                ChangedBy = SqlHelper.GetInt(r, "changed_by"),
                ChangedAt = SqlHelper.GetString(r, "changed_at")
            };
        }

        private static CashTransactionLine MapLine(SqliteDataReader r)
        {
            return new CashTransactionLine
            {
                Id = SqlHelper.GetInt(r, "id"),
                JournalNo = SqlHelper.GetString(r, "journal_no"),
                SubCode = SqlHelper.GetString(r, "sub_code"),
                AccountCode = SqlHelper.GetString(r, "account_code"),
                RefNo = SqlHelper.GetString(r, "ref_no"),
                Remark = SqlHelper.GetString(r, "remark"),
                GiroNo = SqlHelper.GetString(r, "giro_no"),
                GiroDate = SqlHelper.GetString(r, "giro_date"),
                GiroStatus = SqlHelper.GetString(r, "giro_status"),
                Direction = SqlHelper.GetString(r, "direction"),
                Value = SqlHelper.GetLong(r, "value"),
                LinkJournal = SqlHelper.GetString(r, "link_journal")
            };
        }
    }
}
