using Microsoft.Data.Sqlite;
using Kasir.Models;

namespace Kasir.Data.Repositories
{
    public class MemberRepository
    {
        private readonly SqliteConnection _db;

        public MemberRepository(SqliteConnection db)
        {
            _db = db;
        }

        public Member GetByCode(string memberCode)
        {
            return SqlHelper.QuerySingle(_db,
                "SELECT * FROM members WHERE member_code = @code",
                MapMember,
                SqlHelper.Param("@code", memberCode));
        }

        public void UpdatePoints(string memberCode, int pointsDelta)
        {
            SqlHelper.ExecuteNonQuery(_db,
                @"UPDATE members SET point_balance = point_balance + @delta
                  WHERE member_code = @code",
                SqlHelper.Param("@delta", pointsDelta),
                SqlHelper.Param("@code", memberCode));
        }

        private static Member MapMember(SqliteDataReader reader)
        {
            return new Member
            {
                Id = SqlHelper.GetInt(reader, "id"),
                MemberCode = SqlHelper.GetString(reader, "member_code"),
                Name = SqlHelper.GetString(reader, "name"),
                Address = SqlHelper.GetString(reader, "address"),
                City = SqlHelper.GetString(reader, "city"),
                Phone = SqlHelper.GetString(reader, "phone"),
                OpeningBalance = SqlHelper.GetLong(reader, "opening_balance"),
                DebitTotal = SqlHelper.GetLong(reader, "debit_total"),
                CreditTotal = SqlHelper.GetLong(reader, "credit_total"),
                PointBalance = SqlHelper.GetInt(reader, "point_balance"),
                GroupCode = SqlHelper.GetString(reader, "group_code"),
                Status = SqlHelper.GetString(reader, "status")
            };
        }
    }
}
