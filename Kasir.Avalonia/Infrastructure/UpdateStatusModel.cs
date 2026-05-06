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
using Kasir.Services;
using Kasir.Utils;

namespace Kasir.Avalonia.Infrastructure;

public enum UpdateAvailability
{
    Current,
    UpdateAvailable,
    Updating
}

public sealed class UpdateStatusModel : INotifyPropertyChanged
{
    private static readonly Lazy<UpdateStatusModel> _instance = new(() => new UpdateStatusModel());
    public static UpdateStatusModel Current => _instance.Value;

    private static readonly TimeSpan CheckEvery = TimeSpan.FromHours(24);
    private static readonly TimeSpan TickInterval = TimeSpan.FromHours(1);

    private static readonly string ConfigDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Kasir");
    private static readonly string StatePath = Path.Combine(ConfigDir, "update-state.json");

    private UpdateAvailability _state = UpdateAvailability.Current;
    private string _currentVersion = AppVersion.Current;
    private string? _newVersion;
    private DateTime? _lastCheckAt;
    private Timer? _timer;
    private int _checking; // 0=idle, 1=in-flight

    private UpdateStatusModel() { }

    public UpdateAvailability State
    {
        get => _state;
        private set
        {
            if (_state == value) return;
            _state = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(DisplayText));
            OnPropertyChanged(nameof(ShowDot));
            OnPropertyChanged(nameof(ShowSpinner));
            OnPropertyChanged(nameof(DotBrush));
            OnPropertyChanged(nameof(TextBrush));
        }
    }

    public string DisplayText => State switch
    {
        UpdateAvailability.Current         => $"v{_currentVersion}",
        UpdateAvailability.UpdateAvailable => $"v{_currentVersion} → v{_newVersion}",
        UpdateAvailability.Updating        => "Memperbarui…",
        _                                  => string.Empty
    };

    public bool ShowDot => State == UpdateAvailability.UpdateAvailable;
    public bool ShowSpinner => State == UpdateAvailability.Updating;

    public IBrush DotBrush => State switch
    {
        UpdateAvailability.UpdateAvailable => ResolveBrush("BrandBrush", "AccentBrush", "SuccessBrush"),
        UpdateAvailability.Updating        => ResolveBrush("WarningBrush"),
        _                                  => ResolveBrush("FgDimBrush", "FgSecondaryBrush")
    };

    public IBrush TextBrush => State switch
    {
        UpdateAvailability.Current => ResolveBrush("FgDimBrush", "FgSecondaryBrush"),
        _                          => ResolveBrush("FgPrimaryBrush", "FgSecondaryBrush")
    };

    public void Start()
    {
        ReadState();
        _timer?.Dispose();
        // Tick immediately, then hourly. The handler decides whether to actually hit the network.
        _timer = new Timer(_ => Tick(), null, TimeSpan.Zero, TickInterval);
    }

    public void Stop()
    {
        _timer?.Dispose();
        _timer = null;
    }

    public void MarkUpdating() => State = UpdateAvailability.Updating;

    private void Tick()
    {
        if (_lastCheckAt is { } last && DateTime.UtcNow - last < CheckEvery)
        {
            return;
        }
        _ = RunCheckAsync();
    }

    private async Task RunCheckAsync()
    {
        if (Interlocked.Exchange(ref _checking, 1) == 1) return;
        try
        {
            UpdateCheckResult? result = null;
            try
            {
                var svc = new UpdateService(DbConnection.GetConnection());
                result = await svc.CheckForUpdateAsync().ConfigureAwait(false);
            }
            catch
            {
                // Best-effort; keep last known state.
            }

            if (result is not null && string.IsNullOrEmpty(result.Error))
            {
                if (result.Available && !string.IsNullOrEmpty(result.NewVersion))
                {
                    _newVersion = result.NewVersion;
                    State = UpdateAvailability.UpdateAvailable;
                }
                else if (State != UpdateAvailability.Updating)
                {
                    _newVersion = null;
                    State = UpdateAvailability.Current;
                }
            }

            _lastCheckAt = DateTime.UtcNow;
            WriteState();
        }
        finally
        {
            Interlocked.Exchange(ref _checking, 0);
        }
    }

    private void ReadState()
    {
        try
        {
            if (!File.Exists(StatePath)) return;
            var json = File.ReadAllText(StatePath);
            var s = JsonSerializer.Deserialize<PersistedState>(json);
            if (s is null) return;
            if (DateTime.TryParse(s.LastCheckAt, out var dt))
            {
                _lastCheckAt = dt.ToUniversalTime();
            }
        }
        catch
        {
            // Best-effort
        }
    }

    private void WriteState()
    {
        try
        {
            Directory.CreateDirectory(ConfigDir);
            var s = new PersistedState
            {
                LastCheckAt = (_lastCheckAt ?? DateTime.UtcNow).ToString("o")
            };
            File.WriteAllText(StatePath, JsonSerializer.Serialize(s));
        }
        catch
        {
            // Best-effort
        }
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

    private sealed class PersistedState
    {
        public string? LastCheckAt { get; set; }
    }
}
