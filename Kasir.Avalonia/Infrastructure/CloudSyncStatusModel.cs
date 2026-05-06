using System;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Media;
using Kasir.Data;
using Microsoft.Data.Sqlite;
using Npgsql;

namespace Kasir.Avalonia.Infrastructure;

public enum CloudSyncState
{
    Disabled,
    Waking,
    Connected,
    Queued,
    Disconnected,
    Error
}

public sealed class CloudSyncStatusModel : INotifyPropertyChanged
{
    private static readonly Lazy<CloudSyncStatusModel> _instance = new(() => new CloudSyncStatusModel());
    public static CloudSyncStatusModel Current => _instance.Value;

    private static readonly TimeSpan QueuePollInterval = TimeSpan.FromSeconds(60);
    private static readonly TimeSpan ConnectivityInterval = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan WakeTimeout = TimeSpan.FromSeconds(90);

    private static readonly string ConfigDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Kasir");
    private static readonly string CredsPath = Path.Combine(ConfigDir, "cloudsync.json");

    private CloudSyncState _state = CloudSyncState.Disabled;
    private int _queueDepth;
    private int _consecutiveFailures;
    private DateTime _lastConnectivityCheck = DateTime.MinValue;
    private DateTime _lastKeepAliveDate = DateTime.MinValue.Date;
    private CloudCreds? _creds;

    private Timer? _queueTimer;
    private Timer? _connectivityTimer;
    private Timer? _keepAliveTimer;

    private int _connectivityInFlight;

    private CloudSyncStatusModel() { }

    public CloudSyncState State
    {
        get => _state;
        private set
        {
            if (_state == value) return;
            _state = value;
            OnPropertyChanged();
            RaiseDerived();
        }
    }

