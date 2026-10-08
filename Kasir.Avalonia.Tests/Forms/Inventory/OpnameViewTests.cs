using System;
using System.IO;
using System.Linq;
using System.Reflection;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.NUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Kasir.Avalonia.Forms.Inventory;
using Kasir.Avalonia.Navigation;
using Kasir.Data;
using Kasir.Data.Repositories;
using Kasir.Models;
using Microsoft.Data.Sqlite;
using NUnit.Framework;

namespace Kasir.Avalonia.Tests.Forms.Inventory;

// PR-K6: an opname line is stamped with its count time when the physical qty is entered;
// lines not counted show "belum dihitung" and are left alone on save.
[TestFixture]
[NonParallelizable]
public class OpnameViewTests
{
    private static readonly string DataDir = Path.Combine(AppContext.BaseDirectory, "data");
    private ShellWindow _window = null!;
    private OpnameView _view = null!;

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

    private void OpenOpname()
    {
        ResetDbConnection();
        _window = new ShellWindow();
        _window.Show();
        Dispatcher.UIThread.RunJobs();
        DbConnection.FirstRunHandler = () => new FirstRunResult { Choice = "seed" };
        DbConnection.InitializeDatabase();
        var repo = new ProductRepository(DbConnection.GetConnection());
        foreach (var (code, name) in new[] { ("OP1", "AAA OPNAME SATU"), ("OP2", "AAB OPNAME DUA") })
            repo.Insert(new Product
            {
                ProductCode = code, Name = name, Price = 100000, Status = "A",
                OpenPrice = "N", VatFlag = "N", LuxuryTaxFlag = "N", IsConsignment = "N"
            });
        _view = new OpnameView(1);
        NavigationService.Navigate(_view);
        Pump();
    }

    private DataGrid Grid => _view.FindControl<DataGrid>("DgvOpname")!;

    // OpnameRow is a private record: read its properties by reflection.
    private static string Prop(object row, string name) =>
        (string)row.GetType().GetProperty(name)!.GetValue(row)!;

    private System.Collections.Generic.List<object> Rows =>
        ((System.Collections.IEnumerable)Grid.ItemsSource!).Cast<object>().ToList();

    private (string Physical, string System, string CountedAt) Row(string code)
    {
        var r = Rows.Single(x => Prop(x, "Code") == code);
        return (Prop(r, "Physical"), Prop(r, "System"), Prop(r, "CountedAt"));
    }

    [AvaloniaTest]
    public void EnterPhysicalQty_StampsCountTime_OthersStayBelumDihitung()
    {
        OpenOpname();
        Press(Key.F3);
        Assert.That(Row("OP1").Physical, Is.EqualTo("belum dihitung"));
        Assert.That(Row("OP1").System, Is.EqualTo("-"), "system qty is not shown before the count");

        int idx = Rows.FindIndex(r => Prop(r, "Code") == "OP1");
        Grid.SelectedIndex = idx;
        Grid.Focus();
        Pump();
        Press(Key.Enter);
        var overlayHost = _window.FindControl<ContentControl>("OverlayHost")!;
        var input = overlayHost.GetVisualDescendants().OfType<TextBox>().First();
        Assert.That(input.IsFocused, Is.True, "qty box takes typing straight away");
        Type("7");
        Press(Key.Enter);

        Assert.That(Row("OP1").Physical, Is.EqualTo("7"));
        Assert.That(Row("OP1").CountedAt, Does.Match(@"^\d{2}:\d{2}:\d{2}$"), "count time is shown");
        Assert.That(Row("OP2").Physical, Is.EqualTo("belum dihitung"));
        Assert.That(Row("OP2").CountedAt, Is.EqualTo(""));
    }
}
