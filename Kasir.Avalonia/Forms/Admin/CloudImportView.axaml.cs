using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;
using Kasir.Avalonia.Infrastructure;
using Kasir.Avalonia.Navigation;
using Kasir.CloudSync.Restore;
using Kasir.Data;

namespace Kasir.Avalonia.Forms.Admin;

public partial class CloudImportView : UserControl
{
    private readonly TaskCompletionSource<FirstRunResult?> _tcs = new();
    private CancellationTokenSource? _cts;
    private string? _stagingPath;

    public CloudImportView()
    {
        InitializeComponent();
        BtnSubmit.Click += async (_, _) => await OnSubmit();
        BtnCancel.Click += (_, _) =>
        {
            _cts?.Cancel();
            _tcs.TrySetResult(null);
        };
        TxtCode.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter)
            {
                e.Handled = true;
                _ = OnSubmit();
            }
        };
    }

    public Task<FirstRunResult?> WaitForChoice() => _tcs.Task;

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (KeyboardRouter.IsEscape(e))
        {
            e.Handled = true;
            _cts?.Cancel();
            _tcs.TrySetResult(null);
        }
    }

    private async Task OnSubmit()
    {
        if (_cts != null) return; // already running
        string code = TxtCode.Text?.Trim() ?? string.Empty;
        if (code.Length != 6)
        {
            SetStatus("Kode harus 6 digit.", isError: true);
            return;
        }

        BtnSubmit.IsEnabled = false;
        TxtCode.IsEnabled = false;
        PnlProgress.IsVisible = true;
        _cts = new CancellationTokenSource();
        var ct = _cts.Token;

        try
        {
            string supabaseUrl = ResolveSupabaseUrl();
            if (string.IsNullOrEmpty(supabaseUrl))
            {
                SetStatus("Konfigurasi Supabase URL tidak ditemukan.", isError: true);
                ResetUi();
                return;
            }

            SetStage("Memvalidasi kode…", 5);
            var pairClient = new BootstrapTokenClient(supabaseUrl);
            var pair = await pairClient.PairAsync(code, ct);

            SetStage("Mengunduh snapshot…", 15);
            _stagingPath = ResolveStagingPath();
            var progress = new Progress<CloudSnapshotRestorer.RestoreProgress>(p =>
            {
                if (p.Stage == "downloading" && p.TotalBytes > 0)
                {
                    var pct = 15 + (int)((double)p.BytesDownloaded / p.TotalBytes * 70);
                    SetStage($"Mengunduh… {(int)(100.0 * p.BytesDownloaded / p.TotalBytes)}%", pct);
                }
                else if (p.Stage == "verifying")
                {
                    SetStage("Memverifikasi integritas…", 87);
                }
                else if (p.Stage == "swapping")
                {
                    SetStage("Memasang database…", 95);
                }
                else if (p.Stage == "done")
                {
                    SetStage("Selesai.", 100);
                }
            });

            var restorer = new CloudSnapshotRestorer(supabaseUrl);
            await restorer.RunAsync(pair.Jwt, _stagingPath, progress, ct);

            _tcs.TrySetResult(new FirstRunResult
            {
                Choice = "import",
                ImportPath = _stagingPath,
            });
        }
        catch (OperationCanceledException)
        {
            SetStatus("Dibatalkan.", isError: false);
            ResetUi();
        }
        catch (BootstrapTokenClient.PairException pex)
        {
            SetStatus("Pairing gagal: " + (pex.ErrorCode ?? "unknown"), isError: true);
            ResetUi();
        }
        catch (CloudSnapshotRestorer.RestoreException rex)
        {
            SetStatus($"Gagal di tahap {rex.Stage}: {rex.Message}", isError: true);
            ResetUi();
        }
        catch (Exception ex)
        {
            SetStatus("Kesalahan: " + ex.Message, isError: true);
            ResetUi();
        }
    }

    private void ResetUi()
    {
        BtnSubmit.IsEnabled = true;
        TxtCode.IsEnabled = true;
        PnlProgress.IsVisible = false;
        _cts?.Dispose();
        _cts = null;
    }

    private void SetStage(string message, int percent)
    {
        Dispatcher.UIThread.Post(() =>
        {
            LblStage.Text = message;
            PbDownload.Value = percent;
        });
    }

    private void SetStatus(string message, bool isError)
    {
        Dispatcher.UIThread.Post(() =>
        {
            LblStatus.Text = message;
            LblStatus.Foreground = ThemeResources.Brush(isError ? "DangerBrush" : "FgSecondaryBrush");
        });
    }

    private static string ResolveSupabaseUrl()
    {
        // Priority:
        //   1. env var (dev / explicit override)
        //   2. help.json — same file Bantuan / SupabaseMachineAuth already reads.
        //      Lives at %APPDATA%\Kasir\help.json or {exe}/help.json (ships in release).
        var envUrl = Environment.GetEnvironmentVariable("KASIR_SUPABASE_URL")
                     ?? Environment.GetEnvironmentVariable("SUPABASE_URL");
        if (!string.IsNullOrWhiteSpace(envUrl)) return envUrl;

        var cfg = Kasir.Help.Auth.HelpConfigLoader.TryLoad();
        if (cfg != null && !string.IsNullOrWhiteSpace(cfg.SupabaseUrl)) return cfg.SupabaseUrl;

        return string.Empty;
    }

    private static string ResolveStagingPath()
    {
        var dir = Path.Combine(AppContext.BaseDirectory, "data");
        Directory.CreateDirectory(dir);
        return Path.Combine(dir, "kasir.db.cloud-staging");
    }
}
