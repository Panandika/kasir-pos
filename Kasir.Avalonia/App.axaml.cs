using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Kasir.Avalonia.Infrastructure;
using Kasir.Data;
using Kasir.Data.Repositories;
using Kasir.Hardware;

namespace Kasir.Avalonia;

public partial class App : Application
{
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        // Apply persisted theme variant before opening MainWindow to avoid unstyled flash.
        ThemeService.Current.LoadAndApplyAtStartup();

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.MainWindow = new ShellWindow();
        }

        // Footer status models — best-effort, must not block UI thread.
        PrinterStatusModel.Current.Start(BuildPrinter);
        UpdateStatusModel.Current.Start();
        CloudSyncStatusModel.Current.Start();

        base.OnFrameworkInitializationCompleted();
    }

    private static IReceiptPrinter? BuildPrinter()
    {
        try
        {
            var conn = DbConnection.GetConnection();
            return new ReceiptPrinter(new ConfigRepository(conn));
        }
        catch
        {
            return null;
        }
    }
}