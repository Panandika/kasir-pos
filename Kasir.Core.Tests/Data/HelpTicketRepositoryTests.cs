using Microsoft.Data.Sqlite;
using NUnit.Framework;
using FluentAssertions;
using Kasir.Data.Repositories;
using Kasir.Models;
using Kasir.Tests.TestHelpers;

namespace Kasir.Tests.Data
{
    [TestFixture]
    public class HelpTicketRepositoryTests
    {
        private SqliteConnection _db;
        private HelpTicketRepository _repo;

        [SetUp]
        public void SetUp()
        {
            _db = TestDb.Create();
            _repo = new HelpTicketRepository(_db);
        }

        [TearDown]
        public void TearDown()
        {
            _db.Close();
            _db.Dispose();
        }

        private static HelpTicket Sample(string ticketNo = "TKT-SM-01-260507-0001")
        {
            return new HelpTicket
            {
                TicketNo = ticketNo,
                StoreId = "sinar-makmur",
                RegisterId = "01",
                CashierId = "U1",
                Category = "hardware",
                Body = "Printer macet setelah cetak ke-3, lampu merah",
                AttachmentsJson = "{\"version\":\"2.4.1\",\"invoice\":\"INV-047\"}"
            };
        }

        [Test]
        public void Insert_QueuesTicket_DefaultsAreSet()
        {
            int id = _repo.Insert(Sample());

            id.Should().BeGreaterThan(0);
            var t = _repo.GetById(id);
            t.Should().NotBeNull();
            t.Status.Should().Be("queued");
            t.SyncAttempts.Should().Be(0);
            t.SentAt.Should().BeNull();
            t.LastError.Should().BeNull();
            t.ClientCreatedAt.Should().NotBeNullOrEmpty();
        }

        [Test]
        public void Insert_DuplicateTicketNo_Throws()
        {
            _repo.Insert(Sample());
            System.Action act = () => _repo.Insert(Sample());
            act.Should().Throw<SqliteException>();
        }

        [Test]
        public void Insert_InvalidCategory_Throws()
        {
            var t = Sample();
            t.Category = "spam";
            System.Action act = () => _repo.Insert(t);
            act.Should().Throw<SqliteException>();
        }

        [Test]
        public void Insert_BodyTooShort_Throws()
        {
            var t = Sample();
            t.Body = "ok";
            System.Action act = () => _repo.Insert(t);
            act.Should().Throw<SqliteException>();
        }

        [Test]
        public void Insert_BodyTooLong_Throws()
        {
            var t = Sample();
            t.Body = new string('a', 2001);
            System.Action act = () => _repo.Insert(t);
            act.Should().Throw<SqliteException>();
        }

        [Test]
        public void Insert_InvalidJson_Throws()
        {
            var t = Sample();
            t.AttachmentsJson = "not-json";
            System.Action act = () => _repo.Insert(t);
            act.Should().Throw<SqliteException>();
        }

        [Test]
        public void GetPending_ReturnsOnlyQueued_OrderedByCreated()
        {
            _repo.Insert(Sample("TKT-SM-01-260507-0001"));
            int second = _repo.Insert(Sample("TKT-SM-01-260507-0002"));
            _repo.Insert(Sample("TKT-SM-01-260507-0003"));
            _repo.MarkSent(second);

            var pending = _repo.GetPending();
            pending.Should().HaveCount(2);
            pending.Should().OnlyContain(t => t.Status == "queued");
        }

        [Test]
        public void MarkSent_ClearsLastError()
        {
            int id = _repo.Insert(Sample());
            _repo.RecordRetryFailure(id, "5xx network");
            _repo.MarkSent(id);

            var t = _repo.GetById(id);
            t.Status.Should().Be("sent");
            t.SentAt.Should().NotBeNullOrEmpty();
            t.LastError.Should().BeNull();
        }

        [Test]
        public void MarkFailed_StoresShortErrorOnly()
        {
            int id = _repo.Insert(Sample());
            _repo.MarkFailed(id, "400 invalid");

            var t = _repo.GetById(id);
            t.Status.Should().Be("failed");
            t.LastError.Should().Be("400 invalid");
        }

        [Test]
        public void RecordRetryFailure_IncrementsAttempts()
        {
            int id = _repo.Insert(Sample());
            _repo.RecordRetryFailure(id, "5xx network");
            _repo.RecordRetryFailure(id, "5xx network");
            _repo.RecordRetryFailure(id, "5xx network");

            var t = _repo.GetById(id);
            t.SyncAttempts.Should().Be(3);
            t.Status.Should().Be("queued");
        }

        [Test]
        public void CountPendingForRegister_PerRegisterIsolation()
        {
            _repo.Insert(Sample("TKT-SM-01-260507-0001"));
            _repo.Insert(Sample("TKT-SM-01-260507-0002"));
            var other = Sample("TKT-SM-02-260507-0001");
            other.RegisterId = "02";
            _repo.Insert(other);

            _repo.CountPendingForRegister("01").Should().Be(2);
            _repo.CountPendingForRegister("02").Should().Be(1);
        }
    }
}
