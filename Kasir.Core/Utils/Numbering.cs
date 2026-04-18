using Microsoft.Data.Sqlite;
using Kasir.Data.Repositories;

namespace Kasir.Utils
{
    public static class Numbering
    {
        public static string GetNextNumber(string prefix, string registerId, SqliteConnection db)
        {
            var repo = new CounterRepository(db);
            return repo.GetNext(prefix, registerId);
        }
    }
}
