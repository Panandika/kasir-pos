using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using Kasir.Data;
using Kasir.Avalonia.Forms;
using Kasir.Avalonia.Forms.Admin;

namespace Kasir.Avalonia;

public partial class App : Application
{
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var loginWindow = new LoginWindow();
            desktop.MainWindow = loginWindow;

            DbConnection.FirstRunHandler = () =>
            {
                FirstRunResult? result = null;
                Dispatcher.UIThread.InvokeAsync(async () =>
                {
                    var dlg = new FirstRunWindow();
                    await dlg.ShowDialog(loginWindow);
                    result = new FirstRunResult
                    {
                        Choice = dlg.Choice == FirstRunChoice.Seed ? "seed"
                               : dlg.Choice == FirstRunChoice.Import ? "import"
                               : null,
                        ImportPath = dlg.ImportPath
                    };
                }).GetAwaiter().GetResult();
                return result!;
            };
        }

        base.OnFrameworkInitializationCompleted();
    }
}