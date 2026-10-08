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

    // A real key press: KeyDown, then the TextInput the OS sends for it (WM_CHAR), then
    // KeyUp. Press() above sends KeyDown only, which hides the TextInput leak (H1).
    private void PressChar(Key key, PhysicalKey physical, string ch)
    {
        _window.KeyPress(key, RawInputModifiers.None, physical, ch);
        _window.KeyTextInput(ch);
        _window.KeyRelease(key, RawInputModifiers.None, physical, ch);
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

    // Code "1" -> category picker (6 = LAIN-LAIN) -> price (PR-K4).
    private void AddMiscItem(string price)
    {
        Scan("1");
        PressChar(Key.D6, PhysicalKey.Digit6, "6");
        Scan(price);
    }

    private SaleItem OnlyCartLine()
    {
        var sales = (Kasir.Services.SalesService)typeof(SaleView)
            .GetField("_salesService", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(_view)!;
        Assert.That(sales.CurrentItems, Has.Count.EqualTo(1));
        return sales.CurrentItems[0];
    }

    [AvaloniaTest]
    public void Code1_AsksCategoryFirst_NumberKeyPicks_ThenPriceTypesStraightIntoCodeBox()
    {
        OpenSaleScreen(withShift: true);
        Scan("1");
        Assert.That(OverlayHost.Content, Is.InstanceOf<CategoryPickerOverlay>(), "code 1 asks for the category");

        PressChar(Key.D1, PhysicalKey.Digit1, "1"); // ALAT LISTRIK, no click needed
        Assert.That(OverlayHost.IsVisible, Is.False, "picker closes on the number key");
        Assert.That(Barcode.IsFocused, Is.True, "price is typed into the code box straight away");

        Scan("10000");
        var line = OnlyCartLine();
        Assert.That(line.ProductCode, Is.EqualTo("AL"));
        Assert.That(line.UnitPrice, Is.EqualTo(10000L * 100));
        Assert.That(line.Cogs, Is.EqualTo(7500L * 100), "estimated COGS at the default 25% margin");
        AssertBarcodeReady("after a category line");
    }

    [AvaloniaTest]
    public void TypedCategoryCode_GoesStraightToPrice()
    {
        OpenSaleScreen(withShift: true);
        Scan("pl");
        Assert.That(OverlayHost.IsVisible, Is.False, "a typed category code needs no picker");
        Scan("5000");
        var line = OnlyCartLine();
        Assert.That(line.ProductCode, Is.EqualTo("PL"));
        Assert.That(line.ProductName, Is.EqualTo("PLASTIK"));
    }

    [AvaloniaTest]
    public void F4_OpensCategoryPicker_NumpadPicks_EscCancels()
    {
        OpenSaleScreen(withShift: true);
        Press(Key.F4);
        Assert.That(OverlayHost.Content, Is.InstanceOf<CategoryPickerOverlay>());
        Press(Key.Escape);
        Assert.That(ItemCount, Is.EqualTo(0));
        AssertBarcodeReady("after cancelling the category picker");

        Press(Key.F4);
        PressChar(Key.NumPad5, PhysicalKey.NumPad5, "5"); // MAINAN
        Scan("2000");
        Assert.That(OnlyCartLine().ProductCode, Is.EqualTo("MY"));
    }

    [AvaloniaTest]
    public void CategoryPick_DigitTextInput_DoesNotLeakIntoThePrice()
    {
        // H1: KeyDown picks, the digit's TextInput arrives after it. It must not land in
        // the price box ("1" + "10000" = Rp 110.000).
        OpenSaleScreen(withShift: true);
        Scan("1");
        _window.KeyPress(Key.D1, RawInputModifiers.None, PhysicalKey.Digit1, "1");
        _window.KeyTextInput("1");
        Pump();
        Assert.That(OverlayHost.IsVisible, Is.False, "picker closed");
        Assert.That(Barcode.Text ?? "", Is.EqualTo(""), "the picking digit is swallowed");
        _window.KeyRelease(Key.D1, RawInputModifiers.None, PhysicalKey.Digit1, "1");
        Pump();
        Assert.That(Barcode.Text ?? "", Is.EqualTo(""));

        Scan("10000");
        var line = OnlyCartLine();
        Assert.That(line.ProductCode, Is.EqualTo("AL"));
        Assert.That(line.UnitPrice, Is.EqualTo(10000L * 100));
    }

    [AvaloniaTest]
    public void EnteringPriceMode_ClearsStaleTextInTheCodeBox()
    {
        OpenSaleScreen(withShift: true);
        Type("abc");
        Press(Key.F4);
        PressChar(Key.D2, PhysicalKey.Digit2, "2"); // ALAT TULIS
        Assert.That(Barcode.Text ?? "", Is.EqualTo(""));
        Scan("3000");
        Assert.That(OnlyCartLine().UnitPrice, Is.EqualTo(3000L * 100));
    }

    [AvaloniaTest]
    public void MiscPrice_MoreThanNineDigits_IsRejected_PromptStays()
    {
        // A barcode scanned into the price prompt must not become a huge price.
        OpenSaleScreen(withShift: true);
        Scan("1");
        PressChar(Key.D6, PhysicalKey.Digit6, "6");
        Scan("8991234567890");
        Assert.That(ItemCount, Is.EqualTo(0), "13 digits is a barcode, not a price");
        Scan("5000");
        Assert.That(OnlyCartLine().UnitPrice, Is.EqualTo(5000L * 100), "still waiting for the price");
    }

    [AvaloniaTest]
    public void F2Search_PickingACategoryRow_AsksForThePrice_NotARp0Line()
    {
        // M3: a category (or code "1") picked from name search goes to price entry.
        OpenSaleScreen(withShift: true);
        Press(Key.F2);
        typeof(SaleView).GetMethod("LoadSearchResults", BindingFlags.NonPublic | BindingFlags.Instance)!
            .Invoke(_view, new object[] { "PLASTIK" });
        var grid = _view!.FindControl<DataGrid>("DgvSearch")!;
        grid.SelectedItem = ((System.Collections.IEnumerable)grid.ItemsSource!).Cast<object>()
            .First(r => (string)r.GetType().GetProperty("Code")!.GetValue(r)! == "PL");
        Press(Key.Enter);

        Assert.That(ItemCount, Is.EqualTo(0), "no Rp 0 line");
        Scan("5000");
        var line = OnlyCartLine();
        Assert.That(line.ProductCode, Is.EqualTo("PL"));
        Assert.That(line.UnitPrice, Is.EqualTo(5000L * 100));
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

    // A barcode scanned into the Tunai box: the scanner types the digits and presses Enter
    // once. That must not finish the sale with a giant KEMBALI; a second Enter confirms.
    [AvaloniaTest]
    public void F5_Bayar_BarcodeInTunai_SingleEnterDoesNotCompleteSale()
    {
        OpenSaleScreen(withShift: true);
        AddMiscItem("10000");
        Press(Key.F5);
        var pay = (PaymentOverlay)OverlayHost.Content!;
        var label = pay.FindControl<TextBlock>("LblChange")!;

        Scan("8991234567890");
        Assert.That(ItemCount, Is.EqualTo(1), "one Enter from the scanner must not complete the sale");
        Assert.That(OverlayHost.Content, Is.SameAs(pay), "payment screen stays open");
        Assert.That(label.Text, Does.StartWith("YAKIN?"), "asks to confirm the huge change");

        Press(Key.Escape);
        Assert.That(OverlayHost.Content, Is.SameAs(pay), "Esc on the question goes back to editing, not out of payment");
        var txtCash = pay.FindControl<TextBox>("TxtCash")!;
        Assert.That(txtCash.IsFocused, Is.True, "cash box ready to correct");
        Type("20000");
        Assert.That(txtCash.Text, Is.EqualTo("20.000"), "typing replaces the wrong amount");
        Press(Key.Enter);
        Assert.That(ItemCount, Is.EqualTo(0), "normal change completes with one Enter");
        AssertBarcodeReady("after correcting the cash");
    }

    [AvaloniaTest]
    public void F5_Bayar_HugeChange_SecondEnterConfirms()
    {
        OpenSaleScreen(withShift: true);
        AddMiscItem("10000");
        Press(Key.F5);
        Type("2000000");
        Press(Key.Enter);
        Assert.That(ItemCount, Is.EqualTo(1), "first Enter only asks");
        Press(Key.Enter);
        Assert.That(ItemCount, Is.EqualTo(0), "second Enter completes the sale");
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
