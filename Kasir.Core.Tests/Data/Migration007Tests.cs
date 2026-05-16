using System;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using NUnit.Framework;
using Kasir.Data.Migrations;
using Kasir.Tests.TestHelpers;

namespace Kasir.Tests.Data
{
    [TestFixture]
    public class Migration007Tests
    {
        [Test]
        public void Up_Twice_IsIdempotent()
        {
            using var db = TestDb.Create();
            var migration = new Migration_007();

            Action runTwice = () =>
            {
                migration.Up(db);
                migration.Up(db);
            };

            runTwice.Should().NotThrow();
        }

        [Test]
        public void Up_RethrowsNonDuplicateColumnError()
        {
            using var db = new SqliteConnection("Data Source=:memory:");
            db.Open();

            var migration = new Migration_007();

            Action act = () => migration.Up(db);

            act.Should().Throw<SqliteException>()
                .Where(ex => ex.Message.IndexOf("no such table", StringComparison.OrdinalIgnoreCase) >= 0);
        }
    }
}
