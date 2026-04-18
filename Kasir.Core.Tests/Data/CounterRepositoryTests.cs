using Microsoft.Data.Sqlite;
using NUnit.Framework;
using FluentAssertions;
using Kasir.Data.Repositories;
using Kasir.Tests.TestHelpers;

namespace Kasir.Tests.Data
{
    [TestFixture]
    public class CounterRepositoryTests
    {
        private SqliteConnection _db;
        private CounterRepository _repo;

        [SetUp]
        public void SetUp()
        {
            _db = TestDb.Create();
            _repo = new CounterRepository(_db);
        }

        [TearDown]
        public void TearDown()
        {
            _db.Close();
            _db.Dispose();
        }

        [Test]
        public void GetNext_FirstCall_Returns1()
        {
            string result = _repo.GetNext("KLR", "01");

            result.Should().Contain("KLR");
            result.Should().Contain("01");
            result.Should().Contain("0001");
        }

        [Test]
        public void GetNext_SecondCall_Returns2()
        {
            _repo.GetNext("KLR", "01");
            string result = _repo.GetNext("KLR", "01");

            result.Should().Contain("0002");
        }

        [Test]
        public void GetNext_DifferentPrefixes_IndependentCounters()
        {
            string klr1 = _repo.GetNext("KLR", "01");
            string msk1 = _repo.GetNext("MSK", "01");
            string klr2 = _repo.GetNext("KLR", "01");

            klr1.Should().Contain("KLR").And.Contain("0001");
            msk1.Should().Contain("MSK").And.Contain("0001");
            klr2.Should().Contain("KLR").And.Contain("0002");
        }

        [Test]
        public void GetNext_DifferentRegisters_IndependentCounters()
        {
            string reg01 = _repo.GetNext("KLR", "01");
            string reg02 = _repo.GetNext("KLR", "02");

            reg01.Should().Contain("01").And.Contain("0001");
            reg02.Should().Contain("02").And.Contain("0001");
        }

        [Test]
        public void Reset_SetsCounterToZero()
        {
            _repo.GetNext("KLR", "01");
            _repo.GetNext("KLR", "01");
            _repo.Reset("KLR", "01");

            string result = _repo.GetNext("KLR", "01");
            result.Should().Contain("0001");
        }

        [Test]
        public void GetNext_Sequential_NoDuplicates()
        {
            var numbers = new System.Collections.Generic.HashSet<string>();
            for (int i = 0; i < 100; i++)
            {
                string num = _repo.GetNext("TST", "01");
                numbers.Add(num).Should().BeTrue(
                    "document number {0} should be unique", num);
            }

            numbers.Count.Should().Be(100);
        }
    }
}
