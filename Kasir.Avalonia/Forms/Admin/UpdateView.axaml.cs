using System;
using System.Threading;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input;
using Avalonia.Threading;
using Kasir.Data;
using Kasir.Services;
using Kasir.Utils;
using Kasir.Avalonia.Forms.Shared;
using Kasir.Avalonia.Infrastructure;
using Kasir.Avalonia.Navigation;
using Kasir.Avalonia.Utils;

namespace Kasir.Avalonia.Forms.Admin;

// Utility → Periksa Update. Checks GitHub Releases, shows what's new, downloads the
// signed package for this register, verifies it, then hands over to the staged build
// (--apply-update) which replaces the files and reopens the POS.
public partial class UpdateView : UserControl
{
    private readonly UpdateService _updateService;
    private UpdateCheckResult? _check;
    private bool _busy;
    private CancellationTokenSource? _cts;

    public UpdateView()
    {
        _updateService = new UpdateService(DbConnection.IsInitialized ? DbConnection.GetConnection() : null);
        InitializeComponent();

        LblCurrentVersion.Text = $"Kasir v{AppVersion.Current}";

        BtnCheck.Click += (_, _) => CheckForUpdate();
        BtnApply.Click += (_, _) => ApplyUpdate();

        FooterStatus.RegisterDefault(StatusLabel, "Periksa Update — F5=Periksa  F8=Update  Esc=Keluar");
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);

        if (KeyboardRouter.IsF5(e))
        {
            e.Handled = true;
            CheckForUpdate();
        }
        else if (KeyboardRouter.IsF8(e))
        {
            e.Handled = true;
            if (_check?.Available == true) ApplyUpdate();
        }
        else if (KeyboardRouter.IsEscape(e))
        {
            e.Handled = true;
            if (_busy) _cts?.Cancel();
            else NavigationService.GoBack();
        }
    }

    private async void CheckForUpdate()
    {
        if (_busy) return;
        _busy = true;
        LblStatus.Text = UpdateMessages.Checking;
        _check = null;
        TxtPatchNotes.IsVisible = false;
        BtnApply.IsVisible = false;

        try
        {
            var result = await _updateService.CheckForUpdateAsync();
            if (!string.IsNullOrEmpty(result.Error))
            {
                LblStatus.Text = result.Error;
                return;
            }

            if (result.Available)
            {
                _check = result;
                LblStatus.Text = string.Format(UpdateMessages.Available, result.NewVersion);
                if (!string.IsNullOrWhiteSpace(result.ReleaseNotes))
                {
                    TxtPatchNotes.Text = $"Yang baru di v{result.NewVersion}:\n\n{result.ReleaseNotes}";
                    TxtPatchNotes.IsVisible = true;
                }
                BtnApply.IsVisible = true;
            }
            else
            {
                LblStatus.Text = UpdateMessages.UpToDate + $" (v{result.CurrentVersion})";
            }
        }
        catch (Exception ex)
        {
            LblStatus.Text = UpdateMessages.Offline + " (" + ex.Message + ")";
        }
        finally
        {
            _busy = false;
        }
    }

    private async void ApplyUpdate()
    {
        if (_busy || _check == null || !_check.Available) return;

        bool ok = await MsgBox.Confirm(NavigationService.Owner, string.Format(UpdateMessages.Confirm, _check.NewVersion));
        if (!ok) return;

        _busy = true;
        _cts = new CancellationTokenSource();
        BtnApply.IsEnabled = false;
        BtnCheck.IsEnabled = false;
        try
        {
            var progress = new Progress<int>(p =>
                LblStatus.Text = p >= 100 ? UpdateMessages.Verifying : string.Format(UpdateMessages.Downloading, p));
            LblStatus.Text = string.Format(UpdateMessages.Downloading, 0);

            var prep = await _updateService.DownloadAndPrepareAsync(_check, progress, _cts.Token);
            if (!prep.Success)
            {
                LblStatus.Text = prep.Error;
                return;
            }

            if (!_updateService.WalCheckpoint())
            {
                LblStatus.Text = UpdateMessages.WalCheckpointFailed;
                return;
            }

            UpdateStatusModel.Current.MarkUpdating();
            LblStatus.Text = UpdateMessages.InProgress;
            DbConnection.CloseConnection();

            _updateService.ExitAction = () => Dispatcher.UIThread.Post(() =>
            {
                if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
                    desktop.Shutdown();
                else
                    Environment.Exit(0);
            });
            _updateService.ApplyUpdate();
        }
        catch (Exception ex)
        {
            LblStatus.Text = UpdateMessages.PrepareFailed + " (" + ex.Message + ")";
        }
        finally
        {
            _busy = false;
            BtnApply.IsEnabled = true;
            BtnCheck.IsEnabled = true;
            _cts?.Dispose();
            _cts = null;
        }
    }
}
