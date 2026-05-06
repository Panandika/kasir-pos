using System;
using Microsoft.Data.Sqlite;
using NUnit.Framework;
using FluentAssertions;
using Kasir.Help;
using Kasir.Tests.TestHelpers;

namespace Kasir.Tests.Help
{
    [TestFixture]
    public class TicketNumberGeneratorTests
    {
        private SqliteConnection _db;

        [SetUp]
        public void SetUp() { _db = TestDb.Create(); }

        [TearDown]
        public void TearDown() { _db.Close(); _db.Dispose(); }

        [Test]
        public void Next_FormatsExpectedShape()
        {
            DateTime fixedNow = new DateTime(2026, 5, 7);
            var gen = new TicketNumberGenerator(_db, "SM", "01", () => fixedNow);

            string n = gen.Next();
            n.Should().Be("TKT-SM-01-260507-0001");
        }

        [Test]
        public void Next_IncrementsPerCall()
        {
            DateTime fixedNow = new DateTime(2026, 5, 7);
            var gen = new TicketNumberGenerator(_db, "SM", "01", () => fixedNow);

            gen.Next().Should().EndWith("0001");
            gen.Next().Should().EndWith("0002");
            gen.Next().Should().EndWith("0003");
        }

        [Test]
        public void Next_PerRegisterCountersDoNotCollide()
        {
            DateTime fixedNow = new DateTime(2026, 5, 7);
            var reg1 = new TicketNumberGenerator(_db, "SM", "01", () => fixedNow);
            var reg2 = new TicketNumberGenerator(_db, "SM", "02", () => fixedNow);

            reg1.Next().Should().Be("TKT-SM-01-260507-0001");
            reg2.Next().Should().Be("TKT-SM-02-260507-0001");
            reg1.Next().Should().Be("TKT-SM-01-260507-0002");
            reg2.Next().Should().Be("TKT-SM-02-260507-0002");
        }

        [Test]
        public void Next_RegisterIdIsAlwaysTwoDigits()
        {
            DateTime fixedNow = new DateTime(2026, 5, 7);
            var gen = new TicketNumberGenerator(_db, "SM", "1", () => fixedNow);
            gen.Next().Should().Be("TKT-SM-01-260507-0001");
        }
    }
}
