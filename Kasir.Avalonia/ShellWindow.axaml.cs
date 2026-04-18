using System;
using System.Threading.Tasks;
using Avalonia.Controls;
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
