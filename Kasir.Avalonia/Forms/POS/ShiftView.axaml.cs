using System;
using Avalonia.Controls;
using Avalonia.Input;
using Kasir.Data;
using Kasir.Data.Repositories;
using Kasir.Models;
using Kasir.Utils;
using Kasir.Avalonia.Forms.Shared;
using Kasir.Avalonia.Navigation;
using Kasir.Avalonia.Utils;

namespace Kasir.Avalonia.Forms.POS;

public partial class ShiftView : UserControl
{
    public Shift? CurrentShift { get; private set; }

    private readonly ShiftRepository _shiftRepo;
    private readonly ConfigRepository _configRepo;
    private readonly SaleRepository _saleRepo;
    private readonly int _cashierId;

    public ShiftView(int cashierId)
    {
        InitializeComponent();
        _cashierId = cashierId;
        var conn = DbConnection.GetConnection();
        _shiftRepo = new ShiftRepository(conn);
        _configRepo = new ConfigRepository(conn);
        _saleRepo = new SaleRepository(conn);
        BtnOpenShift.Click += async (_, _) => await OpenShift();
        BtnCloseShift.Click += async (_, _) => await CloseShift();
        FooterStatus.RegisterDefault(StatusLabel, "F1=Buka Shift  F2=Tutup Shift  Esc=Keluar");
        RefreshStatus();
    }

    private void RefreshStatus()
    {
        string regId = _configRepo.Get("register_id") ?? "01";
        CurrentShift = _shiftRepo.GetOpenShift(regId);
        if (CurrentShift != null)
        {
            LblStatus.Text = $"Shift {CurrentShift.ShiftNumber} — BUKA";
            LblInfo.Text = $"Dibuka: {CurrentShift.OpenedAt}\nKasir ID: {CurrentShift.CashierId}\nKas Awal: {Formatting.FormatCurrency(CurrentShift.OpeningCash)}";
        }
        else
        {
            LblStatus.Text = "Tidak ada shift terbuka";
            LblInfo.Text = "Tekan F1 untuk membuka shift baru.";
        }
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (KeyboardRouter.IsF1(e)) { e.Handled = true; _ = OpenShift(); }
        else if (KeyboardRouter.IsF2(e)) { e.Handled = true; _ = CloseShift(); }
        else if (KeyboardRouter.IsEscape(e)) { e.Handled = true; NavigationService.GoHome(); }
    }

    private async System.Threading.Tasks.Task OpenShift()
    {
        string regId = _configRepo.Get("register_id") ?? "01";
        if (_shiftRepo.GetOpenShift(regId) != null)
        {
            await MsgBox.Show(NavigationService.Owner, "Shift sudah terbuka.");
            return;
        }

        var (ok, vals) = await InputDialogWindow.Show(NavigationService.Owner,
            "Buka Shift",
            new[] { "Kas awal (Rp)" },
            new[] { "0" });

        if (!ok) return;

        if (!long.TryParse(vals[0], out long openingCash))
        {
            await MsgBox.Show(NavigationService.Owner, "Jumlah tidak valid.");
            return;
        }

        var shift = new Shift
        {
            RegisterId = regId,
            ShiftNumber = "1",
            CashierId = _cashierId,
            OpenedAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
            OpeningCash = openingCash * 100
        };
        _shiftRepo.OpenShift(shift);
        RefreshStatus();
        await MsgBox.Show(NavigationService.Owner, "Shift dibuka.");
    }

    private async System.Threading.Tasks.Task CloseShift()
    {
        if (CurrentShift == null)
        {
            await MsgBox.Show(NavigationService.Owner, "Tidak ada shift terbuka.");
            return;
        }

        string today = DateTime.Now.ToString("yyyy-MM-dd");
        long dailyCash = _saleRepo.GetDailyTotal(today);
        long expected = CurrentShift.OpeningCash + dailyCash;

        var (ok, vals) = await InputDialogWindow.Show(NavigationService.Owner,
            "Tutup Shift",
            new[] { $"Hitung uang di laci (ekspektasi: {Formatting.FormatCurrency(expected)})" },
            new[] { (expected / 100).ToString() });

        if (!ok) return;

        if (!long.TryParse(vals[0], out long closing))
        {
            await MsgBox.Show(NavigationService.Owner, "Jumlah tidak valid.");
            return;
        }

        closing *= 100;
        _shiftRepo.CloseShift(CurrentShift.Id, closing, expected);
        long variance = closing - expected;
        string vtext = variance == 0
            ? "Tidak ada selisih."
            : $"Selisih: {(variance > 0 ? "+" : "")}{Formatting.FormatCurrency(variance)}";

        await MsgBox.Show(NavigationService.Owner,
            $"Shift ditutup.\n\nEkspektasi: {Formatting.FormatCurrency(expected)}\nDihitung: {Formatting.FormatCurrency(closing)}\n{vtext}");
        RefreshStatus();
    }

    private void SetStatus(string t) => FooterStatus.Show(StatusLabel, t);
}
