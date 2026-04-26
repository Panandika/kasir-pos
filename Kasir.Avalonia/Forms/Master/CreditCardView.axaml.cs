using System.Collections.Generic;
using System.Collections.ObjectModel;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia;
using Kasir.Data;
using Kasir.Data.Repositories;
using Kasir.Models;
using Kasir.Avalonia.Utils;
using Kasir.Avalonia.Forms.Shared;
using Kasir.Avalonia.Navigation;
using Kasir.Avalonia.Infrastructure;

namespace Kasir.Avalonia.Forms.Master;

public partial class CreditCardView : UserControl
{
    private record CreditCardRow(string Code, string Name, string Fee, string Account, CreditCard Tag);

    private readonly ObservableCollection<CreditCardRow> _rows = new();
    private readonly List<CreditCardRow> _allRows = new();
    private CreditCardRepository _cardRepo;
    private int _currentUserId;

    public CreditCardView(int currentUserId)
    {
        InitializeComponent();
        _currentUserId = currentUserId;
        _cardRepo = new CreditCardRepository(DbConnection.GetConnection());
        DgvCards.ItemsSource = _rows;
        TxtSearch.TextChanged += (_, _) => FilterGrid(TxtSearch.Text ?? "");
        ViewShortcuts.WireGridEnter(DgvCards, EditCard);
        FooterStatus.RegisterDefault(StatusLabel, "Ins=Tambah  Enter=Edit  Del=Hapus  Esc=Keluar");
        LoadData();
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        ViewShortcuts.AutoFocus(TxtSearch);
    }

    private void LoadData()
    {
        _allRows.Clear();
        foreach (var card in _cardRepo.GetAll())
        {
            _allRows.Add(new CreditCardRow(
                card.CardCode,
                card.Name,
                card.FeePct.ToString() + "%",
                card.AccountCode,
                card));
        }
        FilterGrid(TxtSearch.Text ?? "");
    }

    private void FilterGrid(string query)
    {
        string q = query.Trim().ToLower();
        _rows.Clear();
        foreach (var row in _allRows)
        {
            if (string.IsNullOrEmpty(q) ||
                row.Code.ToLower().Contains(q) ||
                row.Name.ToLower().Contains(q))
            {
                _rows.Add(row);
            }
        }
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (KeyboardRouter.IsInsert(e)) { e.Handled = true; AddCard(); }
        else if (KeyboardRouter.IsEnter(e)) { e.Handled = true; EditCard(); }
        else if (KeyboardRouter.IsDelete(e)) { e.Handled = true; DeleteCard(); }
        else if (KeyboardRouter.IsEscape(e)) { e.Handled = true; NavigationService.GoBack(); }
    }

    private async void AddCard()
    {
        var (ok, vals) = await InputDialogWindow.Show(NavigationService.Owner,
            "Tambah Kartu Kredit",
            new[] { "Kode", "Nama", "Fee (%)", "Kode Akun" },
            new[] { "", "", "0", "" });

        if (!ok) return;

        string code = vals[0].Trim().ToUpper();
        string name = vals[1].Trim();
        string feeStr = vals[2].Trim();
        string acc = vals[3].Trim();

        if (string.IsNullOrEmpty(code) || string.IsNullOrEmpty(name))
        {
            await MsgBox.Show(NavigationService.Owner, "Kode dan nama tidak boleh kosong.");
            return;
        }

        if (!int.TryParse(feeStr, out int fee) || fee < 0)
        {
            await MsgBox.Show(NavigationService.Owner, "Fee harus berupa angka >= 0.");
            return;
        }

        var existing = _cardRepo.GetByCode(code);
        if (existing != null)
        {
            await MsgBox.Show(NavigationService.Owner, $"Kode '{code}' sudah digunakan.");
            return;
        }

        var card = new CreditCard
        {
            CardCode = code,
            Name = name,
            FeePct = fee,
            AccountCode = acc,
            MinValue = 0,
            ChangedBy = _currentUserId
        };
        _cardRepo.Insert(card);
        LoadData();
        SetStatus($"Kartu '{name}' ditambahkan.");
    }

    private async void EditCard()
    {
        var row = DgvCards.SelectedItem as CreditCardRow;
        if (row == null)
        {
            await MsgBox.Show(NavigationService.Owner, "Pilih kartu yang akan diedit.");
            return;
        }

        var card = row.Tag;
        var (ok, vals) = await InputDialogWindow.Show(NavigationService.Owner,
            "Edit Kartu Kredit",
            new[] { "Nama", "Fee (%)", "Kode Akun" },
            new[] { card.Name, card.FeePct.ToString(), card.AccountCode });

        if (!ok) return;

        string name = vals[0].Trim();
        string feeStr = vals[1].Trim();
        string acc = vals[2].Trim();

        if (string.IsNullOrEmpty(name))
        {
            await MsgBox.Show(NavigationService.Owner, "Nama tidak boleh kosong.");
            return;
        }

        if (!int.TryParse(feeStr, out int fee) || fee < 0)
        {
            await MsgBox.Show(NavigationService.Owner, "Fee harus berupa angka >= 0.");
            return;
        }

        card.Name = name;
        card.FeePct = fee;
        card.AccountCode = acc;
        card.ChangedBy = _currentUserId;
        _cardRepo.Update(card);
        LoadData();
        SetStatus($"Kartu '{name}' diperbarui.");
    }

    private async void DeleteCard()
    {
        var row = DgvCards.SelectedItem as CreditCardRow;
        if (row == null)
        {
            await MsgBox.Show(NavigationService.Owner, "Pilih kartu yang akan dihapus.");
            return;
        }

        bool confirmed = await MsgBox.Confirm(NavigationService.Owner, $"Hapus kartu '{row.Name}'?");
        if (!confirmed) return;

        _cardRepo.Delete(row.Tag.Id);
        LoadData();
        SetStatus($"Kartu '{row.Name}' dihapus.");
    }

    private void SetStatus(string text) => FooterStatus.Show(StatusLabel, text);
}
