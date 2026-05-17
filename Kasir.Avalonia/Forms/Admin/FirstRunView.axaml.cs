using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Kasir.Avalonia.Navigation;
using Kasir.Data;
using Kasir.Avalonia.Infrastructure;

namespace Kasir.Avalonia.Forms.Admin;

public partial class FirstRunView : UserControl
{
    private readonly TaskCompletionSource<FirstRunResult?> _tcs = new();

    public FirstRunView()
    {
        InitializeComponent();
        BtnSeed.Click   += (_, _) => _tcs.TrySetResult(new FirstRunResult { Choice = "seed" });
        BtnCancel.Click += (_, _) => _tcs.TrySetResult(null);
        BtnImport.Click += (_, _) => _ = OnImport();
        BtnCloud.Click  += (_, _) => _ = OnCloud();
    }

    private async Task OnCloud()
    {
        var view = new CloudImportView();
        NavigationService.Navigate(view);
        var result = await view.WaitForChoice();
        NavigationService.GoBack();
        if (result != null)
        {
            _tcs.TrySetResult(result);
        }
    }

    public Task<FirstRunResult?> WaitForChoice() => _tcs.Task;

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (KeyboardRouter.IsEscape(e))
        {
            e.Handled = true;
            _tcs.TrySetResult(null);
        }
    }

    private async Task OnImport()
    {
        var files = await NavigationService.Owner.StorageProvider.OpenFilePickerAsync(
            new FilePickerOpenOptions
            {
                Title = "Pilih database SQLite (kasir.db)",
                FileTypeFilter = new[]
                {
                    new FilePickerFileType("SQLite database (*.db)") { Patterns = new[] { "*.db" } },
                    new FilePickerFileType("Semua file") { Patterns = new[] { "*" } }
                }
            });

        if (files.Count == 0) return;

        LblStatus.Text = "Memvalidasi database...";
        LblStatus.Foreground = ThemeResources.Brush("SuccessBrush");

        var validation = DatabaseValidator.Validate(files[0].Path.LocalPath, runIntegrityCheck: true);
        if (!validation.IsValid)
        {
            LblStatus.Foreground = ThemeResources.Brush("DangerBrush");
            LblStatus.Text = "Database tidak valid:\n - " + string.Join("\n - ", validation.Errors);
            return;
        }

        _tcs.TrySetResult(new FirstRunResult { Choice = "import", ImportPath = files[0].Path.LocalPath });
    }
}
