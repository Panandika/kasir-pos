using Microsoft.Data.Sqlite;

namespace Kasir.Data.Migrations
{
    public interface IMigration
    {
        int Version { get; }
        string Description { get; }
        void Up(SqliteConnection db);
    }
}
