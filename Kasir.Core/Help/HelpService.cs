using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using Kasir.Data.Repositories;
using Kasir.Help.KnowledgeBase;
using Kasir.Models;

namespace Kasir.Help
{
    /// <summary>
    /// Top-level orchestrator for Bantuan UI:
    ///   - AskAsync(query) → hybrid retrieval result for TANYA mode
    ///   - ReportAsync(category, body, ...) → enqueues a help_tickets row
    ///     and returns the locally-generated ticket number (drained later by
    ///     HelpSyncService).
    /// </summary>
    public class HelpService
    {
        private readonly SqliteConnection _db;
        private readonly HybridRetriever _retriever;
        private readonly ContextCollector _context;
        private readonly TicketNumberGenerator _ticketNumbers;
        private readonly string _storeId;

        public HelpService(
            SqliteConnection db,
            HybridRetriever retriever,
            ContextCollector context,
            TicketNumberGenerator ticketNumbers,
            string storeId)
        {
            _db = db;
            _retriever = retriever;
            _context = context;
            _ticketNumbers = ticketNumbers;
            _storeId = storeId;
        }

        public Task<RetrievalResult> AskAsync(string query, string registerId, CancellationToken ct)
            => _retriever.RetrieveAsync(query, registerId, ct);

        public string Report(
            string category,
            string body,
            string registerId,
            string cashierId,
            string appVersion,
            string lastInvoice,
            string lastError)
        {
            string attachments = _context.BuildAttachmentsJson(
                _storeId, registerId, appVersion, lastInvoice, lastError);

            string ticketNo = _ticketNumbers.Next();

            var ticket = new HelpTicket
            {
                TicketNo = ticketNo,
                StoreId = _storeId,
                RegisterId = registerId,
                CashierId = cashierId,
                Category = category,
                Body = body,
                AttachmentsJson = attachments
            };

            new HelpTicketRepository(_db).Insert(ticket);
            return ticketNo;
        }
    }
}
