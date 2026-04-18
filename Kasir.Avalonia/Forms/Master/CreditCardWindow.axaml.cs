using System.Collections.ObjectModel;
using Avalonia.Controls;
using Avalonia.Input;
using Kasir.Data;
using Kasir.Data.Repositories;
using Kasir.Models;
using Kasir.Avalonia.Forms.Shared;

namespace Kasir.Avalonia.Forms.Master;

public partial class CreditCardWindow : Window
{
    private record CreditCardRow(string Code, string Name, string Fee, string Account, CreditCard Tag);

    private readonly ObservableCollection<CreditCardRow> _rows = new();
    private CreditCardRepository _cardRepo;
    private int _currentUserId;

    public CreditCardWindow(int currentUserId)
    {
        InitializeComponent();
        _currentUserId = currentUserId;
        _cardRepo = new CreditCardRepository(DbConnection.GetConnection());
        DgvCards.ItemsSource = _rows;
        SetStatus("Ins=Tambah  Enter=Edit  Del=Hapus  Esc=Keluar");
        LoadData();
    }

    private void LoadData()
    {
        _rows.Clear();
        foreach (var card in _cardRepo.GetAll())
        {
            _rows.Add(new CreditCardRow(
                card.CardCode,
                card.Name,
                card.FeePct.ToString() + "%",
                card.AccountCode,
                card));
        }
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (KeyboardRouter.IsInsert(e)) { e.Handled = true; AddCard(); }
        else if (KeyboardRouter.IsEnter(e)) { e.Handled = true; EditCard(); }
        else if (KeyboardRouter.IsDelete(e)) { e.Handled = true; DeleteCard(); }
        else if (KeyboardRouter.IsEscape(e)) { e.Handled = true; Close(); }
    }

    private async void AddCard()
    {
        var (ok, vals) = await InputDialogWindow.Show(this,
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
            await MsgBox.Show(this, "Kode dan nama tidak boleh kosong.");
            return;
        }

        if (!int.TryParse(feeStr, out int fee) || fee < 0)
        {
            await MsgBox.Show(this, "Fee harus berupa angka >= 0.");
            return;
        }

        var existing = _cardRepo.GetByCode(code);
        if (existing != null)
        {
            await MsgBox.Show(this, $"Kode '{code}' sudah digunakan.");
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
            await MsgBox.Show(this, "Pilih kartu yang akan diedit.");
            return;
        }

        var card = row.Tag;
        var (ok, vals) = await InputDialogWindow.Show(this,
            "Edit Kartu Kredit",
            new[] { "Nama", "Fee (%)", "Kode Akun" },
            new[] { card.Name, card.FeePct.ToString(), card.AccountCode });

        if (!ok) return;

        string name = vals[0].Trim();
        string feeStr = vals[1].Trim();
        string acc = vals[2].Trim();

        if (string.IsNullOrEmpty(name))
        {
            await MsgBox.Show(this, "Nama tidak boleh kosong.");
            return;
        }

        if (!int.TryParse(feeStr, out int fee) || fee < 0)
        {
            await MsgBox.Show(this, "Fee harus berupa angka >= 0.");
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
            await MsgBox.Show(this, "Pilih kartu yang akan dihapus.");
            return;
        }

        bool confirmed = await MsgBox.Confirm(this, $"Hapus kartu '{row.Name}'?");
        if (!confirmed) return;

        _cardRepo.Delete(row.Tag.Id);
        LoadData();
        SetStatus($"Kartu '{row.Name}' dihapus.");
    }

    private void SetStatus(string text)
    {
        StatusLabel.Text = text;
    }
}
