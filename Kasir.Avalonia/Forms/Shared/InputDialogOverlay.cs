using System;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Kasir.Avalonia.Infrastructure;
using Kasir.Utils;

namespace Kasir.Avalonia.Forms.Shared;

public class InputDialogOverlay : UserControl
{
    private readonly TextBox[] _inputs;
    private readonly TaskCompletionSource<(bool ok, string[] values)> _tcs = new();

    public InputDialogOverlay(string title, string[] labels, string[] defaults)
    {
        _inputs = new TextBox[labels.Length];
        Background = new SolidColorBrush(Color.FromArgb(0xCC, 0x0F, 0x14, 0x19));
        Focusable = true;
        HorizontalAlignment = HorizontalAlignment.Stretch;
        VerticalAlignment = VerticalAlignment.Stretch;

        var fontFamily = new global::Avalonia.Media.FontFamily(ThemeConstants.FontFamily);

        var card = new Border
        {
            Width = 500,
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
            FontSize = 18,
            FontWeight = FontWeight.Bold,
            Foreground = ThemeResources.Brush("FgPrimaryBrush"),
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 14),
        };
        DockPanel.SetDock(titleBlock, Dock.Top);
        dock.Children.Add(titleBlock);

        var btnPanel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Spacing = 10,
            Margin = new Thickness(0, 12, 0, 0),
        };
        DockPanel.SetDock(btnPanel, Dock.Bottom);
        var btnOk = new Button
        {
            Content = "OK",
            Width = 90,
            Height = 32,
            FontFamily = fontFamily,
            FontSize = 13,
            Background = ThemeResources.Brush("BgSelectedBrush"),
            Foreground = ThemeResources.Brush("FgPrimaryBrush"),
        };
        var btnCancel = new Button
        {
            Content = "Batal",
            Width = 90,
            Height = 32,
            FontFamily = fontFamily,
            FontSize = 13,
            Background = ThemeResources.Brush("AccentBgBrush"),
            Foreground = ThemeResources.Brush("DangerBrush"),
        };
        btnOk.Click += (_, _) => Accept();
        btnCancel.Click += (_, _) => Cancel();
        btnPanel.Children.Add(btnOk);
        btnPanel.Children.Add(btnCancel);
        dock.Children.Add(btnPanel);

        var fieldPanel = new StackPanel { Spacing = 6 };
        for (int i = 0; i < labels.Length; i++)
        {
            var lbl = new TextBlock
            {
                Text = labels[i] + ":",
                Foreground = ThemeResources.Brush("FgDimBrush"),
                FontFamily = fontFamily,
                FontSize = ThemeConstants.FontSize,
            };
            var tb = new TextBox
            {
                Text = (defaults != null && i < defaults.Length) ? defaults[i] ?? "" : "",
                Background = ThemeResources.Brush("Bg1Brush"),
                Foreground = ThemeResources.Brush("FgPrimaryBrush"),
                FontFamily = fontFamily,
                FontSize = ThemeConstants.FontSize,
                Height = 30,
            };

            if (labels[i].Contains("Rp", StringComparison.OrdinalIgnoreCase))
            {
                bool reentry = false;
                var captured = tb;
                captured.TextChanged += (_, _) =>
                {
                    if (reentry) return;
                    var raw = captured.Text ?? "";
                    var digits = new string(raw.Where(char.IsDigit).ToArray());
                    if (string.IsNullOrEmpty(digits))
                    {
                        reentry = true; captured.Text = ""; reentry = false; return;
                    }
                    if (!long.TryParse(digits, out long val)) return;
                    var formatted = Formatting.FormatRupiahInput(val);
                    if (formatted == raw) return;
                    reentry = true;
                    captured.Text = formatted;
                    captured.CaretIndex = formatted.Length;
                    reentry = false;
                };
            }

            _inputs[i] = tb;
            fieldPanel.Children.Add(lbl);
            fieldPanel.Children.Add(tb);
        }
        dock.Children.Add(fieldPanel);

        card.Child = dock;
        Content = card;

        AttachedToVisualTree += (_, _) =>
        {
            if (_inputs.Length > 0)
            {
                _inputs[0].Focus();
                _inputs[0].SelectAll();
            }
        };

        KeyDown += (_, e) =>
        {
            if (KeyboardRouter.IsEscape(e)) { e.Handled = true; Cancel(); }
            else if (KeyboardRouter.IsEnter(e)) { e.Handled = true; Accept(); }
        };
    }

    private void Accept()
    {
        var vals = new string[_inputs.Length];
        for (int i = 0; i < _inputs.Length; i++)
            vals[i] = _inputs[i].Text?.Trim() ?? "";
        _tcs.TrySetResult((true, vals));
    }

    private void Cancel()
    {
        _tcs.TrySetResult((false, Array.Empty<string>()));
    }

    public Task<(bool ok, string[] values)> Result => _tcs.Task;
}
