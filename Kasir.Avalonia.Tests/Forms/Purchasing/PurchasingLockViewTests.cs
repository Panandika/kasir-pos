using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Headless;
using Avalonia.Headless.NUnit;
using Avalonia.Input;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Kasir.Avalonia.Forms;
using Kasir.Avalonia.Forms.Purchasing;
using Kasir.Avalonia.Navigation;
using Kasir.Data;
using Kasir.Data.Repositories;
using Kasir.Services;
using Microsoft.Data.Sqlite;
using NUnit.Framework;

namespace Kasir.Avalonia.Tests.Forms.Purchasing;

// WP-05: with the default config every purchasing screen shows the lock banner instead
// of the form; the owner can open it from the banner ("buka darurat"); it locks again
// when the app restarts.
[TestFixture]
[NonParallelizable]
public class PurchasingLockViewTests
{
    private static readonly string DataDir = Path.Combine(AppContext.BaseDirectory, "data");
    private ShellWindow _window = null!;

    public static IEnumerable<TestCaseData> Views()
    {
        yield return new TestCaseData((Func<UserControl>)(() => new PurchaseOrderView(1))).SetArgDisplayNames("PurchaseOrderView");
        yield return new TestCaseData((Func<UserControl>)(() => new GoodsReceiptView(1))).SetArgDisplayNames("GoodsReceiptView");
        yield return new TestCaseData((Func<UserControl>)(() => new PurchaseInvoiceView(1))).SetArgDisplayNames("PurchaseInvoiceView");
        yield return new TestCaseData((Func<UserControl>)(() => new ReturnView(1))).SetArgDisplayNames("ReturnView");
    }

    [TearDown]
    public void TearDown()
    {
        _window?.Close();
        ResetDbConnection(deleteData: true);
    }

    private static void ResetDbConnection(bool deleteData)
    {
        DbConnection.CloseConnection();
        SqliteConnection.ClearAllPools();
        typeof(DbConnection).GetProperty(nameof(DbConnection.IsInitialized))!.SetValue(null, false);
        typeof(DbConnection).GetField("_uiThreadId", BindingFlags.NonPublic | BindingFlags.Static)!.SetValue(null, 0);
        DbConnection.FirstRunHandler = null;
        if (deleteData && Directory.Exists(DataDir)) Directory.Delete(DataDir, true);
    }

    private static void Pump()
    {
        for (int i = 0; i < 3; i++) Dispatcher.UIThread.RunJobs();
    }

    private void Press(Key key)
    {
        _window.KeyPress(key, RawInputModifiers.None, PhysicalKey.None, null);
        Pump();
    }

    private void Type(string text)
    {
        _window.KeyTextInput(text);
        Pump();
    }

    private void StartApp()
    {
        ResetDbConnection(deleteData: true);
        _window = new ShellWindow();
        _window.Show();
        Dispatcher.UIThread.RunJobs();
        DbConnection.FirstRunHandler = () => new FirstRunResult { Choice = "seed" };
        DbConnection.InitializeDatabase();
    }

    // Simulates closing and reopening the app on the same kasir.db.
    private static void RestartApp()
    {
        ResetDbConnection(deleteData: false);
        DbConnection.InitializeDatabase();
    }

    private UserControl Open(Func<UserControl> make)
    {
        NavigationService.Navigate(new UserControl()); // something to go back to
        var view = make();
        NavigationService.Navigate(view);
        Pump();
        return view;
    }

    private static Control Banner(UserControl view) =>
        view.GetLogicalDescendants().OfType<Control>().Single(c => c.Name == PurchasingLockGate.BannerName);

    private static Button EmergencyButton(UserControl view) =>
        view.GetLogicalDescendants().OfType<Button>().Single(c => c.Name == PurchasingLockGate.EmergencyButtonName);

    private static Control Form(UserControl view) => ((Panel)view.Content!).Children[0];

    private static string AllText(Control root) => string.Join(" ",
        root.GetLogicalDescendants().OfType<TextBlock>()
            .Select(t => t.Text ?? string.Concat(t.Inlines?.OfType<Run>().Select(r => r.Text) ?? Array.Empty<string>())));

    private bool OverlayOpen => _window.IsOverlayOpen;

