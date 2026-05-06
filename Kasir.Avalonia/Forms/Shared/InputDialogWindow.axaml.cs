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

public partial class InputDialogWindow : Window
{
    private readonly TextBox[] _inputs;
    public bool Accepted { get; private set; }
    public string[] Values { get; private set; } = [];

    public InputDialogWindow(string title, string[] labels, string[] defaults)
    {
        InitializeComponent();
        Title = title;
        TitleBlock.Text = title.ToUpperInvariant();
        _inputs = new TextBox[labels.Length];

        var dimBrush = ThemeResources.Brush("FgDimBrush");
        var inputBg = ThemeResources.Brush("Bg1Brush");
        var fgPrimary = ThemeResources.Brush("FgPrimaryBrush");

        for (int i = 0; i < labels.Length; i++)
        {
            var lbl = new TextBlock
            {
                Text = labels[i] + ":",
                Foreground = dimBrush,
                FontFamily = new global::Avalonia.Media.FontFamily(ThemeConstants.FontFamily),
                FontSize = ThemeConstants.FontSize
            };

            var tb = new TextBox
            {
                Text = (defaults != null && i < defaults.Length) ? defaults[i] ?? "" : "",
                Background = inputBg,
                Foreground = fgPrimary,
                FontFamily = new global::Avalonia.Media.FontFamily(ThemeConstants.FontFamily),
                FontSize = ThemeConstants.FontSize,
                Height = 30
            };
            _inputs[i] = tb;

            bool isRupiah = labels[i].Contains("Rp", StringComparison.OrdinalIgnoreCase);
            if (isRupiah)
            {
                bool reentry = false;
                tb.TextChanged += (_, _) =>
                {
                    if (reentry) return;
                    var raw = tb.Text ?? "";
                    var digits = new string(raw.Where(char.IsDigit).ToArray());
                    if (string.IsNullOrEmpty(digits))
                    {
                        reentry = true; tb.Text = ""; reentry = false; return;
                    }
                    if (!long.TryParse(digits, out long val)) return;
                    var formatted = Formatting.FormatRupiahInput(val);
                    if (formatted == raw) return;
                    reentry = true;
                    tb.Text = formatted;
                    tb.CaretIndex = formatted.Length;
                    reentry = false;
                };
            }

            FieldPanel.Children.Add(lbl);
            FieldPanel.Children.Add(tb);
        }

        BtnOk.Click += (_, _) => Accept();
        BtnCancel.Click += (_, _) => { Accepted = false; Close(); };

        this.Opened += (_, _) =>
        {
            if (_inputs.Length > 0)
            {
                _inputs[0].Focus();
                _inputs[0].SelectAll();
            }
        };
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (KeyboardRouter.IsEscape(e)) { Accepted = false; Close(); }
        if (KeyboardRouter.IsEnter(e)) Accept();
    }

    private void Accept()
    {
        Values = new string[_inputs.Length];
        for (int i = 0; i < _inputs.Length; i++)
            Values[i] = _inputs[i].Text?.Trim() ?? "";
        Accepted = true;
        Close();
    }

    public static async Task<(bool ok, string[] values)> Show(
        Visual? owner, string title, string[] labels, string[] defaults)
    {
        var dlg = new InputDialogWindow(title, labels, defaults);
        var w = owner is Window win ? win : TopLevel.GetTopLevel(owner) as Window;
        await dlg.ShowDialog(w!);
        return (dlg.Accepted, dlg.Values);
    }
}
