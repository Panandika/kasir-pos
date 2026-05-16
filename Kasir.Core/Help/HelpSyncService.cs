using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using Kasir.Data.Repositories;

namespace Kasir.Help
{
    /// <summary>
    /// Drains queued help_tickets to the Bantuan Edge Function. Standalone from
    /// CloudSync's OutboxRouter — different transport (HTTPS, not Npgsql) and
    /// different payload shape make a shared sink impractical.
    ///
    /// Loop: every <see cref="PollInterval"/>, fetch a batch of pending tickets,
    /// POST each one, mark sent/failed/retry per the result.
    /// </summary>
    public class HelpSyncService
    {
        private readonly SqliteConnection _db;
        private readonly IHelpReportClient _client;

        public TimeSpan PollInterval { get; set; } = TimeSpan.FromSeconds(15);
        public int BatchSize { get; set; } = 10;
        public int MaxAttemptsBeforeFail { get; set; } = 8;

        public HelpSyncService(SqliteConnection db, IHelpReportClient client)
        {
            _db = db;
            _client = client;
        }

        /// <summary>
        /// Drain one batch synchronously. Returns count of tickets that hit a
        /// terminal state (sent OR failed) this tick. Transient retries are
        /// not counted.
        /// </summary>
        public async Task<int> TickAsync(CancellationToken ct)
        {
            var repo = new HelpTicketRepository(_db);
            var pending = repo.GetPending(BatchSize);
            if (pending.Count == 0) return 0;

            int terminal = 0;
            foreach (var t in pending)
            {
                if (ct.IsCancellationRequested) break;
                SendResult result;
                try
                {
                    result = await _client.SendAsync(t, ct).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    result = SendResult.Transient(ex.GetType().Name);
                }

                switch (result.Outcome)
                {
                    case SendOutcome.Sent:
                        repo.MarkSent(t.Id);
                        terminal++;
                        break;
                    case SendOutcome.Failed:
                        repo.MarkFailed(t.Id, result.ShortError);
                        terminal++;
                        break;
                    case SendOutcome.TransientError:
                        repo.RecordRetryFailure(t.Id, result.ShortError);
                        // Sticky failure after too many transient retries
                        // moves the ticket out of the active queue.
                        var refreshed = repo.GetById(t.Id);
                        if (refreshed != null && refreshed.SyncAttempts >= MaxAttemptsBeforeFail)
                        {
                            repo.MarkFailed(t.Id, "max_retries:" + result.ShortError);
                            terminal++;
                        }
                        break;
                }
            }
            return terminal;
        }

        /// <summary>
        /// Long-running drain loop suitable for a hosted background task.
        /// Sleeps PollInterval between ticks; exits cleanly when ct is signaled.
        /// </summary>
        public async Task RunAsync(CancellationToken ct)
        {
            while (!ct.IsCancellationRequested)
            {
                try
                {
                    await TickAsync(ct).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch
                {
                    // Swallow transient infrastructure errors — next tick will retry.
                }
                try { await Task.Delay(PollInterval, ct).ConfigureAwait(false); }
                catch (OperationCanceledException) { break; }
            }
        }
    }
}
