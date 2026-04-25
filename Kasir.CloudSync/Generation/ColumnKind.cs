namespace Kasir.CloudSync.Generation
{
    // Maps a SQLite column to its Postgres-side type + value-conversion strategy.
    public enum ColumnKind
    {
        // TEXT in SQLite -> TEXT in Postgres. Direct copy.
        Text,
        // INTEGER (cents, x100) in SQLite -> BIGINT in Postgres. Preserves Rupiah
        // precision above INT32 max.
        BigintMoney,
        // INTEGER (qty, factor, conversion etc.) in SQLite -> BIGINT in Postgres.
        BigintQty,
        // INTEGER (small id, retry_count, etc.) in SQLite -> INTEGER in Postgres.
        Int,
        // TEXT (ISO datetime) in SQLite -> TIMESTAMPTZ in Postgres via DateParser.
        TimestampTz
    }
}
