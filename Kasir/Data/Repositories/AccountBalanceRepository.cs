using System.Collections.Generic;
using System.Data.SQLite;
using Kasir.Models;

namespace Kasir.Data.Repositories
{
    public class AccountBalanceRepository
    {
        private readonly SQLiteConnection _db;

        public AccountBalanceRepository(SQLiteConnection db)
        {
            _db = db;
        }

        public void EnsureBalance(string accountCode, string periodCode)
        {
            SqlHelper.ExecuteNonQuery(_db,
                @"INSERT OR IGNORE INTO account_balances (account_code, period_code,
                  opening_balance, debit_total, credit_total)
                  VALUES (@acc, @period, 0, 0, 0)",
                SqlHelper.Param("@acc", accountCode),
                SqlHelper.Param("@period", periodCode));
        }

        public void AddDebit(string accountCode, string periodCode, long amount)
        {
            EnsureBalance(accountCode, periodCode);
            SqlHelper.ExecuteNonQuery(_db,
                @"UPDATE account_balances SET debit_total = debit_total + @amt
                  WHERE account_code = @acc AND period_code = @period",
                SqlHelper.Param("@amt", amount),
                SqlHelper.Param("@acc", accountCode),
                SqlHelper.Param("@period", periodCode));
        }

        public void AddCredit(string accountCode, string periodCode, long amount)
        {
            EnsureBalance(accountCode, periodCode);
            SqlHelper.ExecuteNonQuery(_db,
                @"UPDATE account_balances SET credit_total = credit_total + @amt
                  WHERE account_code = @acc AND period_code = @period",
                SqlHelper.Param("@amt", amount),
                SqlHelper.Param("@acc", accountCode),
                SqlHelper.Param("@period", periodCode));
        }

        public AccountBalance GetBalance(string accountCode, string periodCode)
        {
            return SqlHelper.QuerySingle(_db,
                @"SELECT * FROM account_balances
                  WHERE account_code = @acc AND period_code = @period",
                MapBalance,
                SqlHelper.Param("@acc", accountCode),
                SqlHelper.Param("@period", periodCode));
        }

        public List<AccountBalance> GetAllForPeriod(string periodCode)
        {
            return SqlHelper.Query(_db,
                @"SELECT * FROM account_balances
                  WHERE period_code = @period ORDER BY account_code",
                MapBalance, SqlHelper.Param("@period", periodCode));
        }

        public void SetOpeningBalance(string accountCode, string periodCode, long amount)
        {
            EnsureBalance(accountCode, periodCode);
            SqlHelper.ExecuteNonQuery(_db,
                @"UPDATE account_balances SET opening_balance = @amt
                  WHERE account_code = @acc AND period_code = @period",
                SqlHelper.Param("@amt", amount),
                SqlHelper.Param("@acc", accountCode),
                SqlHelper.Param("@period", periodCode));
        }

        public void CarryForward(string fromPeriod, string toPeriod)
        {
            var balances = GetAllForPeriod(fromPeriod);
            foreach (var bal in balances)
            {
                long closing = bal.OpeningBalance + bal.DebitTotal - bal.CreditTotal;
                SetOpeningBalance(bal.AccountCode, toPeriod, closing);
            }
        }

        private static AccountBalance MapBalance(SQLiteDataReader r)
        {
            return new AccountBalance
            {
                Id = SqlHelper.GetInt(r, "id"),
                AccountCode = SqlHelper.GetString(r, "account_code"),
                PeriodCode = SqlHelper.GetString(r, "period_code"),
                OpeningBalance = SqlHelper.GetLong(r, "opening_balance"),
                DebitTotal = SqlHelper.GetLong(r, "debit_total"),
                CreditTotal = SqlHelper.GetLong(r, "credit_total"),
                Flag = SqlHelper.GetString(r, "flag")
            };
        }
    }
}
