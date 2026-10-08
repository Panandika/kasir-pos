using System;
using System.ComponentModel;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.VisualTree;
using System.Linq;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using Kasir.Data;
using Kasir.Help;
using Kasir.Help.Auth;
using Kasir.Utils;
using Kasir.Avalonia.Navigation;
using Kasir.Avalonia.Forms;
using Kasir.Avalonia.Forms.Admin;
using Kasir.Avalonia.Forms.Shared;
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
        PrinterStatusModel.Current.PropertyChanged += OnPrinterStatusChanged;
        UpdateStatusModel.Current.PropertyChanged += OnUpdateStatusChanged;
        CloudSyncStatusModel.Current.PropertyChanged += OnCloudStatusChanged;
        UpdatePrinterBadge();
        UpdateVersionBadge();
        UpdateCloudBadge();
    }

    public bool IsOverlayOpen => OverlayHost.IsVisible;

    // What had the keyboard before the first overlay opened (e.g. the sale code box), so
    // it gets it back when the overlay closes.
    private IInputElement? _focusBeforeOverlay;

    public void ShowOverlay(Control content)
    {
        if (!OverlayHost.IsVisible) _focusBeforeOverlay = FocusManager?.GetFocusedElement();
        OverlayHost.Content = content;
        OverlayHost.IsVisible = true;
        // Overlays focus their first field when attached, but at that point the field is
        // not in the visual tree yet and Focus() is ignored, leaving focus on the screen
        // behind (e.g. the sale code box): typing, Enter and Esc then went to that screen
        // instead of the dialog. Lay the overlay out now and move focus into it, so even
        // keys that arrive straight away (a barcode scanner) reach the dialog; check
        // again once loaded.
        UpdateLayout();
        EnsureFocusInside(content);
        Dispatcher.UIThread.Post(() => EnsureFocusInside(content), DispatcherPriority.Loaded);
    }

    private void EnsureFocusInside(Control overlay)
    {
        if (!ReferenceEquals(OverlayHost.Content, overlay)) return;
        if (FocusManager?.GetFocusedElement() is Visual focused && overlay.IsVisualAncestorOf(focused)) return;
        // Prefer the first input box over a button: a dialog that shows an input is
        // waiting for typing, and the OK button often comes first in the visual tree.
        var candidates = overlay.GetVisualDescendants()
            .OfType<InputElement>()
            .Where(c => c.Focusable && c.IsEffectivelyVisible && c.IsEffectivelyEnabled)
            .ToList();
        var target = candidates.OfType<TextBox>().FirstOrDefault() ?? candidates.FirstOrDefault();
        if (target is null) { overlay.Focus(); return; }
        ViewShortcuts.FocusInput(target);
    }

    public void HideOverlay()
    {
        OverlayHost.IsVisible = false;
        OverlayHost.Content = null;
        var previous = _focusBeforeOverlay;
        _focusBeforeOverlay = null;
        Dispatcher.UIThread.Post(() => RestoreFocusAfterOverlay(previous), DispatcherPriority.Loaded);
    }

    // The focused field was inside the overlay that just closed, so the keyboard went
    // nowhere and the cashier had to click the code box before the next scan. Give focus
    // back to what had it before the overlay, unless the screen already moved it.
    private void RestoreFocusAfterOverlay(IInputElement? previous)
    {
        if (OverlayHost.IsVisible) return; // another dialog opened meanwhile
        if (FocusManager?.GetFocusedElement() is Visual focused
            && focused.IsEffectivelyVisible && ContentArea.IsVisualAncestorOf(focused)) return;
        if (previous is InputElement prev && prev.IsEffectivelyVisible && prev.IsEffectivelyEnabled
            && ContentArea.IsVisualAncestorOf(prev))
        {
            prev.Focus();
            return;
        }
        (ContentArea.Content as InputElement)?.Focus();
    }

    // The "Ctrl+/ Bantuan" badge in the header used to be a hint only; clicking it
    // now does the same as the shortcut.
    private void OnBantuanHintPressed(object? sender, PointerPressedEventArgs e)
    {
        Forms.Help.BantuanOverlayHost.Current.Toggle(this);
        e.Handled = true;
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

        // Copying a cloud/imported database and running migrations can take a while on
        // the store PCs: show a busy screen (no buttons) instead of the first-run choices.
        NavigationService.ReplaceRoot(new PreparingDataView());

        try
        {
            await Task.Run(() => DbConnection.InitializeDatabase());
        }
        catch (Exception ex)
        {
            // Without a database the shell cannot continue: log, tell the operator what
            // to do (corrupt DB -> admin re-registers from cloud), then close.
            CrashLog.Write("ShellWindow.InitializeDatabase", ex);
            await MsgBox.Show(this, DatabaseInitFailureMessage.Build(ex, CrashLog.LogPath),
                "Database Bermasalah");
            Close();
            return;
        }

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

        NavigationService.ReplaceRoot(new LoginView());
    }

    protected override void OnClosed(EventArgs e)
    {
        try { _shellCts.Cancel(); } catch { }
        base.OnClosed(e);
    }
}
