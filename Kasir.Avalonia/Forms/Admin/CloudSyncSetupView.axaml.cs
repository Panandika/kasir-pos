using System;
using System.ComponentModel;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;
using Kasir.Avalonia.Forms.Shared;
using Kasir.Avalonia.Infrastructure;
using Kasir.Avalonia.Navigation;
using Kasir.Avalonia.Utils;
using Npgsql;

namespace Kasir.Avalonia.Forms.Admin;

public partial class CloudSyncSetupView : UserControl
{
    private readonly CloudSyncStatusModel _status = CloudSyncStatusModel.Current;

    public CloudSyncSetupView()
    {
        InitializeComponent();

        LoadCredsIntoForm();
        RefreshStatusPanel();

        _status.PropertyChanged += OnStatusChanged;

        BtnTest.Click += async (_, _) => await OnTest();
        BtnSave.Click += async (_, _) => await OnSave();
        BtnBack.Click += (_, _) => NavigationService.GoBack();

        FooterStatus.RegisterDefault(StatusLabel, "Cloud Sync — F5=Uji Koneksi  F10=Simpan  Esc=Keluar");
    }

    protected override void OnDetachedFromVisualTree(global::Avalonia.VisualTreeAttachmentEventArgs e)
    {
        _status.PropertyChanged -= OnStatusChanged;
        base.OnDetachedFromVisualTree(e);
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Handled) return;
        if (KeyboardRouter.IsEscape(e)) { e.Handled = true; NavigationService.GoBack(); return; }
        if (e.Key == Key.F5) { e.Handled = true; _ = OnTest(); return; }
        if (e.Key == Key.F10) { e.Handled = true; _ = OnSave(); return; }
    }

    private void LoadCredsIntoForm()
    {
        var c = CloudSyncCredsService.Load();
        if (c is null) return;
        TxtHost.Text = c.Host;
        TxtPort.Text = c.Port.ToString();
        TxtDatabase.Text = c.Database;
        TxtUsername.Text = c.Username;
        TxtPassword.Text = c.Password;
    }

    private void OnStatusChanged(object? sender, PropertyChangedEventArgs e)
    {
        Dispatcher.UIThread.Post(RefreshStatusPanel);
    }

    private void RefreshStatusPanel()
    {
        LblState.Text = _status.DisplayText;
        LblQueue.Text = _status.QueueDepth > 0 ? $"{_status.QueueDepth} antri" : "0";
        LblHeartbeat.Text = _status.State switch
        {
            CloudSyncState.Connected    => "OK",
            CloudSyncState.Queued       => "OK (ada antrian)",
            CloudSyncState.Waking       => "membangunkan…",
            CloudSyncState.Disconnected => "offline",
            CloudSyncState.Error        => "gagal",
            _                           => "-"
        };
    }

    private bool TryBuildCredsFromForm(out CloudSyncCreds creds, out string? error)
    {
        creds = new CloudSyncCreds();
        var host = (TxtHost.Text ?? "").Trim();
        var portText = (TxtPort.Text ?? "").Trim();
        var db = (TxtDatabase.Text ?? "").Trim();
        var user = (TxtUsername.Text ?? "").Trim();
        var pwd = TxtPassword.Text ?? "";

        if (string.IsNullOrEmpty(host)) { error = "Host wajib diisi."; return false; }
        if (!int.TryParse(portText, out int port) || port < 1 || port > 65535)
        { error = "Port harus angka 1-65535."; return false; }
        if (string.IsNullOrEmpty(db)) { error = "Database wajib diisi."; return false; }
        if (string.IsNullOrEmpty(user)) { error = "Username wajib diisi."; return false; }
        if (string.IsNullOrEmpty(pwd)) { error = "Password wajib diisi."; return false; }

        creds = new CloudSyncCreds
        {
            Host = host,
            Port = port,
            Database = db,
            Username = user,
            Password = pwd,
        };
        error = null;
        return true;
    }

    private async Task OnTest()
    {
        if (!TryBuildCredsFromForm(out var creds, out var error))
        {
            LblTestResult.Text = error ?? "Form tidak valid.";
            return;
        }

        LblTestResult.Text = "Menguji koneksi…";
        BtnTest.IsEnabled = false;
        try
        {
            var (ok, msg) = await ProbeAsync(creds, TimeSpan.FromSeconds(30));
            LblTestResult.Text = ok ? "Koneksi berhasil" : $"Koneksi gagal: {msg}";
        }
        finally
        {
            BtnTest.IsEnabled = true;
        }
    }

    private static async Task<(bool ok, string? error)> ProbeAsync(CloudSyncCreds creds, TimeSpan timeout)
    {
        try
        {
            var b = new NpgsqlConnectionStringBuilder
            {
                Host = creds.Host,
                Port = creds.Port,
                Database = creds.Database,
                Username = creds.Username,
                Password = creds.Password,
                SslMode = SslMode.Require,
                Timeout = (int)Math.Max(1, timeout.TotalSeconds),
            };
            using var cts = new CancellationTokenSource(timeout);
            await using var conn = new NpgsqlConnection(b.ConnectionString);
            await conn.OpenAsync(cts.Token).ConfigureAwait(false);
            await using var cmd = new NpgsqlCommand("SELECT 1", conn);
            cmd.CommandTimeout = (int)Math.Max(1, timeout.TotalSeconds);
            await cmd.ExecuteScalarAsync(cts.Token).ConfigureAwait(false);
            return (true, null);
        }
        catch (Exception ex)
        {
            return (false, ex.Message);
        }
    }

    private async Task OnSave()
    {
        if (!TryBuildCredsFromForm(out var creds, out var error))
        {
            await MsgBox.Show(NavigationService.Owner, error ?? "Form tidak valid.");
            return;
        }

        if (!CloudSyncCredsService.Save(creds))
        {
            await MsgBox.Show(NavigationService.Owner, "Gagal menyimpan konfigurasi.");
            return;
        }

        _status.RefreshCreds();
        await MsgBox.Show(NavigationService.Owner, "Konfigurasi tersimpan.");
    }
}
