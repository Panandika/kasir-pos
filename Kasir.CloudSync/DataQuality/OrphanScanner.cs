using System;
using System.Collections.Generic;
using Microsoft.Data.Sqlite;

namespace Kasir.CloudSync.DataQuality
{
    // Phase C follow-up — pre-load orphan scan.
    //
    // Scans the SQLite source for rows that would fail the strict FK
    // constraints applied at the END of the initial load (Sql/constraints.sql).
    // Catching them BEFORE the load means the operator sees a clean failure
    // up front and can choose to (a) clean the source, (b) skip-orphans and
    // accept they will be excluded from the mirror, or (c) abort the load.
    //
    // We do NOT mutate the SQLite source. The scanner is read-only.
    public class OrphanScanner
    {
        private readonly SqliteConnection _db;

        public OrphanScanner(SqliteConnection db)
        {
            _db = db;
        }

        // Each Check is one (child_table, fk_column) -> (parent_table, parent_column)
        // pair that the production constraints.sql will enforce. Keep in sync
        // with constraints.sql by inspection — the schema-drift CI guards
        // column drift but not constraint-set drift, so a code review is
        // expected when adding a constraint.
        public static readonly IReadOnlyList<OrphanCheck> Checks = new[]
        {
            new OrphanCheck("product_barcodes", "product_code", "products", "product_code"),
            new OrphanCheck("sale_items", "journal_no", "sales", "journal_no"),
            new OrphanCheck("sale_items", "product_code", "products", "product_code"),
            new OrphanCheck("stock_movements", "product_code", "products", "product_code"),
            // purchases.sub_code skipped here because subsidiaries may be
            // mid-migration; add to enforced set after the first clean load.
        };

        public OrphanScanResult Scan()
        {
            var result = new OrphanScanResult { PerCheck = new List<OrphanCheckResult>() };

            foreach (var check in Checks)
            {
                using var cmd = _db.CreateCommand();
                cmd.CommandText = $@"
                    SELECT COUNT(*) FROM [{check.ChildTable}] c
                    LEFT JOIN [{check.ParentTable}] p
                      ON c.[{check.ChildColumn}] = p.[{check.ParentColumn}]
                    WHERE c.[{check.ChildColumn}] IS NOT NULL
                      AND TRIM(c.[{check.ChildColumn}]) != ''
                      AND p.[{check.ParentColumn}] IS NULL;";
                long orphans;
                try { orphans = Convert.ToInt64(cmd.ExecuteScalar()); }
                catch (SqliteException) { orphans = -1; } // table missing

                var sample = orphans > 0 ? CollectSampleKeys(check, max: 5) : new List<string>();
                result.PerCheck.Add(new OrphanCheckResult
                {
                    Check = check,
                    OrphanCount = orphans,
                    SampleKeys = sample
                });
            }
            return result;
        }

        private List<string> CollectSampleKeys(OrphanCheck check, int max)
        {
            var keys = new List<string>(max);
            using var cmd = _db.CreateCommand();
            cmd.CommandText = $@"
                SELECT DISTINCT c.[{check.ChildColumn}] FROM [{check.ChildTable}] c
                LEFT JOIN [{check.ParentTable}] p
                  ON c.[{check.ChildColumn}] = p.[{check.ParentColumn}]
                WHERE c.[{check.ChildColumn}] IS NOT NULL
                  AND TRIM(c.[{check.ChildColumn}]) != ''
                  AND p.[{check.ParentColumn}] IS NULL
                LIMIT @max;";
            var p = cmd.CreateParameter(); p.ParameterName = "@max"; p.Value = max;
            cmd.Parameters.Add(p);
            using var reader = cmd.ExecuteReader();
            while (reader.Read()) keys.Add(reader.GetString(0));
            return keys;
        }
    }

    public class OrphanCheck
    {
        public string ChildTable { get; }
        public string ChildColumn { get; }
        public string ParentTable { get; }
        public string ParentColumn { get; }

        public OrphanCheck(string childTable, string childColumn, string parentTable, string parentColumn)
        {
            ChildTable = childTable;
            ChildColumn = childColumn;
            ParentTable = parentTable;
            ParentColumn = parentColumn;
        }

        public override string ToString() =>
            $"{ChildTable}.{ChildColumn} -> {ParentTable}.{ParentColumn}";
    }

    public class OrphanCheckResult
    {
        public OrphanCheck Check { get; set; }
        public long OrphanCount { get; set; }
        public List<string> SampleKeys { get; set; }
    }

    public class OrphanScanResult
    {
        public List<OrphanCheckResult> PerCheck { get; set; }

        public bool HasAnyOrphans
        {
            get
            {
                foreach (var c in PerCheck)
                    if (c.OrphanCount > 0) return true;
                return false;
            }
        }

        public long TotalOrphans
        {
            get
            {
                long total = 0;
                foreach (var c in PerCheck)
                    if (c.OrphanCount > 0) total += c.OrphanCount;
                return total;
            }
        }
    }
}
