using System;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.LogicalTree;
using Avalonia.Media;
using Avalonia.Threading;
using Kasir.Auth;
using Kasir.Avalonia.Forms.Shared;
using Kasir.Avalonia.Infrastructure;
using Kasir.Avalonia.Navigation;
using Kasir.Services;
using Lucide.Avalonia;
using Microsoft.Data.Sqlite;

namespace Kasir.Avalonia.Forms.Purchasing;

/// <summary>
/// WP-05: puts the purchasing lock in front of a purchasing screen. While
/// config.purchasing_locked is on, the form is hidden and disabled and a banner says
/// "Pembelian sekarang lewat dashboard." with the emergency unlock link under it
/// (owner password). Esc still goes back. Used by all four purchasing views.
/// </summary>
public sealed class PurchasingLockGate
{
    public const string BannerName = "PurchasingLockBanner";
    public const string EmergencyButtonName = "PurchasingEmergencyUnlock";

    private readonly UserControl _view;
    private readonly Control _form;
    private readonly PurchasingLockService _lock;

    public Control Banner { get; }
    public Button EmergencyButton { get; }
    public bool IsLocked { get; private set; }

    private PurchasingLockGate(UserControl view, Control form, PurchasingLockService lockService, string screenName)
    {
        _view = view;
        _form = form;
        _lock = lockService;
        EmergencyButton = BuildEmergencyButton();
        Banner = BuildBanner(screenName, EmergencyButton);
    }

    /// <summary>
    /// Call at the end of the view constructor (after InitializeComponent). Wraps the
    /// view's content so the banner can cover it.
    /// </summary>
    public static PurchasingLockGate Attach(UserControl view, SqliteConnection db, string screenName)
    {
        var form = view.Content as Control
            ?? throw new InvalidOperationException("Purchasing view has no content to lock.");
        view.Content = null;

        var gate = new PurchasingLockGate(view, form, new PurchasingLockService(db), screenName);
        var host = new Panel();
        host.Children.Add(form);
        host.Children.Add(gate.Banner);
        view.Content = host;
        gate.Refresh();

        // Keyboard straight onto the unlock link so Enter works without a click.
        view.AttachedToVisualTree += (_, _) =>
            Dispatcher.UIThread.Post(() => { if (gate.IsLocked) gate.EmergencyButton.Focus(); },
                DispatcherPriority.Loaded);
        return gate;
    }

    /// <summary>Re-reads the flag and shows either the banner or the form.</summary>
    public void Refresh()
    {
        IsLocked = _lock.IsLocked;
        _form.IsVisible = !IsLocked;
        _form.IsEnabled = !IsLocked;
        Banner.IsVisible = IsLocked;
    }

    /// <summary>
    /// First line of the view's OnKeyDown. While locked, every form shortcut is
    /// swallowed and Esc goes back. Returns true when the key was taken.
    /// </summary>
    public bool HandleKey(KeyEventArgs e)
    {
        if (!IsLocked) return false;
        if (KeyboardRouter.IsEscape(e))
        {
            e.Handled = true;
            NavigationService.GoBack();
        }
        return true;
    }

    /// <summary>
    /// Last check before a save writes anything: true (and nothing must be saved)
    /// when purchasing is locked.
    /// </summary>
    public bool BlocksSave()
    {
        Refresh();
        return IsLocked;
    }

    private async void OnEmergencyClick()
    {
        bool opened = await PurchasingLockPrompt.UnlockAsync(_view, PurchasingLockService.SourceEmergency);
        if (!opened) return;
        Refresh();
        var first = _form.GetLogicalDescendants().OfType<DataGrid>().FirstOrDefault();
        first?.Focus();
    }

    private Button BuildEmergencyButton()
    {
        var text = new TextBlock
        {
            FontSize = 16,
            TextWrapping = TextWrapping.Wrap,
            Foreground = ThemeResources.Brush("BrandBrush"),
            TextDecorations = TextDecorations.Underline,
        };
        text.Inlines!.Add(new Run(PurchasingLockService.EmergencyLinkText));

        var button = new Button
        {
            Name = EmergencyButtonName,
            Content = text,
            MinHeight = 44,
            Padding = new Thickness(12, 8),
            HorizontalAlignment = HorizontalAlignment.Center,
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Cursor = new Cursor(StandardCursorType.Hand),
        };
        button.Click += (_, _) => OnEmergencyClick();
        return button;
    }

    private static Control BuildBanner(string screenName, Button emergencyButton)
    {
        var font = new global::Avalonia.Media.FontFamily(ThemeConstants.FontFamily);
        var stack = new StackPanel
        {
            Spacing = 14,
            MaxWidth = 640,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };
        stack.Children.Add(new LucideIcon
        {
            Kind = LucideIconKind.Lock,
            Size = 48,
            Foreground = ThemeResources.Brush("WarningBrush"),
            HorizontalAlignment = HorizontalAlignment.Center,
        });
        stack.Children.Add(new TextBlock
        {
            Text = screenName.ToUpperInvariant(),
            FontFamily = font,
            FontSize = 13,
            Foreground = ThemeResources.Brush("FgDimBrush"),
            HorizontalAlignment = HorizontalAlignment.Center,
        });
        stack.Children.Add(new TextBlock
        {
            Text = PurchasingLockService.LockedMessage,
            FontFamily = font,
            FontSize = 30,
            FontWeight = FontWeight.Bold,
            TextWrapping = TextWrapping.Wrap,
            TextAlignment = TextAlignment.Center,
            Foreground = ThemeResources.Brush("FgPrimaryBrush"),
            HorizontalAlignment = HorizontalAlignment.Center,
        });
        stack.Children.Add(new TextBlock
        {
            Text = PurchasingLockService.LockedDetail,
            FontFamily = font,
            FontSize = 16,
            TextWrapping = TextWrapping.Wrap,
            TextAlignment = TextAlignment.Center,
            Foreground = ThemeResources.Brush("FgSecondaryBrush"),
            HorizontalAlignment = HorizontalAlignment.Center,
        });
        stack.Children.Add(emergencyButton);
        stack.Children.Add(new TextBlock
        {
            Text = "Esc: Kembali",
            FontFamily = font,
            FontSize = 13,
            Foreground = ThemeResources.Brush("FgDimBrush"),
            HorizontalAlignment = HorizontalAlignment.Center,
        });

        return new Border
        {
            Name = BannerName,
            Background = ThemeResources.Brush("Bg0Brush"),
            Padding = new Thickness(24),
            Child = stack,
        };
    }
}

/// <summary>
/// Owner credential prompt for opening POS purchasing (lock banner and Utility menu).
/// </summary>
public static class PurchasingLockPrompt
{
    public static async Task<bool> UnlockAsync(Visual owner, string source)
    {
        string defaultUser = CurrentSession.User?.Username ?? "";
        var (ok, vals) = await InputDialogWindow.Show(owner,
            "Buka pembelian darurat (pemilik)",
            new[] { "Nama pengguna pemilik", "Kata sandi" },
            new[] { defaultUser, "" },
            new[] { false, true });
        if (!ok) return false;

        var result = new PurchasingLockService(Kasir.Data.DbConnection.GetConnection())
            .Unlock(vals[0], vals[1], source);
        await MsgBox.Show(owner, result.Message, result.Success ? "Pembelian dibuka" : "Gagal membuka");
        return result.Success;
    }
}
