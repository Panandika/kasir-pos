using System;
using System.Net.Http;
using Avalonia.Threading;
using Microsoft.Data.Sqlite;
using Kasir.Data;
using Kasir.Data.Repositories;
using Kasir.Help;
using Kasir.Help.Auth;
using Kasir.Help.KnowledgeBase;
using Kasir.Avalonia.Diagnostics;
using Kasir.Avalonia.Forms.Shared;

namespace Kasir.Avalonia.Forms.Help;

/// <summary>
/// Singleton overlay manager for Bantuan. Owns one HelpService bound to the
/// app's SQLite connection and toggles the strip on/off via ShellWindow's
/// existing OverlayHost ContentControl.
///
/// Wiring is intentionally minimal v1:
///   - HelpAskClient is null (offline-only retrieval via FTS5).
///     When Supabase machine-auth is provisioned, swap to HttpHelpAskClient.
///   - HelpSyncService is NOT started here; tickets queue locally and an
///     operator runs the drainer manually until per-store machine accounts
///     are set up. Document follow-up in plan §9.
/// </summary>
public sealed class BantuanOverlayHost
{
    private static BantuanOverlayHost? _instance;
    public static BantuanOverlayHost Current => _instance ??= new BantuanOverlayHost();

    // Shared HttpClient — one per process, reused for all Bantuan HTTPS calls.
    private static readonly HttpClient _http = new HttpClient();

    private BantuanGlassStrip? _strip;
    private bool _open;

    public string StoreId { get; set; } = "sinar-makmur";
    public string StoreShort { get; set; } = "SM";
    public string RegisterId { get; set; } = "01";
    public string CashierId { get; set; } = "?";
    public string AppVersion { get; set; } = Kasir.Utils.AppVersion.Current;

    public string LastInvoice { get; set; } = "";
    public string LastError { get; set; } = "";

    public void Toggle(ShellWindow shell)
    {
        if (_open) Close(shell);
        else Open(shell);
    }

    public void Open(ShellWindow shell)
    {
        if (_open) return;

        // First-run / cloud-import screens run before the database exists. Opening the
        // connection there threw out of the Ctrl+/ key handler and killed the app (and
        // could create an empty kasir.db). Explain instead.
        if (!DbConnection.IsInitialized)
        {
            ShowUnavailable(shell,
                "database belum siap.\n\nSelesaikan dulu pendaftaran register ini " +
                "(Daftarkan dari cloud, atau pilih database), lalu tekan Ctrl+/ lagi.\n\n" +
                "Untuk bantuan pendaftaran: hubungi pemilik / admin toko.");
            return;
        }

        try
        {
            OpenStrip(shell);
        }
        catch (Exception ex)
        {
            CrashLog.Write("Bantuan.Open", ex);
            _strip = null;
            _open = false;
            ShowUnavailable(shell, ex.Message + "\n\nDetail tersimpan di " + CrashLog.LogPath);
        }
    }

    private static void ShowUnavailable(ShellWindow shell, string reason)
    {
        _ = MsgBox.Show(shell, "Bantuan belum tersedia: " + reason, "Bantuan");
    }

    private void OpenStrip(ShellWindow shell)
    {
        var db = DbConnection.GetConnection();

        var faqRepo = new HelpFaqRepository(db);

        // Build remote IHelpAskClient when machine-auth config is available.
        // When config is missing or load fails, retriever falls back to FTS5-only.
        var config = HelpConfigLoader.TryLoad();
        IHelpAskClient? askClient = null;
        if (config != null)
        {
            var auth = SupabaseMachineAuth.Current;
            askClient = new HttpHelpAskClient(
                _http,
                $"{config.SupabaseUrl.TrimEnd('/')}/functions/v1/help-ask",
                config.AnonKey,
                auth.GetAccessTokenAsync)
            {
                Timeout = TimeSpan.FromSeconds(5) // PM3: cold-start budget
            };
        }
        var retriever = new HybridRetriever(faqRepo, askClient);
        var ticketGen = new TicketNumberGenerator(db, StoreShort, RegisterId);
        var collector = new ContextCollector(new PiiScrubber());
        var service = new HelpService(db, retriever, collector, ticketGen, StoreId);

        _strip = new BantuanGlassStrip();
        _strip.Configure(service, RegisterId, CashierId, AppVersion, LastInvoice, LastError);
        _strip.Closed += (_, _) => Close(shell);

        shell.ShowOverlay(_strip);
        _open = true;
        Dispatcher.UIThread.Post(() => _strip.FocusInput());
    }

    public void Close(ShellWindow shell)
    {
        if (!_open) return;
        shell.HideOverlay();
        _strip = null;
        _open = false;
    }
}
