using System;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Threading;
using Kasir.Data;
using Kasir.Avalonia.Navigation;
using Kasir.Avalonia.Forms;
using Kasir.Avalonia.Forms.Admin;

namespace Kasir.Avalonia;

public partial class ShellWindow : Window
{
    public ShellWindow()
    {
        InitializeComponent();
        NavigationService.Initialize(this, ContentArea);
    }

    protected override async void OnOpened(EventArgs e)
    {
        base.OnOpened(e);
        if (!OperatingSystem.IsWindows())
            WindowState = WindowState.Maximized;

        DbConnection.FirstRunHandler = () =>
        {
            FirstRunResult? result = null;
            Dispatcher.UIThread.InvokeAsync(async () =>
            {
                var dlg = new FirstRunWindow();
                await dlg.ShowDialog(this);
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

        await Task.Run(() => DbConnection.InitializeDatabase());
        NavigationService.Navigate(new LoginView());
    }
}
