using System;
using System.Data.SQLite;
using System.IO;
using Kasir.Data;
using Kasir.Data.Repositories;
using Kasir.Models;

namespace Kasir.Migration
{
    public class DbfMigrator
    {
        private readonly string _dbfBasePath;
        private readonly SQLiteConnection _db;
        private Action<string> _logger;
        private const int BatchSize = 1000;

        public DbfMigrator(string dbfBasePath, SQLiteConnection db, Action<string> logger)
        {
            _dbfBasePath = dbfBasePath;
            _db = db;
            _logger = logger ?? (s => { });
        }

        public MigrationResult Migrate()
        {
            var result = new MigrationResult();

            foreach (var table in FieldMappings.MigrationOrder)
            {
                try
                {
                    _logger("Migrating " + table + "...");
                    var tableResult = MigrateTable(table);
                    result.AddTableResult(tableResult);
                    _logger(string.Format("  {0}: {1}/{2} rows ({3:F1}s)",
                        table, tableResult.MigratedCount, tableResult.SourceCount,
                        tableResult.ElapsedSeconds));
                }
                catch (Exception ex)
                {
                    var errResult = new TableMigrationResult
                    {
                        TableName = table,
                        ErrorCount = 1
                    };
                    errResult.Errors.Add("Table migration failed: " + ex.Message);
                    result.AddTableResult(errResult);
                    _logger("  ERROR: " + table + " — " + ex.Message);
                }
            }

            _logger(result.GetSummary());
            return result;
        }

        private TableMigrationResult MigrateTable(string tableName)
        {
            // This method dispatches to specific migrators based on table name.
            // Each migrator reads the DBF using dBASE.NET and inserts into SQLite.
            // Actual DBF reading requires dBASE.NET NuGet (only on Windows).
            //
            // The migration is designed to run ONE TIME on the production
            // Windows machine with access to the DBF files.
            //
            // For now, return a placeholder result.
            // Actual implementation uses:
            //   var dbf = new dBASE.NET.Dbf();
            //   dbf.Read(path);
            //   foreach (var record in dbf.Records) { ... }

            var result = new TableMigrationResult { TableName = tableName };
            var sw = System.Diagnostics.Stopwatch.StartNew();

            string dbfPath = GetDbfPath(tableName);
            if (string.IsNullOrEmpty(dbfPath) || !File.Exists(Path.Combine(_dbfBasePath, dbfPath)))
            {
                result.SkippedCount = 1;
                result.Errors.Add("DBF file not found: " + (dbfPath ?? "unknown"));
                sw.Stop();
                result.ElapsedSeconds = sw.Elapsed.TotalSeconds;
                return result;
            }

            // Actual migration would read DBF and insert rows here.
            // Each table type has its own migration logic using MigrationTransforms.
            //
            // Example pattern:
            // using (var txn = _db.BeginTransaction())
            // {
            //     var dbf = new dBASE.NET.Dbf();
            //     dbf.Read(fullPath);
            //     result.SourceCount = dbf.Records.Count;
            //     int batch = 0;
            //     foreach (var record in dbf.Records)
            //     {
            //         try
            //         {
            //             MigrateRecord(tableName, record);
            //             result.MigratedCount++;
            //             batch++;
            //             if (batch >= BatchSize) { txn.Commit(); txn = _db.BeginTransaction(); batch = 0; }
            //         }
            //         catch (Exception ex)
            //         {
            //             result.ErrorCount++;
            //             result.Errors.Add("Row " + result.MigratedCount + ": " + ex.Message);
            //             if (result.ErrorCount > 100) break; // circuit breaker
            //         }
            //     }
            //     txn.Commit();
            // }

            sw.Stop();
            result.ElapsedSeconds = sw.Elapsed.TotalSeconds;
            return result;
        }

        private string GetDbfPath(string tableName)
        {
            switch (tableName)
            {
                case "roles": return FieldMappings.DbfPaths.Roles;
                case "users": return FieldMappings.DbfPaths.Users;
                case "departments": return FieldMappings.DbfPaths.Departments;
                case "subsidiaries": return FieldMappings.DbfPaths.Vendors;
                case "products": return FieldMappings.DbfPaths.Products;
                case "product_barcodes": return FieldMappings.DbfPaths.Barcodes;
                case "members": return FieldMappings.DbfPaths.Members;
                case "discounts": return FieldMappings.DbfPaths.Discounts;
                case "credit_cards": return FieldMappings.DbfPaths.CreditCards;
                case "locations": return FieldMappings.DbfPaths.Locations;
                case "accounts": return FieldMappings.DbfPaths.Accounts;
                case "counters": return FieldMappings.DbfPaths.Counters;
                case "sales": return FieldMappings.DbfPaths.SaleHeaders;
                case "sale_items": return FieldMappings.DbfPaths.SaleDetails;
                case "purchases": return FieldMappings.DbfPaths.Purchases;
                case "purchase_items": return FieldMappings.DbfPaths.PurchaseDetails;
                case "orders": return FieldMappings.DbfPaths.PurchaseOrders;
                case "order_items": return FieldMappings.DbfPaths.PurchaseOrderDetails;
                case "payables_register": return FieldMappings.DbfPaths.Payables;
                case "stock_movements": return FieldMappings.DbfPaths.StockHistory;
                case "price_history": return FieldMappings.DbfPaths.PriceHistory;
                case "cash_transactions": return FieldMappings.DbfPaths.Payments;
                default: return null;
            }
        }
    }
}
