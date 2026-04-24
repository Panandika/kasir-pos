using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Npgsql;

namespace Kasir.CloudSync.Capacity
{
    // Capacity reporter for the Phase D health endpoint and the manual
    // monthly check (--capacity-report). Reads pg_database_size and the
    // _capacity_summary view created by Sql/E_capacity_monitoring.sql.
    public class CapacityMonitor
    {
        // Supabase free tier ceiling.
        public const long FreeTierCeilingMb = 500;
        // Alert when projected days-until-ceiling drops below this.
        public const int ProjectedDaysWarning = 90;

        private readonly string _connectionString;

        public CapacityMonitor(string connectionString)
        {
            _connectionString = connectionString;
        }

        public async Task<CapacityReport> SampleAsync(CancellationToken ct)
        {
            var report = new CapacityReport
            {
                SampledAtUtc = DateTimeOffset.UtcNow,
                PerTable = new List<TableCapacity>()
            };

            await using var conn = new NpgsqlConnection(_connectionString);
            await conn.OpenAsync(ct).ConfigureAwait(false);

            await using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = "SELECT pg_database_size(current_database());";
                report.TotalBytes = (long)(await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false));
            }

            await using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = "SELECT table_name, row_count, bytes_on_disk FROM _capacity_summary;";
                await using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
                while (await reader.ReadAsync(ct).ConfigureAwait(false))
                {
                    report.PerTable.Add(new TableCapacity
                    {
                        TableName = reader.GetString(0),
                        RowCount = reader.GetInt64(1),
                        BytesOnDisk = reader.GetInt64(2)
                    });
                }
            }
            return report;
        }

        // Days until the free-tier ceiling, given an observed weekly growth.
        // Returns int.MaxValue if growth is zero or negative.
        public static int ProjectedDaysUntilCeiling(long currentMb, double mbPerWeek)
        {
            if (mbPerWeek <= 0) return int.MaxValue;
            double remainingMb = FreeTierCeilingMb - currentMb;
            if (remainingMb <= 0) return 0;
            double weeks = remainingMb / mbPerWeek;
            return (int)Math.Floor(weeks * 7);
        }

        public static long BytesToMb(long bytes) => bytes / 1024 / 1024;
    }

    public class CapacityReport
    {
        public DateTimeOffset SampledAtUtc { get; set; }
        public long TotalBytes { get; set; }
        public long TotalMb => CapacityMonitor.BytesToMb(TotalBytes);
        public List<TableCapacity> PerTable { get; set; }
    }

    public class TableCapacity
    {
        public string TableName { get; set; }
        public long RowCount { get; set; }
        public long BytesOnDisk { get; set; }
        public long Mb => CapacityMonitor.BytesToMb(BytesOnDisk);
    }
}
