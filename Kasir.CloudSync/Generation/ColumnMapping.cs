namespace Kasir.CloudSync.Generation
{
    public class ColumnMapping
    {
        public string Name { get; }
        public ColumnKind Kind { get; }
        public bool IsPrimaryKey { get; }

        public ColumnMapping(string name, ColumnKind kind, bool isPrimaryKey = false)
        {
            Name = name;
            Kind = kind;
            IsPrimaryKey = isPrimaryKey;
        }
    }
}
