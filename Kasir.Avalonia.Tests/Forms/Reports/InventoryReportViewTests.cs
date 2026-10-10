using System;
using System.IO;
using System.Linq;
using System.Reflection;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.NUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Kasir.Avalonia.Forms.Reports;
using Kasir.Avalonia.Navigation;
using Kasir.Data;
using Microsoft.Data.Sqlite;
using NUnit.Framework;

namespace Kasir.Avalonia.Tests.Forms.Reports;

// stock_opname.qty_system / qty_actual are ledger qty (x100, QLAST N(13,2) x 100): the
// Stock Opname report must show whole units and a variance value with the scale divided out.
[TestFixture]
[NonParallelizable]
public class InventoryReportViewTests
{
    private static readonly string DataDir = Path.Combine(AppContext.BaseDirectory, "data");
    private ShellWindow _window = null!;
    private InventoryReportView _view = null!;

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

    private static string Prop(object row, string name) =>
        (string)row.GetType().GetProperty(name)!.GetValue(row)!;

    [AvaloniaTest]
    public void StockOpname_ShowsLedgerQtyAsUnits_AndScaledVarianceValue()
    {
        ResetDbConnection();
        _window = new ShellWindow();
        _window.Show();
        Dispatcher.UIThread.RunJobs();
        DbConnection.FirstRunHandler = () => new FirstRunResult { Choice = "seed" };
        DbConnection.InitializeDatabase();
        // 72 pcs on the system, 70 counted, HPP Rp 500.
        SqlHelper.ExecuteNonQuery(DbConnection.GetConnection(),
            @"INSERT INTO stock_opname (product_code, product_name, qty_system, qty_actual,
                cost_price, doc_date, period_code, account_code)
              VALUES ('OPR1', 'OPNAME REPORT', 7200, 7000, 50000, '2026-10-01', '202610', '110.001')");

        _view = new InventoryReportView(5);
        NavigationService.Navigate(_view);
        Pump();
        _view.FindControl<TextBox>("TxtDateFrom")!.Text = "2026-10-01";
        _view.FindControl<TextBox>("TxtDateTo")!.Text = "2026-10-31";
        _window.KeyPress(Key.F5, RawInputModifiers.None, PhysicalKey.None, null);
        Pump();

        var grid = _view.FindControl<DataGrid>("DgvReport")!;
        var row = ((System.Collections.IEnumerable)grid.ItemsSource!).Cast<object>()
            .Single(r => Prop(r, "C1") == "OPR1");
        Assert.That(Prop(row, "C3"), Is.EqualTo("72"), "Stok Sistem in units, not the x100 7200");
        Assert.That(Prop(row, "C4"), Is.EqualTo("70"), "Stok Fisik in units");
        Assert.That(Prop(row, "C5"), Is.EqualTo("-2"), "Selisih in units");

        var summary = _view.FindControl<TextBlock>("LblSummary")!.Text;
        Assert.That(summary, Does.Contain(Kasir.Utils.Formatting.FormatCurrency(-100000)),
            "Nilai Selisih: 2 pcs x Rp 500 = Rp -1.000");
    }
}
