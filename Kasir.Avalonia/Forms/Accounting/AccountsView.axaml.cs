using System.Collections.ObjectModel;
using Avalonia.Controls;
using Avalonia.Input;
using Kasir.Data;
using Kasir.Data.Repositories;
using Kasir.Models;
using Kasir.Avalonia.Forms.Shared;
using Kasir.Avalonia.Navigation;

namespace Kasir.Avalonia.Forms.Accounting;

public partial class AccountsView : UserControl
{
    private record AccRow(string Code, string Name, string Group, string NormalBal, string Detail, Account Tag);

    private readonly ObservableCollection<AccRow> _rows = new();
    private readonly AccountRepository _accountRepo;
    private readonly bool _readOnly;

    public AccountsView(bool readOnly = false)
    {
        InitializeComponent();
        _readOnly = readOnly;

        var db = DbConnection.GetConnection();
        _accountRepo = new AccountRepository(db);

        DgvAccounts.ItemsSource = _rows;

        TxtSearch.KeyDown += (_, e) =>
        {
            if (KeyboardRouter.IsEnter(e)) SearchAccounts();
        };

        BtnSearch.Click += (_, _) =>
        {
            TxtSearch.Focus();
            SearchAccounts();
        };

        SetStatus(_readOnly ? "Daftar Akun — Esc: Keluar" : "Daftar Akun — Ins: Tambah, Enter: Edit, Esc: Keluar");
        LoadData();
    }

    private void LoadData()
    {
        _rows.Clear();
        foreach (var a in _accountRepo.GetAll())
        {
            string indent = new string(' ', a.Level * 2);
            _rows.Add(new AccRow(
                a.AccountCode,
                indent + a.AccountName,
                GetGroupName(a.AccountGroup),
                a.NormalBalance ?? "",
                a.IsDetail == 1 ? "Ya" : "",
                a));
        }
    }

    private void SearchAccounts()
    {
        string q = TxtSearch.Text?.Trim() ?? "";
        if (string.IsNullOrEmpty(q))
        {
            LoadData();
            return;
        }

        _rows.Clear();
        foreach (var a in _accountRepo.Search(q))
        {
            string indent = new string(' ', a.Level * 2);
            _rows.Add(new AccRow(
                a.AccountCode,
                indent + a.AccountName,
                GetGroupName(a.AccountGroup),
                a.NormalBalance ?? "",
                a.IsDetail == 1 ? "Ya" : "",
                a));
        }
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);

        if (KeyboardRouter.IsF2(e))
        {
            e.Handled = true;
            TxtSearch.Focus();
            return;
        }

        if (KeyboardRouter.IsInsert(e))
        {
            e.Handled = true;
            if (!_readOnly) ShowAddDialog();
            return;
        }

        if (KeyboardRouter.IsEnter(e))
        {
            e.Handled = true;
            if (!_readOnly && DgvAccounts.SelectedItem is AccRow row)
                ShowEditDialog(row.Tag);
            return;
        }

        if (KeyboardRouter.IsEscape(e))
        {
            e.Handled = true;
            NavigationService.GoBack();
        }
    }

    private async void ShowAddDialog()
    {
        var (ok, vals) = await InputDialogWindow.Show(
            NavigationService.Owner,
            "Tambah Akun",
            new[] { "Kode Akun", "Nama Akun", "Kode Induk", "Grup (1-5)", "Saldo Normal (D/K)", "Detail (0/1)" },
            new[] { "", "", "", "1", "D", "1" });

        if (!ok) return;

        var account = new Account
        {
            AccountCode   = vals[0].Trim(),
            AccountName   = vals[1].Trim(),
            ParentCode    = vals[2].Trim(),
            AccountGroup  = int.TryParse(vals[3], out int g) ? g : 1,
            NormalBalance = string.IsNullOrEmpty(vals[4]) ? "D" : vals[4].ToUpper(),
            IsDetail      = int.TryParse(vals[5], out int d) ? d : 1,
            Level         = string.IsNullOrEmpty(vals[2].Trim()) ? 0 : 1,
            ChangedBy     = 1
        };

        _accountRepo.Insert(account);
        LoadData();
    }

    private async void ShowEditDialog(Account existing)
    {
        var (ok, vals) = await InputDialogWindow.Show(
            NavigationService.Owner,
            "Edit Akun",
            new[] { "Kode Akun", "Nama Akun", "Kode Induk", "Grup (1-5)", "Saldo Normal (D/K)", "Detail (0/1)" },
            new[]
            {
                existing.AccountCode,
                existing.AccountName,
                existing.ParentCode ?? "",
                existing.AccountGroup.ToString(),
                existing.NormalBalance ?? "D",
                existing.IsDetail.ToString()
            });

        if (!ok) return;

        existing.AccountCode   = vals[0].Trim();
        existing.AccountName   = vals[1].Trim();
        existing.ParentCode    = vals[2].Trim();
        existing.AccountGroup  = int.TryParse(vals[3], out int g) ? g : existing.AccountGroup;
        existing.NormalBalance = string.IsNullOrEmpty(vals[4]) ? existing.NormalBalance : vals[4].ToUpper();
        existing.IsDetail      = int.TryParse(vals[5], out int d) ? d : existing.IsDetail;
        existing.ChangedBy     = 1;

        _accountRepo.Update(existing);
        LoadData();
    }

    private static string GetGroupName(int group)
    {
        switch (group)
        {
            case 1: return "Aktiva";
            case 2: return "Kewajiban";
            case 3: return "Modal";
            case 4: return "Pendapatan";
            case 5: return "Biaya";
            default: return group.ToString();
        }
    }

    private void SetStatus(string text)
    {
        StatusLabel.Text = text;
    }
}