    [AvaloniaTest, TestCaseSource(nameof(Views))]
    public void purchasing_locked_blocks_form(Func<UserControl> make)
    {
        StartApp();
        var view = Open(make);

        Assert.That(Banner(view).IsVisible, Is.True, "lock banner shown by default");
        Assert.That(Form(view).IsVisible, Is.False, "form input area is closed");
        Assert.That(Form(view).IsEnabled, Is.False);
        Assert.That(AllText(Banner(view)), Does.Contain("Pembelian sekarang lewat dashboard."));
        Assert.That(AllText(EmergencyButton(view)), Does.Contain("Internet mati? Ketuk di sini untuk buka darurat."));

        // Form shortcuts do nothing while locked (no supplier / item / save dialogs).
        foreach (var key in new[] { Key.F2, Key.Insert, Key.F10 })
        {
            Press(key);
            Assert.That(OverlayOpen, Is.False, $"{key} must not open a dialog while locked");
        }

        // Esc still leaves the screen.
        Press(Key.Escape);
        Assert.That(_window.GetVisualDescendants().OfType<UserControl>().Contains(view), Is.False, "Esc goes back");
    }

    [AvaloniaTest, TestCaseSource(nameof(Views))]
    public void Unlocked_ShowsTheForm(Func<UserControl> make)
    {
        StartApp();
        new ConfigRepository(DbConnection.GetConnection()).Set(PurchasingLockService.ConfigKey, "false");

        var view = Open(make);

        Assert.That(Banner(view).IsVisible, Is.False);
        Assert.That(Form(view).IsVisible, Is.True);
        Assert.That(Form(view).IsEnabled, Is.True);

        // Control for the locked test: unlocked, F2 opens the supplier dialog.
        Press(Key.F2);
        Assert.That(OverlayOpen, Is.True, "F2 opens the supplier dialog when unlocked");
        Press(Key.Escape);
    }

    [AvaloniaTest, TestCaseSource(nameof(Views))]
    public void emergency_unlock_allows_purchasing(Func<UserControl> make)
    {
        StartApp();
        var view = Open(make);
        Assert.That(EmergencyButton(view).IsFocused, Is.True, "unlock link has the keyboard");

        // Enter on the link opens the owner prompt; the password box hides what is typed.
        Press(Key.Enter);
        var overlayHost = _window.FindControl<ContentControl>("OverlayHost")!;
        var boxes = overlayHost.GetVisualDescendants().OfType<TextBox>().ToList();
        Assert.That(boxes, Has.Count.EqualTo(2));
        Assert.That(boxes[1].PasswordChar, Is.EqualTo('*'));

        boxes[0].Text = "SM";       // seeded owner (admin role)
        boxes[1].Text = "74121";
        boxes[1].Focus();
        Pump();
        Press(Key.Enter);           // OK
        Press(Key.Enter);           // dismiss "Pembelian dibuka"

        Assert.That(OverlayOpen, Is.False);
        Assert.That(Banner(view).IsVisible, Is.False, "banner gone after the unlock");
        Assert.That(Form(view).IsVisible, Is.True);
        Assert.That(new PurchasingLockService(DbConnection.GetConnection()).History().First().Source,
            Is.EqualTo(PurchasingLockService.SourceEmergency));
    }

    [AvaloniaTest]
    public void EmergencyUnlock_WrongPassword_StaysLocked()
    {
        StartApp();
        var view = Open(() => new GoodsReceiptView(1));

        Press(Key.Enter);
        var boxes = _window.FindControl<ContentControl>("OverlayHost")!
            .GetVisualDescendants().OfType<TextBox>().ToList();
        boxes[0].Text = "SM";
        boxes[1].Text = "salah";
        boxes[1].Focus();
        Pump();
        Press(Key.Enter);
        Press(Key.Enter);

        Assert.That(Banner(view).IsVisible, Is.True);
        Assert.That(Form(view).IsVisible, Is.False);
    }

    [AvaloniaTest]
    public void Lock_ReengagesOnRestart()
    {
        StartApp();
        var unlock = new PurchasingLockService(DbConnection.GetConnection()).EmergencyUnlock("SM", "74121");
        Assert.That(unlock.Success, Is.True, unlock.Message);
        Assert.That(Banner(Open(() => new PurchaseOrderView(1))).IsVisible, Is.False, "open until restart");

        RestartApp();

        Assert.That(new PurchasingLockService(DbConnection.GetConnection()).IsLocked, Is.True);
        Assert.That(Banner(Open(() => new PurchaseOrderView(1))).IsVisible, Is.True, "locked again after restart");
    }

    [AvaloniaTest]
    public void UtilityMenu_ShowsTheLockState()
    {
        StartApp();
        var menu = new MainMenuView(1);
        NavigationService.ReplaceRoot(menu);
        Pump();
        Press(Key.U); // Utility

        Assert.That(AllText(menu), Does.Contain("Kunci Pembelian: TERKUNCI"));
    }
}
