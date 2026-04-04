using System.Collections.Generic;
using System.Data.SQLite;
using Kasir.Models;

namespace Kasir.Data.Repositories
{
    public class SubsidiaryRepository
    {
        private readonly SQLiteConnection _db;

        public SubsidiaryRepository(SQLiteConnection db)
        {
            _db = db;
        }

        public List<SubsidiaryLookup> GetVendors()
        {
            return SqlHelper.Query(_db,
                "SELECT sub_code, name FROM subsidiaries WHERE group_code = '1' ORDER BY name",
                reader => new SubsidiaryLookup
                {
                    SubCode = SqlHelper.GetString(reader, "sub_code"),
                    Name = SqlHelper.GetString(reader, "name")
                });
        }

        public SubsidiaryLookup GetByCode(string subCode)
        {
            return SqlHelper.QuerySingle(_db,
                "SELECT sub_code, name FROM subsidiaries WHERE sub_code = @code",
                reader => new SubsidiaryLookup
                {
                    SubCode = SqlHelper.GetString(reader, "sub_code"),
                    Name = SqlHelper.GetString(reader, "name")
                },
                SqlHelper.Param("@code", subCode));
        }
    }

    public class SubsidiaryLookup
    {
        public string SubCode { get; set; }
        public string Name { get; set; }

        public override string ToString()
        {
            return string.Format("{0} — {1}", SubCode, Name);
        }
    }
}
