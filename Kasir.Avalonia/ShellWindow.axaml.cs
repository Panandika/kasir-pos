using System;
using System.ComponentModel;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using Kasir.Data;
using Kasir.Help;
using Kasir.Help.Auth;
using Kasir.Avalonia.Navigation;
using Kasir.Avalonia.Forms;
using Kasir.Avalonia.Forms.Admin;
using Kasir.Avalonia.Diagnostics;
using Kasir.Avalonia.Infrastructure;
using Lucide.Avalonia;

namespace Kasir.Avalonia;

public partial class ShellWindow : Window
{
    private bool _firstOpen = true;
    private readonly CancellationTokenSource _shellCts = new CancellationTokenSource();
    private static readonly HttpClient _shellHttp = new HttpClient();

    public ShellWindow()
    {
        InitializeComponent();
        NavigationService.Initialize(this, ContentArea);
        UpdateThemeIcon();
        SyncStatusModel.Current.PropertyChanged += OnSyncStatusChanged;
        PrinterStatusModel.Current.PropertyChanged += OnPrinterStatusChanged;
        UpdateStatusModel.Current.PropertyChanged += OnUpdateStatusChanged;
        CloudSyncStatusModel.Current.PropertyChanged += OnCloudStatusChanged;
        UpdateSyncBadge();
        UpdatePrinterBadge();
        UpdateVersionBadge();
        UpdateCloudBadge();
    }

    public void ShowOverlay(Control content)
    {
        OverlayHost.Content = content;
        OverlayHost.IsVisible = true;
    }

    public void HideOverlay()
    {
        OverlayHost.IsVisible = false;
        OverlayHost.Content = null;
    }

    private void OnThemeTogglePressed(object? sender, RoutedEventArgs e)
    {
        ThemeService.Current.Toggle();
        UpdateThemeIcon();
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        // Check Ctrl+Shift+L BEFORE base so it isn't shadowed by other handlers.
        if (KeyboardRouter.IsCtrlShiftL(e))
        {
            ThemeService.Current.Toggle();
            UpdateThemeIcon();
            e.Handled = true;
            return;
        }
        if (KeyboardRouter.IsCtrlSlash(e))
        {
            Forms.Help.BantuanOverlayHost.Current.Toggle(this);
            e.Handled = true;
            return;
        }
        base.OnKeyDown(e);
    }

    private void UpdateThemeIcon()
    {
        if (ThemeToggleIcon is null) return;
        ThemeToggleIcon.Kind = ThemeService.Current.ActiveVariant == ThemeVariant.Dark
            ? LucideIconKind.MoonStar
            : LucideIconKind.SunMedium;
    }

    private void OnSyncStatusChanged(object? sender, PropertyChangedEventArgs e)
    {
        Dispatcher.UIThread.Post(UpdateSyncBadge);
    }

    private void OnPrinterStatusChanged(object? sender, PropertyChangedEventArgs e)
    {
        Dispatcher.UIThread.Post(UpdatePrinterBadge);
    }

    private void OnUpdateStatusChanged(object? sender, PropertyChangedEventArgs e)
    {
        Dispatcher.UIThread.Post(UpdateVersionBadge);
    }

    private void OnCloudStatusChanged(object? sender, PropertyChangedEventArgs e)
    {
        Dispatcher.UIThread.Post(UpdateCloudBadge);
    }

    private void UpdatePrinterBadge()
    {
        if (PrinterText is null || PrinterDot is null) return;
        var m = PrinterStatusModel.Current;
        PrinterText.Text = m.DisplayText;
        PrinterDot.IsVisible = m.ShowDot;
        if (m.ShowDot) PrinterDot.Fill = m.DotBrush;
    }

    private void UpdateVersionBadge()
    {
        if (VersionText is null || VersionDot is null || VersionSpinner is null) return;
        var m = UpdateStatusModel.Current;
        VersionText.Text = m.DisplayText;
        VersionText.Foreground = m.TextBrush;
        VersionSpinner.IsVisible = m.ShowSpinner;
        VersionDot.IsVisible = m.ShowDot;
        if (m.ShowDot) VersionDot.Fill = m.DotBrush;
    }

