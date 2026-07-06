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
    public class Migration008Tests
    {
        [Test]
        public void Up_Twice_IsIdempotent()
        {
            using var db = TestDb.Create();
            var migration = new Migration_008();

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

            var migration = new Migration_008();

            Action act = () => migration.Up(db);

            act.Should().Throw<SqliteException>()
                .Where(ex => ex.Message.IndexOf("no such table", StringComparison.OrdinalIgnoreCase) >= 0);
        }

        [Test]
        public void CardType_RoundTrips_ThroughRepository()
        {
            using var db = TestDb.Create();
            var repo = new CreditCardRepository(db);

            repo.Insert(new CreditCard
            {
                CardCode = "QRIS",
                Name = "QRIS (EDC)",
                AccountCode = "1102",
                FeePct = 0,
                CardType = "Q",
                ChangedBy = 1
            });

            var loaded = repo.GetByCode("QRIS");
            loaded.CardType.Should().Be("Q");
        }

        [Test]
        public void CardType_Empty_DefaultsToCredit()
        {
            using var db = TestDb.Create();
            var repo = new CreditCardRepository(db);

            repo.Insert(new CreditCard
            {
                CardCode = "BCA",
                Name = "BCA Card",
                AccountCode = "1101",
                FeePct = 250,
                CardType = "",
                ChangedBy = 1
            });

            var loaded = repo.GetByCode("BCA");
            loaded.CardType.Should().Be("C");
        }
    }
}
