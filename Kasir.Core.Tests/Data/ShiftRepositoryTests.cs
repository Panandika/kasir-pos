using Microsoft.Data.Sqlite;
using NUnit.Framework;
using FluentAssertions;
using Kasir.Data.Repositories;
using Kasir.Models;
using Kasir.Tests.TestHelpers;

namespace Kasir.Tests.Data
{
    [TestFixture]
    public class ShiftRepositoryTests
    {
        private SqliteConnection _db;
        private ShiftRepository _repo;

        [SetUp]
        public void SetUp()
        {
            _db = TestDb.Create();
            _repo = new ShiftRepository(_db);
        }

        [TearDown]
        public void TearDown()
        {
            _db.Close();
            _db.Dispose();
        }

        private int OpenAt(string opened, string shiftNumber = "1", string register = "01")
        {
            return _repo.OpenShift(new Shift
            {
                RegisterId = register,
                ShiftNumber = shiftNumber,
                CashierId = 1,
                OpenedAt = opened,
                OpeningCash = 10000000
            });
        }

        [Test]
        public void NextShiftNumber_NoShifts_ReturnsOne()
        {
            _repo.NextShiftNumber("01", "2026-05-07").Should().Be("1");
        }

        [Test]
        public void NextShiftNumber_OneShiftToday_ReturnsTwo()
        {
            OpenAt("2026-05-07 08:00:00");
            _repo.NextShiftNumber("01", "2026-05-07").Should().Be("2");
        }

        [Test]
        public void NextShiftNumber_TwoShiftsToday_ReturnsThree()
        {
            OpenAt("2026-05-07 08:00:00", "1");
            OpenAt("2026-05-07 14:00:00", "2");
            _repo.NextShiftNumber("01", "2026-05-07").Should().Be("3");
        }

        [Test]
        public void NextShiftNumber_OnlyCountsShiftsForDate()
        {
            OpenAt("2026-05-06 08:00:00");
            _repo.NextShiftNumber("01", "2026-05-07").Should().Be("1");
        }

        [Test]
        public void NextShiftNumber_OnlyCountsShiftsForRegister()
        {
            OpenAt("2026-05-07 08:00:00", "1", "02");
            _repo.NextShiftNumber("01", "2026-05-07").Should().Be("1");
        }

        [Test]
        public void CloseShift_PersistsCashVariance_PositiveOver()
        {
            int id = OpenAt("2026-05-07 08:00:00");

            _repo.CloseShift(id, closingCash: 15000000, expectedCash: 14000000);

            var loaded = _repo.GetById(id);
            loaded.Status.Should().Be("C");
            loaded.ClosingCash.Should().Be(15000000);
            loaded.ExpectedCash.Should().Be(14000000);
            loaded.CashVariance.Should().Be(1000000);
        }

        [Test]
        public void CloseShift_PersistsCashVariance_NegativeShort()
        {
            int id = OpenAt("2026-05-07 08:00:00");

            _repo.CloseShift(id, closingCash: 13000000, expectedCash: 14000000);

            var loaded = _repo.GetById(id);
            loaded.CashVariance.Should().Be(-1000000);
        }

        [Test]
        public void CloseShift_PersistsCashVariance_Zero()
        {
            int id = OpenAt("2026-05-07 08:00:00");

            _repo.CloseShift(id, closingCash: 14000000, expectedCash: 14000000);

            var loaded = _repo.GetById(id);
            loaded.CashVariance.Should().Be(0);
        }

        [Test]
        public void OpenShift_LeavesCashVarianceNull()
        {
            int id = OpenAt("2026-05-07 08:00:00");
            var loaded = _repo.GetById(id);
            loaded.CashVariance.Should().BeNull();
        }
    }
}