    private void UpdateCloudBadge()
    {
        if (CloudText is null || CloudDot is null || CloudSpinner is null) return;
        var m = CloudSyncStatusModel.Current;
        CloudText.Text = m.DisplayText;
        CloudText.Foreground = m.TextBrush;
        CloudSpinner.IsVisible = m.ShowSpinner;
        CloudDot.IsVisible = m.ShowDot;
        if (m.ShowDot) CloudDot.Fill = m.DotBrush;
    }

    private void UpdateSyncBadge()
    {
        if (SyncText is null || SyncDot is null || SyncSpinner is null) return;

        var model = SyncStatusModel.Current;
        SyncText.Text = model.DisplayText;

        bool isSyncing = model.State == SyncState.Syncing;
        SyncSpinner.IsVisible = isSyncing;
        SyncDot.IsVisible = !isSyncing && model.ShowDot;

        if (!isSyncing)
        {
            var brushKey = model.State switch
            {
                SyncState.OnlineRecent        => "SuccessBrush",
                SyncState.OnlineOverdue       => "WarningBrush",
                SyncState.OfflineTransactable => "WarningBrush",
                SyncState.OfflineBlocked      => "DangerBrush",
                _                             => "SuccessBrush"
            };
            if (Application.Current?.Resources.TryGetResource(brushKey, ActualThemeVariant, out var res) == true
                && res is IBrush brush)
            {
                SyncDot.Fill = brush;
            }
        }
    }

    protected override async void OnOpened(EventArgs e)
    {
        base.OnOpened(e);

        // Fullscreen on macOS/Linux requires deferring the state change until after
        // the window has been shown (see avaloniaui/Avalonia#4846, #7202). Setting it
        // in XAML or synchronously in OnOpened silently fails or is reverted.
        Dispatcher.UIThread.Post(() =>
        {
            WindowState = WindowState.FullScreen;
        }, DispatcherPriority.Background);

        // AppStartup: measure from process start to main window shown.
        Program.StartupWatch.Stop();
        PerfMetrics.Record(PerfMetrics.AppStartup, Program.StartupWatch.ElapsedMilliseconds);

        // FormOpen cold/warm: cold on first open, warm on subsequent.
        if (_firstOpen)
        {
            _firstOpen = false;
            PerfMetrics.Record(PerfMetrics.FormOpenCold, Program.StartupWatch.ElapsedMilliseconds);
        }
        else
        {
            PerfMetrics.Record(PerfMetrics.FormOpenWarm, Program.StartupWatch.ElapsedMilliseconds);
        }
        if (DbConnection.IsFreshInstall())
        {
            var firstRunView = new FirstRunView();
            NavigationService.Navigate(firstRunView);
            var result = await firstRunView.WaitForChoice();
            if (result == null) { Close(); return; }
            DbConnection.FirstRunHandler = () => result;
        }

        await Task.Run(() => DbConnection.InitializeDatabase());

        // Auto-start HelpSyncService to drain queued Bantuan tickets.
        // Fire-and-forget: never block shell startup. Graceful degradation if
        // help.json is missing — Bantuan still works offline (FTS5 + local queue).
        try
        {
            var config = HelpConfigLoader.TryLoad();
            if (config != null)
            {
                var auth = SupabaseMachineAuth.Current;
                var reportClient = new HttpHelpReportClient(
                    _shellHttp,
                    $"{config.SupabaseUrl.TrimEnd('/')}/functions/v1/help-report",
                    config.AnonKey,
                    auth.GetAccessTokenAsync);
                var syncService = new HelpSyncService(DbConnection.GetConnection(), reportClient);
                _ = syncService.RunAsync(_shellCts.Token);
            }
        }
        catch (Exception ex)
        {
            // Log but never block shell startup — graceful degradation principle.
            Console.Error.WriteLine($"HelpSyncService startup failed: {ex.Message}");
        }

        NavigationService.Navigate(new LoginView());
    }

    protected override void OnClosed(EventArgs e)
    {
        try { _shellCts.Cancel(); } catch { }
        base.OnClosed(e);
    }
}
