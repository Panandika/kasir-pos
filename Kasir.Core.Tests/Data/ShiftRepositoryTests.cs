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

        [Test]
        public void NextShiftNumber_ShiftAt2359_CountedForThatDay()
        {
            OpenAt("2026-05-07 23:59:59");
            _repo.NextShiftNumber("01", "2026-05-07").Should().Be("2");
        }

        [Test]
        public void NextShiftNumber_ShiftAtMidnight_NotCountedForPreviousDay()
        {
            OpenAt("2026-05-08 00:00:00");
            _repo.NextShiftNumber("01", "2026-05-07").Should().Be("1");
        }

        [Test]
        public void OpenShiftAtomic_AssignsShiftNumberAndInserts()
        {
            var shift = new Shift
            {
                RegisterId = "01",
                CashierId = 1,
                OpenedAt = "2026-05-07 09:00:00",
                OpeningCash = 5000000
            };

            int id = _repo.OpenShiftAtomic(shift, "2026-05-07");

            id.Should().BeGreaterThan(0);
            shift.ShiftNumber.Should().Be("1");
            var loaded = _repo.GetById(id);
            loaded.ShiftNumber.Should().Be("1");
            loaded.RegisterId.Should().Be("01");
            loaded.Status.Should().Be("O");
        }

        [Test]
        public void OpenShiftAtomic_SecondCall_IncrementsShiftNumber()
        {
            var first = new Shift { RegisterId = "01", CashierId = 1, OpenedAt = "2026-05-07 08:00:00", OpeningCash = 5000000 };
            _repo.OpenShiftAtomic(first, "2026-05-07");

            var second = new Shift { RegisterId = "01", CashierId = 1, OpenedAt = "2026-05-07 14:00:00", OpeningCash = 5000000 };
            _repo.OpenShiftAtomic(second, "2026-05-07");

            second.ShiftNumber.Should().Be("2");
        }

        [Test]
        public void CloseShift_ReturnsVariance()
        {
            int id = OpenAt("2026-05-07 08:00:00");

            long variance = _repo.CloseShift(id, closingCash: 15500000, expectedCash: 14000000);

            variance.Should().Be(1500000);
        }

        [Test]
        public void GetByDateRange_IncludesShiftOpenedAt2359OnEndDate()
        {
            OpenAt("2026-05-07 23:59:59");

            var shifts = _repo.GetByDateRange("2026-05-07 00:00:00", "2026-05-07");

            shifts.Should().HaveCount(1);
        }
    }
}
