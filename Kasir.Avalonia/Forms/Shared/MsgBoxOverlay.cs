using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.Layout;
using Avalonia.Media;
using Kasir.Avalonia.Infrastructure;

namespace Kasir.Avalonia.Forms.Shared;

public class MsgBoxOverlay : UserControl
{
    private readonly TaskCompletionSource<bool> _tcs = new();

    public MsgBoxOverlay(string title, string message, bool showCancel)
    {
        Background = new SolidColorBrush(Color.FromArgb(0xCC, 0x0F, 0x14, 0x19));
        Focusable = true;
        HorizontalAlignment = HorizontalAlignment.Stretch;
        VerticalAlignment = VerticalAlignment.Stretch;

        var fontFamily = new global::Avalonia.Media.FontFamily(ThemeConstants.FontFamily);

        var card = new Border
        {
            Width = 480,
            Background = ThemeResources.Brush("Bg1Brush"),
            BorderBrush = ThemeResources.Brush("BorderSubtleBrush"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(12),
            Padding = new Thickness(24),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };

        var dock = new DockPanel { LastChildFill = true };

        var titleBlock = new TextBlock
        {
            Text = title.ToUpperInvariant(),
            FontFamily = fontFamily,
            FontSize = 16,
            FontWeight = FontWeight.Bold,
            Foreground = ThemeResources.Brush("FgPrimaryBrush"),
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 12),
        };
        DockPanel.SetDock(titleBlock, Dock.Top);
        dock.Children.Add(titleBlock);

        var btnPanel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Center,
            Spacing = 12,
            Margin = new Thickness(0, 14, 0, 0),
        };
        DockPanel.SetDock(btnPanel, Dock.Bottom);

        var btnYes = new Button
        {
            Content = "Ya / OK",
            Width = 100,
            Height = 34,
            FontFamily = fontFamily,
            FontSize = 13,
            Background = ThemeResources.Brush("BgSelectedBrush"),
            Foreground = ThemeResources.Brush("FgPrimaryBrush"),
        };
        var btnNo = new Button
        {
            Content = "Tidak / Batal",
            Width = 100,
            Height = 34,
            FontFamily = fontFamily,
            FontSize = 13,
            Background = ThemeResources.Brush("AccentBgBrush"),
            Foreground = ThemeResources.Brush("DangerBrush"),
            IsVisible = showCancel,
        };
        btnYes.Click += (_, _) => _tcs.TrySetResult(true);
        btnNo.Click += (_, _) => _tcs.TrySetResult(false);
        btnPanel.Children.Add(btnYes);
        btnPanel.Children.Add(btnNo);
        dock.Children.Add(btnPanel);

        var msg = new TextBlock
        {
            Text = message,
            FontFamily = fontFamily,
            FontSize = 13,
            Foreground = ThemeResources.Brush("FgPrimaryBrush"),
            TextWrapping = TextWrapping.Wrap,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };
        dock.Children.Add(msg);

        card.Child = dock;
        Content = card;

        // Enter = Ya, Esc = Tidak. The handler sits on the window in the tunnel phase while
        // the dialog is open: keys used to go to whatever had focus behind the overlay (e.g.
        // the sale screen's code box), so "Tinggalkan transaksi?" could not be answered by
        // keyboard. Focus is also moved to "Ya" once the dialog is actually laid out.
        _showCancel = showCancel;
        AttachedToVisualTree += (_, _) =>
        {
            _topLevel = TopLevel.GetTopLevel(this);
            _topLevel?.AddHandler(KeyDownEvent, OnWindowKeyDown, RoutingStrategies.Tunnel);
            Dispatcher.UIThread.Post(() => btnYes.Focus(), DispatcherPriority.Loaded);
        };
        DetachedFromVisualTree += (_, _) =>
        {
            _topLevel?.RemoveHandler(KeyDownEvent, OnWindowKeyDown);
            _topLevel = null;
        };
    }

    private readonly bool _showCancel;
    private TopLevel? _topLevel;

    private void OnWindowKeyDown(object? sender, KeyEventArgs e)
    {
        if (_tcs.Task.IsCompleted) return;
        if (KeyboardRouter.IsEnter(e)) { e.Handled = true; _tcs.TrySetResult(true); }
        // Esc = Tidak/Batal; on an info box (no cancel button) it just closes it.
        else if (KeyboardRouter.IsEscape(e)) { e.Handled = true; _tcs.TrySetResult(!_showCancel); }
    }

    public Task<bool> Result => _tcs.Task;
}
