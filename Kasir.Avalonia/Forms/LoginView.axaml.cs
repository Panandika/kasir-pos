using System;
using Avalonia.Controls;
using Avalonia.Input;
using Kasir.Auth;
using Kasir.Data;
using Kasir.Utils;
using Kasir.Avalonia.Navigation;
using Kasir.Avalonia.Forms.Shared;

namespace Kasir.Avalonia.Forms;

public partial class LoginView : UserControl
{
    private AuthService? _auth;
    private bool _capsLock = false;

    public LoginView()
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
        TxtUsername.Focus();
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
            bool exit = await MsgBox.Confirm(NavigationService.Owner, "Keluar dari Kasir POS?");
            if (exit)
                NavigationService.Owner.Close();
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
            NavigationService.ReplaceRoot(new MainMenuView(_auth.CurrentUser?.Id ?? 1));
        }
        else
        {
            LblMessage.Text = result.ErrorMessage;
            TxtPassword.Text = "";
            TxtPassword.Focus();
        }
    }

    private void SetStatus(string text) => StatusLabel.Text = text;
}
