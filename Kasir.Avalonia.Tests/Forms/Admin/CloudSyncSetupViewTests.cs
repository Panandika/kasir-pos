using System;
using System.IO;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.NUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Kasir.Avalonia.Forms.Admin;
using Kasir.Security;
using NUnit.Framework;

namespace Kasir.Avalonia.Tests.Forms.Admin;

// F01: the cloud sync screen is the owner's easy way to read the stored (encrypted)
// credentials back on the store PC: values are decrypted into the form, the password
// stays masked until F6 / the "Lihat" button, and the storage location is shown.
[TestFixture]
[NonParallelizable]
public class CloudSyncSetupViewTests
{
    private string _dir = "";

    [SetUp]
    public void SetUp()
    {
        _dir = Path.Combine(Path.GetTempPath(), "kasir-csv-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
        CloudSyncCredentialStore.DirectoryOverride = _dir;
    }

    [TearDown]
    public void TearDown()
    {
        CloudSyncCredentialStore.DirectoryOverride = null;
        try { Directory.Delete(_dir, true); } catch { }
    }

    private static (Window window, CloudSyncSetupView view) Show()
    {
        var view = new CloudSyncSetupView();
        var window = new Window { Content = view };
        window.Show();
        return (window, view);
    }

    [AvaloniaTest]
    public void StoredCreds_AreLoaded_PasswordMasked_F6Reveals_AndHidesAgain()
    {
        Assert.That(CloudSyncCredentialStore.TrySave(new CloudSyncCreds
        {
            Host = "pooler.example.com", Port = 6543, Database = "postgres",
            Username = "postgres.ref", Password = "pw-secret",
        }), Is.True);

        var (window, view) = Show();
        var pwd = view.FindControl<TextBox>("TxtPassword")!;
        Assert.That(view.FindControl<TextBox>("TxtHost")!.Text, Is.EqualTo("pooler.example.com"));
        Assert.That(pwd.Text, Is.EqualTo("pw-secret"));
        Assert.That(pwd.PasswordChar, Is.Not.EqualTo('\0'), "masked by default");

        view.FindControl<TextBox>("TxtHost")!.Focus();
        window.KeyPress(Key.F6, RawInputModifiers.None, PhysicalKey.F6, null);
        Assert.That(pwd.PasswordChar, Is.EqualTo('\0'), "F6 reveals");

        view.FindControl<Button>("BtnReveal")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Assert.That(pwd.PasswordChar, Is.Not.EqualTo('\0'), "button hides again");
    }

    [AvaloniaTest]
    public void StorageLocation_IsShown()
    {
        var (_, view) = Show();
        var text = view.FindControl<TextBlock>("LblStorage")!.Text;
        Assert.That(text, Does.Contain(CloudSyncCredentialStore.FilePath));
    }
}
