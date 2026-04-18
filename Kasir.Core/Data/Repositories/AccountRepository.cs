using System.Collections.Generic;
using Microsoft.Data.Sqlite;
using Kasir.Models;

namespace Kasir.Data.Repositories
{
    public class AccountRepository
    {
        private readonly SqliteConnection _db;

        public AccountRepository(SqliteConnection db)
        {
            _db = db;
        }

        public int Insert(Account account)
        {
            SqlHelper.ExecuteNonQuery(_db,
                @"INSERT INTO accounts (account_code, account_name, parent_code, is_detail,
                  level, account_group, normal_balance, verify_flag, changed_by, changed_at)
                  VALUES (@code, @name, @parent, @detail, @level, @group, @normal, @vf,
                  @changedBy, datetime('now','localtime'))",
                SqlHelper.Param("@code", account.AccountCode),
                SqlHelper.Param("@name", account.AccountName),
                SqlHelper.Param("@parent", account.ParentCode ?? ""),
                SqlHelper.Param("@detail", account.IsDetail),
                SqlHelper.Param("@level", account.Level),
                SqlHelper.Param("@group", account.AccountGroup),
                SqlHelper.Param("@normal", account.NormalBalance ?? "D"),
                SqlHelper.Param("@vf", account.VerifyFlag ?? ""),
                SqlHelper.Param("@changedBy", account.ChangedBy));

            return (int)SqlHelper.LastInsertRowId(_db);
        }

        public void Update(Account account)
        {
            SqlHelper.ExecuteNonQuery(_db,
                @"UPDATE accounts SET account_name = @name, parent_code = @parent,
                  is_detail = @detail, level = @level, account_group = @group,
                  normal_balance = @normal, verify_flag = @vf,
                  changed_by = @changedBy, changed_at = datetime('now','localtime')
                  WHERE account_code = @code",
                SqlHelper.Param("@code", account.AccountCode),
                SqlHelper.Param("@name", account.AccountName),
                SqlHelper.Param("@parent", account.ParentCode ?? ""),
                SqlHelper.Param("@detail", account.IsDetail),
                SqlHelper.Param("@level", account.Level),
                SqlHelper.Param("@group", account.AccountGroup),
                SqlHelper.Param("@normal", account.NormalBalance ?? "D"),
                SqlHelper.Param("@vf", account.VerifyFlag ?? ""),
                SqlHelper.Param("@changedBy", account.ChangedBy));
        }

        public Account GetByCode(string accountCode)
        {
            return SqlHelper.QuerySingle(_db,
                "SELECT * FROM accounts WHERE account_code = @code",
                MapAccount, SqlHelper.Param("@code", accountCode));
        }

        public List<Account> GetAll()
        {
            return SqlHelper.Query(_db,
                "SELECT * FROM accounts ORDER BY account_code",
                MapAccount);
        }

        public List<Account> GetByGroup(int accountGroup)
        {
            return SqlHelper.Query(_db,
                "SELECT * FROM accounts WHERE account_group = @group ORDER BY account_code",
                MapAccount, SqlHelper.Param("@group", accountGroup));
        }

        public List<Account> GetDetailAccounts()
        {
            return SqlHelper.Query(_db,
                "SELECT * FROM accounts WHERE is_detail = 1 ORDER BY account_code",
                MapAccount);
        }

        public List<Account> GetChildren(string parentCode)
        {
            return SqlHelper.Query(_db,
                "SELECT * FROM accounts WHERE parent_code = @parent ORDER BY account_code",
                MapAccount, SqlHelper.Param("@parent", parentCode));
        }

        public List<Account> Search(string keyword)
        {
            return SqlHelper.Query(_db,
                @"SELECT * FROM accounts
                  WHERE account_code LIKE @kw OR account_name LIKE @kw
                  ORDER BY account_code",
                MapAccount, SqlHelper.Param("@kw", "%" + keyword + "%"));
        }

        private static Account MapAccount(SqliteDataReader r)
        {
            return new Account
            {
                Id = SqlHelper.GetInt(r, "id"),
                AccountCode = SqlHelper.GetString(r, "account_code"),
                AccountName = SqlHelper.GetString(r, "account_name"),
                ParentCode = SqlHelper.GetString(r, "parent_code"),
                IsDetail = SqlHelper.GetInt(r, "is_detail"),
                Level = SqlHelper.GetInt(r, "level"),
                AccountGroup = SqlHelper.GetInt(r, "account_group"),
                NormalBalance = SqlHelper.GetString(r, "normal_balance"),
                VerifyFlag = SqlHelper.GetString(r, "verify_flag"),
                ChangedBy = SqlHelper.GetInt(r, "changed_by"),
                ChangedAt = SqlHelper.GetString(r, "changed_at")
            };
        }
    }
}
