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

        private static string Text(SqliteConnection db, string sql)
        {
            using var cmd = db.CreateCommand();
            cmd.CommandText = sql;
            return (string)cmd.ExecuteScalar();
        }

        private static void Exec(SqliteConnection db, string sql)
        {
            using var cmd = db.CreateCommand();
            cmd.CommandText = sql;
            cmd.ExecuteNonQuery();
        }

        // A current-schema test DB without the K3 table: the shape of a live register before 012.
        private static SqliteConnection CreatePreK3Db()
        {
            var db = TestDb.Create();
            Exec(db, "DROP TABLE inactive_sale_log");
            return db;
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
        public void Up_OnPreK3Db_CreatesLogTable_WithoutTouchingSyncQueue()
        {
            using var db = CreatePreK3Db();
            string queueSqlBefore = Text(db, "SELECT sql FROM sqlite_master WHERE type = 'table' AND name = 'sync_queue'");

            using (var txn = db.BeginTransaction())
            {
                new Migration_012().Up(db);
                txn.Commit();
            }

            Scalar(db, "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = 'inactive_sale_log'").Should().Be(1);
            Text(db, "SELECT sql FROM sqlite_master WHERE type = 'table' AND name = 'sync_queue'")
                .Should().Be(queueSqlBefore, "M1: no sync_queue rebuild, no CHECK change");
        }

        [Test]
        public void Up_LogTable_HasNoSyncTrigger_AndDedupesPerDay()
        {
            using var db = CreatePreK3Db();
            new Migration_012().Up(db);

            Exec(db, "INSERT OR IGNORE INTO inactive_sale_log (register_id, product_code, sale_date) VALUES ('01','A1','2026-04-04')");
            Exec(db, "INSERT OR IGNORE INTO inactive_sale_log (register_id, product_code, sale_date) VALUES ('01','A1','2026-04-04')");

            Scalar(db, "SELECT COUNT(*) FROM inactive_sale_log").Should().Be(1, "UNIQUE(product_code, sale_date)");
            Scalar(db, "SELECT COUNT(*) FROM sync_queue").Should().Be(0, "the log never enters sync_queue (cloud delivery deferred)");
            Scalar(db, "SELECT COUNT(*) FROM sqlite_master WHERE type = 'trigger' AND tbl_name = 'inactive_sale_log'").Should().Be(0);
        }

        [Test]
        public void Up_DropsAStrayDevSyncTrigger()
        {
            // A dev DB that ran the first draft of 012 has the queue trigger; on a DB whose
            // CHECK does not list the table it would abort every inactive scan.
            using var db = TestDb.Create();
            Exec(db, @"CREATE TRIGGER trg_inactive_sale_log_sync_i AFTER INSERT ON inactive_sale_log
                       BEGIN SELECT 1; END");

            new Migration_012().Up(db);

            Scalar(db, "SELECT COUNT(*) FROM sqlite_master WHERE name = 'trg_inactive_sale_log_sync_i'").Should().Be(0);
        }

        [Test]
        public void Up_OtherTablesStillQueue()
        {
            using var db = CreatePreK3Db();
            new Migration_012().Up(db);

            new ProductRepository(db).Insert(new Product
            {
                ProductCode = "A2", Name = "X A2", Price = 100, Status = "A",
                OpenPrice = "N", VatFlag = "N", LuxuryTaxFlag = "N", IsConsignment = "N"
            });
            Scalar(db, "SELECT COUNT(*) FROM sync_queue WHERE record_key = 'A2'").Should().Be(1);
        }
    }
}
