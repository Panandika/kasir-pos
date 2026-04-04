using System.Data.SQLite;
using NUnit.Framework;
using FluentAssertions;
using Kasir.Data.Repositories;
using Kasir.Models;
using Kasir.Tests.TestHelpers;

namespace Kasir.Tests.Data
{
    [TestFixture]
    public class DepartmentRepositoryTests
    {
        private SQLiteConnection _db;
        private DepartmentRepository _repo;

        [SetUp]
        public void SetUp()
        {
            _db = TestDb.Create();
            _repo = new DepartmentRepository(_db);
        }

        [TearDown]
        public void TearDown()
        {
            _db.Close();
            _db.Dispose();
        }

        [Test]
        public void Insert_ValidDepartment_ReturnsId()
        {
            var dept = new Department { DeptCode = "10", Name = "ALAT TULIS", ChangedBy = 1 };

            int id = _repo.Insert(dept);

            id.Should().BeGreaterThan(0);
        }

        [Test]
        public void GetByCode_ExistingDept_ReturnsDept()
        {
            _repo.Insert(new Department { DeptCode = "10", Name = "ALAT TULIS", ChangedBy = 1 });

            var dept = _repo.GetByCode("10");

            dept.Should().NotBeNull();
            dept.DeptCode.Should().Be("10");
            dept.Name.Should().Be("ALAT TULIS");
        }

        [Test]
        public void GetByCode_NonExistent_ReturnsNull()
        {
            var dept = _repo.GetByCode("99");
            dept.Should().BeNull();
        }

        [Test]
        public void GetAll_ReturnsAllDepts()
        {
            _repo.Insert(new Department { DeptCode = "10", Name = "ALAT TULIS", ChangedBy = 1 });
            _repo.Insert(new Department { DeptCode = "11", Name = "AKSESORIS", ChangedBy = 1 });
            _repo.Insert(new Department { DeptCode = "12", Name = "KOSMETIK", ChangedBy = 1 });

            var depts = _repo.GetAll();

            depts.Count.Should().Be(3);
        }

        [Test]
        public void GetAll_OrderedByCode()
        {
            _repo.Insert(new Department { DeptCode = "12", Name = "KOSMETIK", ChangedBy = 1 });
            _repo.Insert(new Department { DeptCode = "10", Name = "ALAT TULIS", ChangedBy = 1 });
            _repo.Insert(new Department { DeptCode = "11", Name = "AKSESORIS", ChangedBy = 1 });

            var depts = _repo.GetAll();

            depts[0].DeptCode.Should().Be("10");
            depts[1].DeptCode.Should().Be("11");
            depts[2].DeptCode.Should().Be("12");
        }

        [Test]
        public void Update_ChangesName()
        {
            _repo.Insert(new Department { DeptCode = "10", Name = "ALAT TULIS", ChangedBy = 1 });
            var dept = _repo.GetByCode("10");

            dept.Name = "ALAT TULIS & KANTOR";
            dept.ChangedBy = 2;
            _repo.Update(dept);

            var updated = _repo.GetByCode("10");
            updated.Name.Should().Be("ALAT TULIS & KANTOR");
        }

        [Test]
        public void Delete_RemovesDept()
        {
            _repo.Insert(new Department { DeptCode = "10", Name = "ALAT TULIS", ChangedBy = 1 });
            var dept = _repo.GetByCode("10");

            _repo.Delete(dept.Id);

            _repo.GetByCode("10").Should().BeNull();
        }

        [Test]
        public void Insert_DuplicateCode_Throws()
        {
            _repo.Insert(new Department { DeptCode = "10", Name = "ALAT TULIS", ChangedBy = 1 });

            System.Action act = () =>
                _repo.Insert(new Department { DeptCode = "10", Name = "DUPLICATE", ChangedBy = 1 });

            act.Should().Throw<SQLiteException>();
        }
    }
}
