using System;
using System.Collections.ObjectModel;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Input;
using Kasir.Avalonia.Behaviors;
using Kasir.Avalonia.Forms.Shared;
using Kasir.Avalonia.Infrastructure;
using Kasir.Avalonia.Navigation;
using Kasir.Avalonia.Utils;
using Kasir.Data;
using Kasir.Services;
using Kasir.Utils;

namespace Kasir.Avalonia.Forms.Master;

// PR-K5 "Barang Masuk Cepat": name, category, cost, price, qty -> new product with an
// internal code from 9000-9999 plus a PURCHASE stock movement at that cost. No label
// printing yet: the code is shown (dialog + list) so it can be written on the goods.
public partial class QuickProductView : UserControl
{
    private record AddedRow(string Code, string Name, string Category, string Qty, string Cost, string Price);

    private readonly ObservableCollection<AddedRow> _added = new();
    private readonly ProductService _service;
    private readonly int _userId;
    private readonly TextBox[] _fields;
    private bool _saving;

    public QuickProductView(int userId)
    {
        _userId = userId;
        InitializeComponent();
        _service = new ProductService(DbConnection.GetConnection(), new ClockImpl());
        DgvAdded.ItemsSource = _added;
        _fields = new[] { TxtName, TxtCategory, TxtCost, TxtPrice, TxtQty };

        NumericInputBehavior.AttachLiveFormatting(TxtCost);
        NumericInputBehavior.AttachLiveFormatting(TxtPrice);
        NumericInputBehavior.Attach(TxtQty);
        TxtCategory.TextChanged += (_, _) => ShowCategory();
        ShowCategory();

        FooterStatus.RegisterDefault(StatusLabel, "Barang Masuk Cepat — Enter: kolom berikut / simpan  F10: Simpan  Esc: Keluar");
        ViewShortcuts.AutoFocusOnAttach(this, TxtName);
    }

    // "1".."6" picks by number; a typed code (AL, at, ...) works too.
    private static string? ResolveCategory(string? typed)
    {
        string t = (typed ?? "").Trim();
        if (int.TryParse(t, out int n) && n >= 1 && n <= SalesService.CategoryKeys.Count)
            return SalesService.CategoryKeys[n - 1].Code;
        return SalesService.ResolveCategoryKey(t);
    }

    private static string CategoryName(string code) =>
        SalesService.CategoryKeys.First(k => k.Code == code).Name;

    private void ShowCategory()
    {
        string? code = ResolveCategory(TxtCategory.Text);
        LblCategory.Text = code != null
            ? $"{code} {CategoryName(code)}"
            : string.Join("  ", SalesService.CategoryKeys.Select((k, i) => $"{i + 1}={k.Name}"));
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (KeyboardRouter.IsEnter(e)) { e.Handled = true; NextFieldOrSave(); }
        else if (KeyboardRouter.IsF10(e)) { e.Handled = true; Save(); }
        else if (KeyboardRouter.IsEscape(e)) { e.Handled = true; NavigationService.GoBack(); }
    }

    private void NextFieldOrSave()
    {
        int idx = Array.FindIndex(_fields, f => f.IsFocused);
        if (idx >= 0 && idx < _fields.Length - 1) ViewShortcuts.FocusInput(_fields[idx + 1]);
        else Save();
    }

    private async void Save()
    {
        if (_saving) return;
        _saving = true;
        try
        {
            string? category = ResolveCategory(TxtCategory.Text);
            if (string.IsNullOrWhiteSpace(TxtName.Text)) { await Invalid("Nama barang harus diisi.", TxtName); return; }
            if (category == null) { await Invalid("Kategori: ketik angka 1-6.", TxtCategory); return; }
            if (!Formatting.TryParseRupiahCents(TxtCost.Text ?? "", out long cost) || cost <= 0)
            { await Invalid("Harga modal tidak valid.", TxtCost); return; }
            if (!Formatting.TryParseRupiahCents(TxtPrice.Text ?? "", out long price) || price <= 0)
            { await Invalid("Harga jual tidak valid.", TxtPrice); return; }
            if (!int.TryParse((TxtQty.Text ?? "").Trim(), out int qty) || qty <= 0)
            { await Invalid("Qty tidak valid.", TxtQty); return; }

            if (price < cost &&
                !await MsgBox.Confirm(NavigationService.Owner, "Harga jual di bawah harga modal. Tetap simpan?"))
            {
                ViewShortcuts.FocusInput(TxtPrice);
                return;
            }

            QuickProductResult result;
            try
            {
                result = _service.CreateQuickProduct(TxtName.Text!, category, cost, price, qty, _userId);
            }
            catch (Exception ex) when (ex is ArgumentException || ex is InvalidOperationException)
            {
                await MsgBox.Show(NavigationService.Owner, "Gagal simpan: " + ex.Message);
                return;
            }

            string name = TxtName.Text!.Trim().ToUpperInvariant();
            _added.Insert(0, new AddedRow(result.ProductCode, name, CategoryName(category), qty.ToString(),
                Formatting.FormatCurrencyShort(cost), Formatting.FormatCurrencyShort(price)));
            FooterStatus.Show(StatusLabel, $"Tersimpan: {result.ProductCode} — {name} ({qty} pcs, {result.JournalNo})");
            await MsgBox.Show(NavigationService.Owner,
                $"KODE BARANG: {result.ProductCode}\n\n{name}\nTulis kode ini di barangnya.", "Tersimpan");

            foreach (var f in _fields) f.Text = "";
            ViewShortcuts.FocusInput(TxtName);
        }
        finally
        {
            _saving = false;
        }
    }

    private async System.Threading.Tasks.Task Invalid(string message, TextBox field)
    {
        await MsgBox.Show(NavigationService.Owner, message);
        ViewShortcuts.FocusInput(field);
    }
}
