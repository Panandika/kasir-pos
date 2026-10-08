using System;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using NUnit.Framework;
using Kasir.Data.Migrations;
using Kasir.Data.Repositories;
using Kasir.Models;
using Kasir.Tests.TestHelpers;

namespace Kasir.Tests.Data
{
    [TestFixture]
    public class Migration012Tests
    {
        private static long Scalar(SqliteConnection db, string sql)
        {
            using var cmd = db.CreateCommand();
            cmd.CommandText = sql;
            return Convert.ToInt64(cmd.ExecuteScalar());
        }

        private static void Exec(SqliteConnection db, string sql)
        {
            using var cmd = db.CreateCommand();
            cmd.CommandText = sql;
            cmd.ExecuteNonQuery();
        }

        // Turns a current-schema test DB back into the pre-K3 shape: no inactive_sale_log
        // and a sync_queue whose CHECK list does not know the new table.
        private static SqliteConnection CreatePreK3Db()
        {
            var db = TestDb.Create();
            Exec(db, @"
                DROP TRIGGER trg_inactive_sale_log_sync_i;
                DROP TABLE inactive_sale_log;
                PRAGMA legacy_alter_table = ON;
                CREATE TABLE sync_queue_old (
                    id              INTEGER PRIMARY KEY,
                    register_id     TEXT    NOT NULL,
                    table_name      TEXT    NOT NULL CHECK(table_name IN (
                                        'products', 'product_barcodes',
                                        'sales', 'purchases',
                                        'cash_transactions',
                                        'memorial_journals',
                                        'orders',
                                        'stock_transfers',
                                        'stock_adjustments',
                                        'members', 'subsidiaries',
                                        'departments', 'discounts',
                                        'accounts', 'locations',
                                        'discount_partners', 'credit_cards'
                                    )),
                    record_key      TEXT    NOT NULL,
                    operation       TEXT    NOT NULL CHECK(operation IN ('I','U','D')),
                    payload         TEXT    CHECK(payload IS NULL OR json_valid(payload)),
                    created_at      TEXT    NOT NULL DEFAULT (datetime('now')),
                    synced_at       TEXT,
                    status          TEXT    NOT NULL DEFAULT 'pending'
                                           CHECK(status IN ('pending','synced','failed')),
                    retry_count     INTEGER NOT NULL DEFAULT 0,
                    last_error      TEXT,
                    cloud_synced    INTEGER NOT NULL DEFAULT 0,
                    cloud_synced_at TEXT
                );
                DROP TABLE sync_queue;
                ALTER TABLE sync_queue_old RENAME TO sync_queue;
                PRAGMA legacy_alter_table = OFF;");
            return db;
        }

        private static void InsertProduct(SqliteConnection db, string code)
        {
            new ProductRepository(db).Insert(new Product
            {
                ProductCode = code, Name = "X " + code, Price = 100, Status = "A",
                OpenPrice = "N", VatFlag = "N", LuxuryTaxFlag = "N", IsConsignment = "N"
            });
        }

        [Test]
        public void Up_OnCurrentSchema_IsIdempotent()
        {
            using var db = TestDb.Create();
            var m = new Migration_012();
            Action twice = () => { m.Up(db); m.Up(db); };
            twice.Should().NotThrow();
            Scalar(db, "SELECT COUNT(*) FROM sqlite_master WHERE name = 'inactive_sale_log'").Should().Be(1);
        }

        [Test]
        public void Up_OnPreK3Db_RebuildsSyncQueue_KeepingRowsAndTriggers()
        {
            using var db = CreatePreK3Db();
            InsertProduct(db, "A1"); // queued through trg_products_sync_i
            Exec(db, "UPDATE sync_queue SET status = 'synced', cloud_synced = 1, retry_count = 2");
            Exec(db, "INSERT INTO sync_queue (register_id, table_name, record_key, operation) VALUES ('01','product_barcodes','X','I')");

            using (var txn = db.BeginTransaction())
            {
                new Migration_012().Up(db);
                txn.Commit();
            }

            Scalar(db, "SELECT COUNT(*) FROM sync_queue WHERE table_name = 'products' AND record_key = 'A1' AND status = 'synced' AND cloud_synced = 1 AND retry_count = 2")
                .Should().Be(1, "existing queue rows survive the rebuild with their bookkeeping");
            Scalar(db, "SELECT COUNT(*) FROM sync_queue WHERE table_name = 'product_barcodes'")
                .Should().Be(0, "rows for the dropped product_barcodes table cannot satisfy the new CHECK");

            // The other tables' sync triggers still write into the rebuilt queue.
            InsertProduct(db, "A2");
            Scalar(db, "SELECT COUNT(*) FROM sync_queue WHERE record_key = 'A2'").Should().Be(1);

            // The new table is accepted and queued.
            Exec(db, "INSERT INTO inactive_sale_log (register_id, product_code, sale_date) VALUES ('01','A1','2026-04-04')");
            Scalar(db, "SELECT COUNT(*) FROM sync_queue WHERE table_name = 'inactive_sale_log'").Should().Be(1);

            // Drain indexes are recreated.
            Scalar(db, "SELECT COUNT(*) FROM sqlite_master WHERE type = 'index' AND name IN ('idx_sync_queue_drain','idx_sync_queue_cloud_drain')")
                .Should().Be(2);
            Scalar(db, "PRAGMA foreign_key_check").Should().Be(0);
        }

        [Test]
        public void Up_KeepsIdsMonotonic_AfterRebuild()
        {
            using var db = CreatePreK3Db();
            InsertProduct(db, "A1");
            InsertProduct(db, "A2");
            long maxBefore = Scalar(db, "SELECT MAX(id) FROM sync_queue");

            new Migration_012().Up(db);
            InsertProduct(db, "A3");

            Scalar(db, "SELECT id FROM sync_queue WHERE record_key = 'A3'").Should().BeGreaterThan(maxBefore,
                "the hub's last_applied_id cursor relies on monotonic queue ids");
        }
    }
}
