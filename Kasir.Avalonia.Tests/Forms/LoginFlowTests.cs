using System;
using System.IO;
using System.Reflection;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.NUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Kasir.Avalonia.Forms;
using Kasir.Avalonia.Forms.Shared;
using Kasir.Avalonia.Navigation;
using Kasir.Data;
using Microsoft.Data.Sqlite;
using NUnit.Framework;

namespace Kasir.Avalonia.Tests.Forms;

// Keyboard flow on the login screen: cashiers coming from FoxPro press Enter
// to move to the next field, so Enter on Username must go to Password
// instead of leaving focus where the password would be typed into Username.
[TestFixture]
[NonParallelizable]
public class LoginFlowTests
{
    private static readonly string DataDir = Path.Combine(AppContext.BaseDirectory, "data");
    private ShellWindow _window = null!;
    private LoginView _view = null!;

    [TearDown]
    public void TearDown()
    {
        _window?.Close();
        ResetDbConnection();
    }

    private static void ResetDbConnection()
    {
        DbConnection.CloseConnection();
        SqliteConnection.ClearAllPools();
        typeof(DbConnection).GetProperty(nameof(DbConnection.IsInitialized))!.SetValue(null, false);
        typeof(DbConnection).GetField("_uiThreadId", BindingFlags.NonPublic | BindingFlags.Static)!.SetValue(null, 0);
        DbConnection.FirstRunHandler = null;
        if (Directory.Exists(DataDir)) Directory.Delete(DataDir, true);
    }

    private void OpenLogin()
    {
        ResetDbConnection();
        _window = new ShellWindow();
        _window.Show();
        Dispatcher.UIThread.RunJobs();
        DbConnection.FirstRunHandler = () => new FirstRunResult { Choice = "seed" };
        DbConnection.InitializeDatabase();
        _view = new LoginView();
        NavigationService.Navigate(_view);
        Pump();
    }

    private void Pump()
    {
        for (int i = 0; i < 3; i++) Dispatcher.UIThread.RunJobs();
    }

    private void Press(Key key)
    {
        _window.KeyPress(key, RawInputModifiers.None, PhysicalKey.None, null);
        _window.KeyRelease(key, RawInputModifiers.None, PhysicalKey.None, null);
        Pump();
    }

    private TextBox Username => _view.FindControl<TextBox>("TxtUsername")!;
    private TextBox Password => _view.FindControl<TextBox>("TxtPassword")!;

    [AvaloniaTest]
    public void Enter_OnUsername_MovesFocusToPassword()
    {
        OpenLogin();
        Assert.That(Username.IsFocused, Is.True, "login opens with Username focused");

        _window.KeyTextInput("TESTER");
        Pump();
        Press(Key.Return);

        Assert.That(Password.IsFocused, Is.True, "Enter on Username moves to Password");
        Assert.That(Username.Text, Is.EqualTo("TESTER"));

        _window.KeyTextInput("rahasia");
        Pump();
        Assert.That(Password.Text, Is.EqualTo("rahasia"), "next keys go into Password");
        Assert.That(Username.Text, Is.EqualTo("TESTER"), "Username is not polluted by the password");
    }

    [AvaloniaTest]
    public void Tab_OnUsername_StillMovesFocusToPassword()
    {
        OpenLogin();
        _window.KeyTextInput("TESTER");
        Pump();
        Press(Key.Tab);

        Assert.That(Password.IsFocused, Is.True, "Tab keeps working as before");
    }

    [AvaloniaTest]
    public void Enter_OnUsername_DoesNotAttemptLogin()
    {
        OpenLogin();
        _window.KeyTextInput("TESTER");
        Pump();
        Press(Key.Return);

        Assert.That(_view.FindControl<TextBlock>("LblMessage")!.Text ?? "", Is.Empty,
            "Enter on Username only moves focus; it does not submit an empty password");
        Assert.That(NavigationService.Owner, Is.Not.Null);
    }
}
