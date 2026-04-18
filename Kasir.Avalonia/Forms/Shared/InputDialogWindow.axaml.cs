using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;

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
        Height = 80 + (labels.Length * 58) + 60;
        _inputs = new TextBox[labels.Length];

        for (int i = 0; i < labels.Length; i++)
        {
            var lbl = new TextBlock
            {
                Text = labels[i] + ":",
                Foreground = ThemeConstants.DisabledBrush,
                FontFamily = new global::Avalonia.Media.FontFamily(ThemeConstants.FontFamily),
                FontSize = ThemeConstants.FontSize
            };

            var tb = new TextBox
            {
                Text = (defaults != null && i < defaults.Length) ? defaults[i] ?? "" : "",
                Background = ThemeConstants.InputBackBrush,
                Foreground = ThemeConstants.ForegroundBrush,
                FontFamily = new global::Avalonia.Media.FontFamily(ThemeConstants.FontFamily),
                FontSize = ThemeConstants.FontSize,
                Height = 30
            };
            _inputs[i] = tb;

            FieldPanel.Children.Add(lbl);
            FieldPanel.Children.Add(tb);
        }

        BtnOk.Click += (_, _) => Accept();
        BtnCancel.Click += (_, _) => { Accepted = false; Close(); };
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
        Window owner, string title, string[] labels, string[] defaults)
    {
        var dlg = new InputDialogWindow(title, labels, defaults);
        await dlg.ShowDialog(owner);
        return (dlg.Accepted, dlg.Values);
    }
}
