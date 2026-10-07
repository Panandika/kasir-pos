using System.Collections.Generic;
using System.Linq;

namespace Kasir.CloudSync.Generation
{
    // Declarative description of a SQLite -> Postgres mirror table. The single
    // source of truth that drives both the upsert SQL generator and the value
    // conversion path. Adding a new mapped table = adding one TableMapping
    // instance, no per-table mapper class.
    public class TableMapping
    {
        public string TableName { get; }
        public IReadOnlyList<ColumnMapping> Columns { get; }

        // False for mirror-only tables: shipped to Supabase for the dashboard but
        // never written into a register snapshot (SnapshotBuilder skips them).
        public bool RestoreToRegister { get; }

        public TableMapping(string tableName, IReadOnlyList<ColumnMapping> columns, bool restoreToRegister = true)
        {
            TableName = tableName;
            Columns = columns;
            RestoreToRegister = restoreToRegister;
        }

        public IReadOnlyList<string> PrimaryKeyColumns =>
            Columns.Where(c => c.IsPrimaryKey).Select(c => c.Name).ToList();

        public IReadOnlyList<string> NonKeyColumns =>
            Columns.Where(c => !c.IsPrimaryKey).Select(c => c.Name).ToList();
    }
}
