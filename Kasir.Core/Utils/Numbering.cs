using System.Data.SQLite;
using Kasir.Data.Repositories;

namespace Kasir.Utils
{
    public static class Numbering
    {
        public static string GetNextNumber(string prefix, string registerId, SQLiteConnection db)
        {
            var repo = new CounterRepository(db);
            return repo.GetNext(prefix, registerId);
        }
    }
}