    public int QueueDepth
    {
        get => _queueDepth;
        private set
        {
            if (_queueDepth == value) return;
            _queueDepth = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(DisplayText));
        }
    }

    public string DisplayText => State switch
    {
        CloudSyncState.Disabled     => "Cloud · off",
        CloudSyncState.Waking       => "Cloud · membangunkan…",
        CloudSyncState.Connected    => "Cloud · OK",
        CloudSyncState.Queued       => $"Cloud · {_queueDepth} antri",
        CloudSyncState.Disconnected => "Cloud · offline",
        CloudSyncState.Error        => "Cloud · gagal",
        _                           => string.Empty
    };

    public bool ShowDot => State is CloudSyncState.Connected or CloudSyncState.Queued or CloudSyncState.Error;
    public bool ShowSpinner => State == CloudSyncState.Waking;

    public IBrush DotBrush => State switch
    {
        CloudSyncState.Connected    => ResolveBrush("SuccessBrush"),
        CloudSyncState.Queued       => ResolveBrush("WarningBrush"),
        CloudSyncState.Waking       => ResolveBrush("WarningBrush"),
        CloudSyncState.Error        => ResolveBrush("DangerBrush"),
        CloudSyncState.Disconnected => ResolveBrush("WarningBrush"),
        _                           => ResolveBrush("FgDimBrush", "FgSecondaryBrush")
    };

    public IBrush TextBrush => State == CloudSyncState.Disabled
        ? ResolveBrush("FgDimBrush", "FgSecondaryBrush")
        : ResolveBrush("FgSecondaryBrush", "FgPrimaryBrush");

    public void Start()
    {
        _creds = TryReadCreds();
        if (_creds is null)
        {
            State = CloudSyncState.Disabled;
            return;
        }

        State = CloudSyncState.Waking;
        _ = WakeAsync();

        _queueTimer = new Timer(_ => PollQueueDepth(), null, QueuePollInterval, QueuePollInterval);
        _connectivityTimer = new Timer(_ => _ = ConnectivityCheckAsync(), null, ConnectivityInterval, ConnectivityInterval);
        // Keep-alive: tick every 30 minutes, fire at 03:00 once per day.
        _keepAliveTimer = new Timer(_ => _ = MaybeKeepAliveAsync(), null,
            TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(30));
    }

    public void Stop()
    {
        _queueTimer?.Dispose();
        _connectivityTimer?.Dispose();
        _keepAliveTimer?.Dispose();
        _queueTimer = _connectivityTimer = _keepAliveTimer = null;
    }

    private async Task WakeAsync()
    {
        var ok = await TrySelectOneAsync(WakeTimeout).ConfigureAwait(false);
        PollQueueDepth();
        if (ok)
        {
            _consecutiveFailures = 0;
            _lastConnectivityCheck = DateTime.UtcNow;
            State = _queueDepth > 0 ? CloudSyncState.Queued : CloudSyncState.Connected;
        }
        else
        {
            State = CloudSyncState.Disconnected;
        }
    }

    private async Task ConnectivityCheckAsync()
    {
        if (_creds is null) return;
        if (Interlocked.Exchange(ref _connectivityInFlight, 1) == 1) return;
        try
        {
            var ok = await TrySelectOneAsync(TimeSpan.FromSeconds(30)).ConfigureAwait(false);
            _lastConnectivityCheck = DateTime.UtcNow;
            if (ok)
            {
                _consecutiveFailures = 0;
                if (State is CloudSyncState.Disconnected or CloudSyncState.Error or CloudSyncState.Waking)
                {
                    State = _queueDepth > 0 ? CloudSyncState.Queued : CloudSyncState.Connected;
                }
            }
            else
            {
                _consecutiveFailures++;
                State = _consecutiveFailures >= 3 ? CloudSyncState.Error : CloudSyncState.Disconnected;
            }
        }
        finally
        {
            Interlocked.Exchange(ref _connectivityInFlight, 0);
        }
    }

    private async Task MaybeKeepAliveAsync()
    {
        if (_creds is null) return;
        var now = DateTime.Now;
        if (now.Hour != 3) return;
        if (_lastKeepAliveDate == now.Date) return;
        _lastKeepAliveDate = now.Date;
        await TrySelectOneAsync(TimeSpan.FromSeconds(30)).ConfigureAwait(false);
    }

    private async Task<bool> TrySelectOneAsync(TimeSpan timeout)
    {
        if (_creds is null) return false;
        try
        {
            var connStr = BuildConnString(_creds);
            using var cts = new CancellationTokenSource(timeout);
            await using var conn = new NpgsqlConnection(connStr);
            await conn.OpenAsync(cts.Token).ConfigureAwait(false);
            await using var cmd = new NpgsqlCommand("SELECT 1", conn);
            cmd.CommandTimeout = (int)Math.Max(1, timeout.TotalSeconds);
            await cmd.ExecuteScalarAsync(cts.Token).ConfigureAwait(false);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private void PollQueueDepth()
    {
        try
        {
            var conn = DbConnection.GetConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT COUNT(*) FROM sync_queue WHERE cloud_synced=0";
            var result = cmd.ExecuteScalar();
            var depth = result is null ? 0 : Convert.ToInt32(result);
            QueueDepth = depth;
            // Only flip Connected ↔ Queued; leave Waking/Disconnected/Error alone.
            if (State is CloudSyncState.Connected or CloudSyncState.Queued)
            {
                State = depth > 0 ? CloudSyncState.Queued : CloudSyncState.Connected;
            }
        }
        catch (SqliteException)
        {
            // sync_queue may not exist on first run — ignore.
        }
        catch
        {
            // Best-effort
        }
    }

    private static string BuildConnString(CloudCreds c)
    {
        var b = new NpgsqlConnectionStringBuilder
        {
            Host = c.Host,
            Port = c.Port,
            Database = c.Database,
            Username = c.Username,
            Password = c.Password,
            SslMode = SslMode.Require,
            Timeout = 30
        };
        return b.ConnectionString;
    }

    private static CloudCreds? TryReadCreds()
    {
        try
        {
            if (!File.Exists(CredsPath)) return null;
            var json = File.ReadAllText(CredsPath);
            var c = JsonSerializer.Deserialize<CloudCreds>(json);
            if (c is null) return null;
            if (string.IsNullOrWhiteSpace(c.Host) || string.IsNullOrWhiteSpace(c.Database)
                || string.IsNullOrWhiteSpace(c.Username) || string.IsNullOrWhiteSpace(c.Password))
            {
                return null;
            }
            return c;
        }
        catch
        {
            return null;
        }
    }

    private void RaiseDerived()
    {
        OnPropertyChanged(nameof(DisplayText));
        OnPropertyChanged(nameof(ShowDot));
        OnPropertyChanged(nameof(ShowSpinner));
        OnPropertyChanged(nameof(DotBrush));
        OnPropertyChanged(nameof(TextBrush));
    }

    private static IBrush ResolveBrush(params string[] keys)
    {
        var app = Application.Current;
        if (app is null) return Brushes.Gray;
        foreach (var key in keys)
        {
            if (app.Resources.TryGetResource(key, app.ActualThemeVariant, out var res) && res is IBrush brush)
            {
                return brush;
            }
        }
        return Brushes.Gray;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    private sealed class CloudCreds
    {
        public string Host { get; set; } = "";
        public int Port { get; set; } = 6543;
        public string Database { get; set; } = "postgres";
        public string Username { get; set; } = "";
        public string Password { get; set; } = "";
    }
}
