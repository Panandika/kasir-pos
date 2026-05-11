using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using NUnit.Framework;
using FluentAssertions;
using Kasir.Data.Repositories;
using Kasir.Help;
using Kasir.Models;
using Kasir.Tests.TestHelpers;

namespace Kasir.Tests.Help
{
    [TestFixture]
    public class HelpSyncServiceTests
    {
        private SqliteConnection _db;
        private HelpTicketRepository _repo;
        private FakeHelpReportClient _client;
        private HelpSyncService _svc;

        [SetUp]
        public void SetUp()
        {
            _db = TestDb.Create();
            _repo = new HelpTicketRepository(_db);
            _client = new FakeHelpReportClient();
            _svc = new HelpSyncService(_db, _client) { MaxAttemptsBeforeFail = 3 };
        }

        [TearDown]
        public void TearDown()
        {
            _db.Close();
            _db.Dispose();
        }

        private static HelpTicket Sample(string ticketNo)
        {
            return new HelpTicket
            {
                TicketNo = ticketNo,
                StoreId = "sinar-makmur",
                RegisterId = "01",
                CashierId = "U1",
                Category = "hardware",
                Body = "Printer macet setelah cetak ke-3",
                AttachmentsJson = "{\"version\":\"2.4.1\"}"
            };
        }

        [Test]
        public async Task Tick_SendsPending_MarkSent()
        {
            int id = _repo.Insert(Sample("TKT-SM-01-260507-0001"));
            _client.Outcomes.Enqueue(SendResult.Ok());

            int n = await _svc.TickAsync(CancellationToken.None);

            n.Should().Be(1);
            _repo.GetById(id).Status.Should().Be("sent");
        }

        [Test]
        public async Task Tick_PermanentFailure_MarksFailedNoRetry()
        {
            int id = _repo.Insert(Sample("TKT-SM-01-260507-0001"));
            _client.Outcomes.Enqueue(SendResult.Bad("400 invalid"));

            await _svc.TickAsync(CancellationToken.None);

            var t = _repo.GetById(id);
            t.Status.Should().Be("failed");
            t.LastError.Should().Be("400 invalid");
            t.SyncAttempts.Should().Be(0); // permanent failures don't count as retries
        }

        [Test]
        public async Task Tick_TransientError_KeepsQueuedAndIncrementsAttempts()
        {
            int id = _repo.Insert(Sample("TKT-SM-01-260507-0001"));
            _client.Outcomes.Enqueue(SendResult.Transient("5xx server"));

            int n = await _svc.TickAsync(CancellationToken.None);

            n.Should().Be(0); // not terminal
            var t = _repo.GetById(id);
            t.Status.Should().Be("queued");
            t.SyncAttempts.Should().Be(1);
            t.LastError.Should().Be("5xx server");
        }

        [Test]
        public async Task Tick_TransientErrorRepeated_FlipsToFailedAtMaxAttempts()
        {
            int id = _repo.Insert(Sample("TKT-SM-01-260507-0001"));
            // 3 transient → flips to failed after 3rd attempt (MaxAttemptsBeforeFail=3)
            _client.Outcomes.Enqueue(SendResult.Transient("5xx"));
            _client.Outcomes.Enqueue(SendResult.Transient("5xx"));
            _client.Outcomes.Enqueue(SendResult.Transient("5xx"));

            await _svc.TickAsync(CancellationToken.None);
            await _svc.TickAsync(CancellationToken.None);
            await _svc.TickAsync(CancellationToken.None);

            var t = _repo.GetById(id);
            t.Status.Should().Be("failed");
            t.LastError.Should().StartWith("max_retries:");
            t.SyncAttempts.Should().Be(3);
        }

        [Test]
        public async Task Tick_NoPending_ReturnsZero()
        {
            int n = await _svc.TickAsync(CancellationToken.None);
            n.Should().Be(0);
        }

        [Test]
        public async Task Tick_BatchSize_LimitsRowsPerCall()
        {
            for (int i = 1; i <= 5; i++)
            {
                _repo.Insert(Sample("TKT-SM-01-260507-000" + i));
                _client.Outcomes.Enqueue(SendResult.Ok());
            }
            _svc.BatchSize = 2;

            int n = await _svc.TickAsync(CancellationToken.None);
            n.Should().Be(2);

            int remaining = _repo.GetPending().Count;
            remaining.Should().Be(3);
        }

        private class FakeHelpReportClient : IHelpReportClient
        {
            public Queue<SendResult> Outcomes { get; } = new Queue<SendResult>();

            public Task<SendResult> SendAsync(HelpTicket ticket, CancellationToken ct)
            {
                if (Outcomes.Count == 0) return Task.FromResult(SendResult.Transient("no fixture"));
                return Task.FromResult(Outcomes.Dequeue());
            }
        }
    }
}
