using System.Data.SQLite;

namespace Kasir.Data.Migrations
{
    public interface IMigration
    {
        int Version { get; }
        string Description { get; }
        void Up(SQLiteConnection db);
    }
}
