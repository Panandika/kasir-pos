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
using Kasir.Auth;
using Kasir.Avalonia.Forms.POS;
using Kasir.Avalonia.Forms.Shared;
using Kasir.Avalonia.Navigation;
using Kasir.Data;
using Kasir.Data.Repositories;
using Kasir.Models;
using Microsoft.Data.Sqlite;
using NUnit.Framework;

namespace Kasir.Avalonia.Tests.Forms.POS;

// Owner reports from the v2.10.1 store test:
//  #14 code "1" (Barang Tanpa Kode) sold without an open shift, coded items did not.
//  #15/#16 opening the shift from the sale screen: amount box not typeable, and the
//          app stayed on the shift screen instead of going back to scanning.
//  #18 F5 Bayar: amount shown highlighted but typing did nothing until clicked.
//  #21 after a sale (or any dialog on the sale screen) the cursor was not back in the
//      code box: the cashier had to click it before the next scan.
[TestFixture]
[NonParallelizable]
public class SaleFlowTests
{
    private static readonly string DataDir = Path.Combine(AppContext.BaseDirectory, "data");
    private ShellWindow _window = null!;
    private SaleView? _view;

    [TearDown]
    public void TearDown()
    {
        if (_view != null)
        {
            foreach (var name in new[] { "_clockTimer", "_bannerTimer", "_debounce" })
                (typeof(SaleView).GetField(name, BindingFlags.NonPublic | BindingFlags.Instance)!
                    .GetValue(_view) as DispatcherTimer)?.Stop();
        }
        _window?.Close();
        CurrentSession.Clear();
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

    private static string RegisterId =>
        new ConfigRepository(DbConnection.GetConnection()).Get("register_id") ?? "01";

    private static void OpenShiftInDb()
    {
        new ShiftRepository(DbConnection.GetConnection()).OpenShiftAtomic(new Shift
        {
            RegisterId = RegisterId,
            CashierId = 1,
            OpenedAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
            OpeningCash = 0,
        }, DateTime.Now.ToString("yyyy-MM-dd"));
    }

    private SaleView OpenSaleScreen(bool withShift)
    {
        ResetDbConnection();
        // Shell first (fresh install: it parks on the first-run screen), then the database.
        _window = new ShellWindow();
        _window.Show();
        Dispatcher.UIThread.RunJobs();
        DbConnection.FirstRunHandler = () => new FirstRunResult { Choice = "seed" };
        DbConnection.InitializeDatabase();
        var conn = DbConnection.GetConnection();
        if (withShift) OpenShiftInDb();
        // No receipt printer in the test: a failed print would add its own message box.
        new ConfigRepository(conn).Set("printer_name", "");
        CurrentSession.User = new User { Id = 1, Alias = "TES" };
        _view = new SaleView(new AuthService(conn));
        NavigationService.Navigate(_view);
        Pump();
        return _view;
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

    private void Scan(string text)
    {
        Type(text);
        Press(Key.Enter);
    }

    private TextBox Barcode => _view!.FindControl<TextBox>("TxtBarcode")!;
    private ContentControl OverlayHost => _window.FindControl<ContentControl>("OverlayHost")!;
    private int ItemCount =>
        ((System.Collections.IEnumerable)_view!.FindControl<DataGrid>("DgvItems")!.ItemsSource!).Cast<object>().Count();

    private TextBox OverlayInput() => OverlayHost.GetVisualDescendants().OfType<TextBox>().First();

    private void AssertBarcodeReady(string because)
    {
        Assert.That(OverlayHost.IsVisible, Is.False, because + ": no dialog left open");
        Assert.That(Barcode.IsFocused, Is.True, because + ": the code box must have keyboard focus");
        Type("899");
        Assert.That(Barcode.Text, Is.EqualTo("899"), because + ": typing must land in the code box");
        Barcode.Text = "";
    }

    private void AddMiscItem(string price)
    {
        Scan("1");
        Scan(price);
    }

    [AvaloniaTest]
    public void NoShift_EnteringSale_AsksForOpeningCash_ThenScanIsReady()
    {
        OpenSaleScreen(withShift: false);

        Assert.That(OverlayHost.IsVisible, Is.True, "entering Penjualan without a shift asks to open one");
        var input = OverlayInput();
        Assert.That(input.IsFocused, Is.True, "the opening-cash box takes typing straight away");
        Type("100000");
        Assert.That(input.Text, Is.EqualTo("100.000"));
        Press(Key.Enter);

        var shift = new ShiftRepository(DbConnection.GetConnection()).GetOpenShift(RegisterId);
        Assert.That(shift, Is.Not.Null, "the shift is opened");
        Assert.That(shift!.OpeningCash, Is.EqualTo(100000L * 100));
        Assert.That(NavigationService.Instance.FindControl<ContentControl>("ContentArea")!.Content,
            Is.SameAs(_view), "stays on Penjualan (no detour to the shift screen)");
        AssertBarcodeReady("after opening the shift");
    }

    [AvaloniaTest]
    public void NoShift_Code1_IsBlockedLikeCodedItems()
    {
        OpenSaleScreen(withShift: false);
        Press(Key.Escape); // decline the opening prompt
        Assert.That(OverlayHost.IsVisible, Is.False);

        Scan("1");
        Assert.That(OverlayHost.IsVisible, Is.True, "code 1 must ask for the shift too");
        Press(Key.Escape);
        Scan("5000");
        Press(Key.Escape); // "5000" as a code asks for the shift again
        Assert.That(ItemCount, Is.EqualTo(0), "nothing can be sold without an open shift");
    }

    [AvaloniaTest]
    public void F5_Bayar_AmountTakesTyping_AndAfterSaleCodeBoxIsReady()
    {
        OpenSaleScreen(withShift: true);
        AddMiscItem("10000");
        Assert.That(ItemCount, Is.EqualTo(1));

        Press(Key.F5);
        Assert.That(OverlayHost.Content, Is.InstanceOf<PaymentOverlay>());
        var cash = OverlayHost.FindDescendantOfType<PaymentOverlay>() ?? (PaymentOverlay)OverlayHost.Content!;
        var txtCash = cash.FindControl<TextBox>("TxtCash")!;
        Assert.That(txtCash.IsFocused, Is.True, "the cash box takes typing straight away");
        Type("50000");
        Assert.That(txtCash.Text, Is.EqualTo("50.000"), "typing replaces the suggested amount");

        Press(Key.Enter);
        Assert.That(ItemCount, Is.EqualTo(0), "sale completed");
        AssertBarcodeReady("after a completed sale");
    }

    [AvaloniaTest]
    public void AfterSale_PrinterErrorMessage_ThenCodeBoxIsReady()
    {
        OpenSaleScreen(withShift: true);
        new ConfigRepository(DbConnection.GetConnection()).Set("printer_name", "PRINTER-TIDAK-ADA");
        AddMiscItem("10000");
        Press(Key.F5);
        Press(Key.Enter);

        var sw = System.Diagnostics.Stopwatch.StartNew();
        while (!(OverlayHost.Content is MsgBoxOverlay) && sw.Elapsed < TimeSpan.FromSeconds(10))
        {
            Pump();
            System.Threading.Thread.Sleep(20);
        }
        Assert.That(OverlayHost.Content, Is.InstanceOf<MsgBoxOverlay>(), "precondition: the print failure is reported");
        Press(Key.Enter);
        AssertBarcodeReady("after the receipt-printer message");
    }

    [AvaloniaTest]
    public void F5_Bayar_Escape_CodeBoxIsReady()
    {
        OpenSaleScreen(withShift: true);
        AddMiscItem("10000");
        Press(Key.F5);
        Assert.That(OverlayHost.IsVisible, Is.True);
        Press(Key.Escape);
        Assert.That(ItemCount, Is.EqualTo(1));
        AssertBarcodeReady("after cancelling payment");
    }

    [AvaloniaTest]
    public void F10_Batal_And_F8_Void_CodeBoxIsReady()
    {
        OpenSaleScreen(withShift: true);
        AddMiscItem("10000");
        Press(Key.F10);
        Press(Key.Enter);
        Assert.That(ItemCount, Is.EqualTo(0));
        AssertBarcodeReady("after F10 batal");

        AddMiscItem("10000");
        _view!.FindControl<DataGrid>("DgvItems")!.SelectedIndex = 0;
        Press(Key.F8);
        Press(Key.Enter);
        Assert.That(ItemCount, Is.EqualTo(0));
        AssertBarcodeReady("after F8 void");
    }

    [AvaloniaTest]
    public void UnknownCode_DoesNotReplaceSubtotal()
    {
        OpenSaleScreen(withShift: true);
        AddMiscItem("10000");
        var subtotal = _view!.FindControl<TextBlock>("LblSubtotal")!;
        string before = subtotal.Text!;

        Scan("5000"); // not a product code
        Assert.That(subtotal.Text, Is.EqualTo(before), "SUBTOTAL keeps showing the running total");
    }
}
