using System.Collections.ObjectModel;
using Avalonia.Controls;
using Avalonia.Input;
using Kasir.Avalonia.Forms.Shared;
using Kasir.Avalonia.Navigation;
using Kasir.Avalonia.Utils;
using Kasir.Data;
using Kasir.Data.Repositories;
using Kasir.Models;

namespace Kasir.Avalonia.Forms.Bank;

public partial class BankView : UserControl
{
    private record BankRow(string Code, string Name, string AccNo, string Branch, string Holder, Subsidiary Tag);

    private readonly ObservableCollection<BankRow> _rows = new();
    private readonly SubsidiaryRepository _bankRepo;

    public BankView()
    {
        InitializeComponent();
        _bankRepo = new SubsidiaryRepository(DbConnection.GetConnection());
        DgvBanks.ItemsSource = _rows;
        FooterStatus.RegisterDefault(StatusLabel, "Data Bank — Ins: Tambah, Enter: Ubah, Esc: Keluar");
        LoadData();
    }

    private void LoadData()
    {
        _rows.Clear();
        var banks = _bankRepo.GetAllByGroup("3", 500, 0);
        foreach (var b in banks)
        {
            _rows.Add(new BankRow(
                b.SubCode ?? "",
                b.Name ?? "",
                b.BankAccountNo ?? "",
                b.BankBranch ?? "",
                b.BankHolder ?? "",
                b));
        }
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (KeyboardRouter.IsInsert(e)) { ShowAddDialog(); e.Handled = true; }
        else if (KeyboardRouter.IsEnter(e)) { ShowEditDialog((DgvBanks.SelectedItem as BankRow)?.Tag); e.Handled = true; }
        else if (KeyboardRouter.IsEscape(e)) { NavigationService.GoBack(); e.Handled = true; }
    }

    private async void ShowAddDialog()
    {
        var (ok, vals) = await InputDialogWindow.Show(
            NavigationService.Owner,
            "Tambah Bank",
            new[] { "Kode", "Nama", "No. Rekening", "Cabang", "Pemegang" },
            new[] { "", "", "", "", "" });

        if (!ok) return;

        var sub = new Subsidiary
        {
            SubCode = vals[0],
            Name = vals[1],
            BankAccountNo = vals[2],
            BankBranch = vals[3],
            BankHolder = vals[4],
            GroupCode = "3",
            Status = "A"
        };

        _bankRepo.Insert(sub);
        LoadData();
    }

    private async void ShowEditDialog(Subsidiary? tag)
    {
        if (tag == null) return;

        var (ok, vals) = await InputDialogWindow.Show(
            NavigationService.Owner,
            "Ubah Bank",
            new[] { "Kode", "Nama", "No. Rekening", "Cabang", "Pemegang" },
            new[] { tag.SubCode ?? "", tag.Name ?? "", tag.BankAccountNo ?? "", tag.BankBranch ?? "", tag.BankHolder ?? "" });

        if (!ok) return;

        tag.Name = vals[1];
        tag.BankAccountNo = vals[2];
        tag.BankBranch = vals[3];
        tag.BankHolder = vals[4];

        _bankRepo.Update(tag);
        LoadData();
    }

    private void SetStatus(string text) => FooterStatus.Show(StatusLabel, text);
}
