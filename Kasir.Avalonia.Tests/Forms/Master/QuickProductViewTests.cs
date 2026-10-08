using System;
using System.IO;
using System.Reflection;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.NUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Kasir.Avalonia.Forms.Master;
using Kasir.Avalonia.Forms.Shared;
using Kasir.Avalonia.Navigation;
using Kasir.Data;
using Kasir.Data.Repositories;
using Kasir.Services;
using Microsoft.Data.Sqlite;
using NUnit.Framework;

namespace Kasir.Avalonia.Tests.Forms.Master;

// PR-K5 Barang Masuk Cepat: the whole form is filled from the keyboard (typing lands in
// the field without clicking, Enter moves on), and the new 9000-range code is shown.
[TestFixture]
[NonParallelizable]
public class QuickProductViewTests
{
    private static readonly string DataDir = Path.Combine(AppContext.BaseDirectory, "data");
    private ShellWindow _window = null!;
    private QuickProductView _view = null!;

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

    private void Pump()
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

    private void Open()
    {
        ResetDbConnection();
        _window = new ShellWindow();
        _window.Show();
        Dispatcher.UIThread.RunJobs();
        DbConnection.FirstRunHandler = () => new FirstRunResult { Choice = "seed" };
        DbConnection.InitializeDatabase();
        _view = new QuickProductView(1);
        NavigationService.Navigate(_view);
        Pump();
    }

    private TextBox Field(string name) => _view.FindControl<TextBox>(name)!;
    private ContentControl OverlayHost => _window.FindControl<ContentControl>("OverlayHost")!;

    [AvaloniaTest]
    public void KeyboardOnly_Entry_CreatesProductWithNextFreeCode()
    {
        Open();
        Assert.That(Field("TxtName").IsFocused, Is.True, "Nama takes typing straight away");

        Type("lampu tidur"); Press(Key.Enter);
        Assert.That(Field("TxtCategory").IsFocused, Is.True, "Enter moves to the next field");
        Type("1"); Press(Key.Enter);
        Type("15000"); Press(Key.Enter);
        Assert.That(Field("TxtCost").Text, Is.EqualTo("15.000"));
        Type("25000"); Press(Key.Enter);
        Type("3"); Press(Key.Enter);

        Assert.That(OverlayHost.Content, Is.InstanceOf<MsgBoxOverlay>(), "the new code is shown");
        var conn = DbConnection.GetConnection();
        var p = new ProductRepository(conn).GetByCode("9000");
        Assert.That(p, Is.Not.Null);
        Assert.That(p!.Name, Is.EqualTo("LAMPU TIDUR"));
        Assert.That(p.DeptCode, Is.EqualTo("42"));
        Assert.That(p.CostPrice, Is.EqualTo(15000L * 100));
        Assert.That(p.Price, Is.EqualTo(25000L * 100));
        Assert.That(new InventoryService(conn).GetStockOnHand("9000"), Is.EqualTo(3));

        Press(Key.Enter); // close the message
        Assert.That(OverlayHost.IsVisible, Is.False);
        Pump();
        Assert.That(Field("TxtName").IsFocused, Is.True, "ready for the next item");
        Assert.That(Field("TxtName").Text, Is.Empty);
    }

    [AvaloniaTest]
    public void MissingCategory_StopsWithMessage_NothingSaved()
    {
        Open();
        Type("gunting"); Press(Key.Enter);
        Press(Key.Enter); // empty category
        Press(Key.Enter); Press(Key.Enter); Press(Key.Enter); // walk to Qty and save
        Assert.That(OverlayHost.Content, Is.InstanceOf<MsgBoxOverlay>());
        Assert.That(new ProductRepository(DbConnection.GetConnection()).GetByCode("9000"), Is.Null);
    }
}
