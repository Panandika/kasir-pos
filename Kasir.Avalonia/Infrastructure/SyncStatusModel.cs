using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace Kasir.Avalonia.Infrastructure;

public enum SyncState
{
    OnlineRecent,
    OnlineOverdue,
    OfflineTransactable,
    OfflineBlocked,
    Syncing
}

public sealed class SyncStatusModel : INotifyPropertyChanged
{
    private static readonly Lazy<SyncStatusModel> _instance = new(() => new SyncStatusModel());
    public static SyncStatusModel Current => _instance.Value;

    private SyncState _state = SyncState.OnlineRecent;
    private int? _ageDays = 0;

    private SyncStatusModel() { }

    public SyncState State
    {
        get => _state;
        private set { _state = value; OnPropertyChanged(); OnPropertyChanged(nameof(DisplayText)); OnPropertyChanged(nameof(ShowDot)); }
    }

    public int? AgeDays
    {
        get => _ageDays;
        private set { _ageDays = value; OnPropertyChanged(); OnPropertyChanged(nameof(DisplayText)); }
    }

    public string DisplayText => State switch
    {
        SyncState.OnlineRecent       => $"Online · Sync {AgeDays ?? 0}d lalu",
        SyncState.OnlineOverdue      => "Sync tertunda",
        SyncState.OfflineTransactable => "Offline · transaksi tersimpan lokal",
        SyncState.OfflineBlocked     => "Tidak dapat memproses",
        SyncState.Syncing            => "Menyinkronkan…",
        _                            => string.Empty
    };

    /// <summary>
    /// True when the status dot should be shown (hidden for OfflineTransactable).
    /// </summary>
    public bool ShowDot => State != SyncState.OfflineTransactable;

    public void Update(SyncState state, int? ageDays = null)
    {
        _ageDays = ageDays;
        State = state;   // triggers all PropertyChanged notifications
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
