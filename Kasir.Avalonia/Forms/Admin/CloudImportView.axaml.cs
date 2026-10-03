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
    private bool _completed;

    private readonly CloudImportLog _log = new(ResolveLogPath());

    public CloudImportView()
    {
        InitializeComponent();

        // Show the store dashboard address when help.json provides one.
        string? dashboardUrl = Kasir.Help.Auth.HelpConfigLoader.TryReadOptional("DashboardUrl");
        if (!string.IsNullOrEmpty(dashboardUrl))
        {
            string baseUrl = dashboardUrl.TrimEnd('/');
            LblStep1.Text = $"1. Buka dashboard toko di browser (HP atau komputer lain): {baseUrl}";
            LblStep3.Text = $"3. Buka menu Pemilik → Register, atau langsung ke {baseUrl}/admin/registers";
        }
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
        if (_completed)
        {
            _tcs.TrySetResult(new FirstRunResult { Choice = "import", ImportPath = _stagingPath });
            return;
        }
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
        HideError();
        _cts = new CancellationTokenSource();
        var ct = _cts.Token;
        string fingerprint = "";

        try
        {
            string supabaseUrl = ResolveSupabaseUrl();
            if (string.IsNullOrEmpty(supabaseUrl))
            {
                _log.Write("config", null, null, null, null, null, "SupabaseUrl not found (env or help.json)");
                ShowError(new CloudImportExplanation
                {
                    Title = "Konfigurasi server tidak ditemukan.",
                    Cause = "File help.json (alamat server Supabase) tidak ada di folder aplikasi.",
                    Action = "Pasang ulang aplikasi dari file zip rilis resmi (help.json ikut di dalamnya).",
                    Technical = "SupabaseUrl kosong",
                });
                ResetUi();
                return;
            }

            SetStage("Memvalidasi kode…", 5);
            var pairClient = new BootstrapTokenClient(supabaseUrl);
            fingerprint = pairClient.DeviceFingerprint;
            pairClient.OnAttempt = a => _log.Write(
                "pair#" + a.Attempt, a.Endpoint, a.HttpStatus,
                a.HttpStatus is >= 200 and < 300 ? null : a.Body,
                a.Exception, a.DeviceFingerprint);
            var pair = await pairClient.PairAsync(code, ct);
            _log.Write("pair", pairClient.Endpoint, 200, null, null, fingerprint,
                $"ok register={pair.RegisterId} snapshot_available={pair.SnapshotAvailable} age_s={pair.SnapshotAgeSeconds}");

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
                else if (p.Stage == "decompressing")
                {
                    SetStage("Membuka kemasan data…", 90);
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

            var restorer = new CloudSnapshotRestorer(supabaseUrl)
            {
                OnEvent = (step, endpoint, status, body, ex) =>
                    _log.Write(step, endpoint, status, body, ex, fingerprint),
            };
            var summary = await restorer.RunAsync(pair.Jwt, _stagingPath, progress, ct, pair.RegisterId);
            _log.Write("done", null, null, null, null, fingerprint,
                $"snapshot restored to staging register_id={summary.RegisterId} seeded_default_login={summary.SeededDefaultLogin}");

            ShowSuccess(pair.RegisterId, summary);
        }
        catch (OperationCanceledException oce)
        {
            // Batal pressed vs HttpClient timeout (token not cancelled by us).
            if (_cts != null && _cts.IsCancellationRequested)
            {
                _log.Write("cancelled", null, null, null, null, fingerprint, "user pressed Batal");
                SetStatus("Dibatalkan.", isError: false);
            }
            else
            {
                _log.Write("timeout", null, null, null, oce, fingerprint);
                ShowError(CloudImportErrors.ForUnexpected(oce, isTimeout: true));
            }
            ResetUi();
        }
        catch (BootstrapTokenClient.PairException pex)
        {
            _log.Write("pair-failed", null, (int)pex.StatusCode, null, pex, fingerprint,
                "code=" + pex.ErrorCode);
            ShowError(CloudImportErrors.ForPair((int)pex.StatusCode, pex.ErrorCode, pex.ServerMessage));
            ResetUi();
        }
        catch (CloudSnapshotRestorer.RestoreException rex)
        {
            _log.Write("restore-failed:" + rex.Stage, null, rex.HttpStatus, null, rex, fingerprint,
                "code=" + rex.ServerCode);
            ShowError(CloudImportErrors.ForRestore(rex.Stage, rex.HttpStatus, rex.ServerCode,
                string.IsNullOrEmpty(rex.ServerMessage) ? rex.Message : rex.ServerMessage));
            ResetUi();
        }
        catch (Exception ex)
        {
            _log.Write("unexpected", null, null, null, ex, fingerprint);
            ShowError(CloudImportErrors.ForUnexpected(ex));
            ResetUi();
        }
    }

    // Restore succeeded: tell the operator which register this PC is and how to log in,
    // then wait for "Masuk ke aplikasi" before handing the database to first-run import.
    private void ShowSuccess(string pairedRegisterId, CloudSnapshotRestorer.RestoreSummary summary)
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("Data toko berhasil diunduh.");
        sb.AppendLine($"PC ini terdaftar sebagai register {pairedRegisterId} (nomor dokumen memakai {summary.RegisterId ?? "-"}).");
        if (summary.SeededDefaultLogin)
        {
            sb.AppendLine();
            sb.AppendLine("Login awal: user SM, password 74121.");
            sb.AppendLine("PENTING: segera ganti password ini di Utility → User Management setelah masuk.");
        }
        _completed = true;
        Dispatcher.UIThread.Post(() =>
        {
            PnlProgress.IsVisible = false;
            TxtSuccess.Text = sb.ToString().TrimEnd();
            PnlSuccess.IsVisible = true;
            BtnSubmit.Content = "Masuk ke aplikasi";
            BtnSubmit.IsEnabled = true;
            BtnCancel.IsVisible = false;
            LblStatus.Text = "Selesai.";
            LblStatus.Foreground = ThemeResources.Brush("FgSecondaryBrush");
            BtnSubmit.Focus();
        });
    }

    private void ShowError(CloudImportExplanation explanation)
    {
        string text = explanation + Environment.NewLine + "File log: " + _log.FilePath;
        Dispatcher.UIThread.Post(() =>
        {
            TxtError.Text = text;
            PnlError.IsVisible = true;
            LblStatus.Text = explanation.Title;
            LblStatus.Foreground = ThemeResources.Brush("DangerBrush");
        });
    }

    private void HideError()
    {
        PnlError.IsVisible = false;
        TxtError.Text = "";
        LblStatus.Text = "";
    }

    // data/logs/cloud-import.log next to the POS database folder (data/).
    private static string ResolveLogPath()
    {
        return Path.Combine(AppContext.BaseDirectory, "data", "logs", "cloud-import.log");
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
