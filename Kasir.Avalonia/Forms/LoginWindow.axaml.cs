using System;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input;
using Kasir.Auth;
using Kasir.Data;
using Kasir.Utils;
using Kasir.Avalonia.Forms.Shared;

namespace Kasir.Avalonia.Forms;

public partial class LoginWindow : Window
{
    private AuthService _auth;
    private bool _capsLock = false;

    public LoginWindow()
    {
        InitializeComponent();
        _auth = new AuthService(DbConnection.GetConnection());
        LblVersion.Text = "v" + AppVersion.Current;
        BtnLogin.Click += (_, _) => AttemptLogin();
        TxtPassword.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Return)
                AttemptLogin();
        };
        SetStatus("Login — masukkan username dan password");
    }

    protected override async void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);

        if (e.Key == Key.CapsLock)
        {
            _capsLock = !_capsLock;
            LblCapsLock.IsVisible = _capsLock;
        }
        else if (KeyboardRouter.IsEscape(e))
        {
            e.Handled = true;
            bool exit = await MsgBox.Confirm(this, "Keluar dari Kasir POS?");
            if (exit)
            {
                if (global::Avalonia.Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
                    desktop.Shutdown();
                else
                    Close();
            }
        }
    }

    private async void AttemptLogin()
    {
        if (_auth == null)
            _auth = new AuthService(DbConnection.GetConnection());

        var result = _auth.Login(TxtUsername.Text?.ToUpper() ?? "", TxtPassword.Text ?? "");

        if (result.Success)
        {
            LblMessage.Text = "";
            var mainMenu = new MainMenuWindow(_auth.CurrentUser?.Id ?? 1);
            await mainMenu.ShowDialog(this);
            _auth.Logout();
            TxtPassword.Text = "";
            LblMessage.Text = "";
            TxtUsername.Focus();
        }
        else
        {
            LblMessage.Text = result.ErrorMessage;
            TxtPassword.Text = "";
            TxtPassword.Focus();
        }
    }

    private void SetStatus(string text)
    {
        StatusLabel.Text = text;
    }
}
