using System;
using System.Threading.Tasks;
using Avalonia.Controls;
using Kasir.Data;
using Kasir.Avalonia.Navigation;
using Kasir.Avalonia.Forms;
using Kasir.Avalonia.Forms.Admin;
using Kasir.Avalonia.Diagnostics;

namespace Kasir.Avalonia;

public partial class ShellWindow : Window
{
    private bool _firstOpen = true;

    public ShellWindow()
    {
        InitializeComponent();
        NavigationService.Initialize(this, ContentArea);
    }

    protected override async void OnOpened(EventArgs e)
    {
        base.OnOpened(e);

        // Force fullscreen on all platforms. The XAML WindowState="FullScreen"
        // is sometimes ignored on macOS until re-applied after the window is shown.
        WindowState = WindowState.FullScreen;

        // AppStartup: measure from process start to main window shown.
        Program.StartupWatch.Stop();
        PerfMetrics.Record(PerfMetrics.AppStartup, Program.StartupWatch.ElapsedMilliseconds);

        // FormOpen cold/warm: cold on first open, warm on subsequent.
        if (_firstOpen)
        {
            _firstOpen = false;
            PerfMetrics.Record(PerfMetrics.FormOpenCold, Program.StartupWatch.ElapsedMilliseconds);
        }
        else
        {
            PerfMetrics.Record(PerfMetrics.FormOpenWarm, Program.StartupWatch.ElapsedMilliseconds);
        }
        if (DbConnection.IsFreshInstall())
        {
            var firstRunView = new FirstRunView();
            NavigationService.Navigate(firstRunView);
            var result = await firstRunView.WaitForChoice();
            if (result == null) { Close(); return; }
            DbConnection.FirstRunHandler = () => result;
        }

        await Task.Run(() => DbConnection.InitializeDatabase());
        NavigationService.Navigate(new LoginView());
    }
}
