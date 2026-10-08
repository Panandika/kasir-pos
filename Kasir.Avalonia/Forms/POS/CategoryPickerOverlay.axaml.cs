using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input;
using Avalonia.Threading;
using Kasir.Services;

namespace Kasir.Avalonia.Forms.POS;

// PR-K4 category quick keys: code "1" (or F4) on the sale screen asks for the category
// first, then the price. Keys 1-6 (top row or numpad) pick, Esc cancels. Result is the
// category code (AL/AT/PR/PL/MY/LL) or null.
public partial class CategoryPickerOverlay : UserControl
{
    private readonly TaskCompletionSource<string?> _tcs = new();

    public CategoryPickerOverlay()
    {
        InitializeComponent();

        for (int i = 0; i < SalesService.CategoryKeys.Count; i++)
        {
            var (code, name) = SalesService.CategoryKeys[i];
            var button = new Button { Content = $"{i + 1}   {code}  {name}", Tag = code };
            button.Classes.Add("cat");
            button.Click += (_, _) => _tcs.TrySetResult(code);
            ButtonList.Children.Add(button);
        }

        // Focus now and again once laid out, so the first key typed reaches the picker.
        AttachedToVisualTree += (_, _) =>
        {
            Focus();
            Dispatcher.UIThread.Post(() => Focus(), DispatcherPriority.Loaded);
        };
        KeyDown += OnKey;
    }

    public Task<string?> Result => _tcs.Task;

    private void OnKey(object? sender, KeyEventArgs e)
    {
        if (KeyboardRouter.IsEscape(e)) { e.Handled = true; _tcs.TrySetResult(null); return; }
        int index = e.Key switch
        {
            >= Key.D1 and <= Key.D9 => e.Key - Key.D1,
            >= Key.NumPad1 and <= Key.NumPad9 => e.Key - Key.NumPad1,
            _ => -1,
        };
        if (index < 0 || index >= SalesService.CategoryKeys.Count) return;
        e.Handled = true;
        _tcs.TrySetResult(SalesService.CategoryKeys[index].Code);
    }

    public static async Task<string?> Show(Visual? owner)
    {
        ShellWindow? shell = TopLevel.GetTopLevel(owner) as ShellWindow;
        if (shell is null
            && Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            shell = desktop.MainWindow as ShellWindow;
        }
        if (shell is null) return null;

        var overlay = new CategoryPickerOverlay();
        shell.ShowOverlay(overlay);
        shell.UpdateLayout();
        overlay.Focus();
        try { return await overlay.Result; }
        finally { shell.HideOverlay(); }
    }
}
