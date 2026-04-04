using System.Collections.Generic;

namespace Kasir.Migration
{
    public class TableMigrationResult
    {
        public string TableName { get; set; }
        public int SourceCount { get; set; }
        public int MigratedCount { get; set; }
        public int SkippedCount { get; set; }
        public int ErrorCount { get; set; }
        public List<string> Errors { get; set; }
        public double ElapsedSeconds { get; set; }

        public TableMigrationResult()
        {
            Errors = new List<string>();
        }
    }

    public class MigrationResult
    {
        public List<TableMigrationResult> TableResults { get; set; }
        public int TotalSourceRows { get; set; }
        public int TotalMigrated { get; set; }
        public int TotalSkipped { get; set; }
        public int TotalErrors { get; set; }
        public double TotalElapsedSeconds { get; set; }
        public bool Success { get; set; }

        public MigrationResult()
        {
            TableResults = new List<TableMigrationResult>();
            Success = true;
        }

        public void AddTableResult(TableMigrationResult result)
        {
            TableResults.Add(result);
            TotalSourceRows += result.SourceCount;
            TotalMigrated += result.MigratedCount;
            TotalSkipped += result.SkippedCount;
            TotalErrors += result.ErrorCount;
            TotalElapsedSeconds += result.ElapsedSeconds;
            if (result.ErrorCount > 0) Success = false;
        }

        public string GetSummary()
        {
            var sb = new System.Text.StringBuilder();
            sb.AppendLine("=== Migration Summary ===");
            sb.AppendLine(string.Format("Total source rows: {0}", TotalSourceRows));
            sb.AppendLine(string.Format("Total migrated: {0}", TotalMigrated));
            sb.AppendLine(string.Format("Total skipped: {0}", TotalSkipped));
            sb.AppendLine(string.Format("Total errors: {0}", TotalErrors));
            sb.AppendLine(string.Format("Elapsed: {0:F1}s", TotalElapsedSeconds));
            sb.AppendLine(string.Format("Status: {0}", Success ? "SUCCESS" : "ERRORS"));
            sb.AppendLine();

            foreach (var tr in TableResults)
            {
                sb.AppendLine(string.Format("  {0}: {1}/{2} migrated, {3} skipped, {4} errors ({5:F1}s)",
                    tr.TableName, tr.MigratedCount, tr.SourceCount,
                    tr.SkippedCount, tr.ErrorCount, tr.ElapsedSeconds));
                foreach (var err in tr.Errors)
                {
                    sb.AppendLine("    ERROR: " + err);
                }
            }

            return sb.ToString();
        }
    }
}
