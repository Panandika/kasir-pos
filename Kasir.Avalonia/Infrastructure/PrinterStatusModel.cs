using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Threading;
using Avalonia;
using Avalonia.Media;
using Kasir.Hardware;

namespace Kasir.Avalonia.Infrastructure;

public enum PrinterState
{
    Connected,
    Offline,
    Error
}

public sealed class PrinterStatusModel : INotifyPropertyChanged
{
    private static readonly Lazy<PrinterStatusModel> _instance = new(() => new PrinterStatusModel());
    public static PrinterStatusModel Current => _instance.Value;

    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(30);

    private PrinterState _state = PrinterState.Offline;
    private Timer? _timer;
    private Func<IReceiptPrinter?>? _printerFactory;
    private int _polling; // 0=idle, 1=in-flight

    private PrinterStatusModel() { }

    public PrinterState State
    {
        get => _state;
        private set
        {
            if (_state == value) return;
            _state = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(DisplayText));
            OnPropertyChanged(nameof(ShowDot));
            OnPropertyChanged(nameof(DotBrush));
        }
    }

    public string DisplayText => State switch
    {
        PrinterState.Connected => "Printer ✓",
        PrinterState.Offline   => "Printer offline",
        PrinterState.Error     => "Printer error",
        _                      => string.Empty
    };

    public bool ShowDot => State != PrinterState.Offline;

    public IBrush DotBrush => State switch
    {
        PrinterState.Connected => ResolveBrush("SuccessBrush"),
        PrinterState.Error     => ResolveBrush("DangerBrush"),
        _                      => ResolveBrush("WarningBrush")
    };

    /// <summary>
    /// Start the poll loop. Factory may return null when no printer is configured;
    /// the model then reports Offline.
    /// </summary>
    public void Start(Func<IReceiptPrinter?> printerFactory)
    {
        _printerFactory = printerFactory ?? throw new ArgumentNullException(nameof(printerFactory));
        _timer?.Dispose();
        _timer = new Timer(_ => Poll(), null, TimeSpan.Zero, PollInterval);
    }

    public void Stop()
    {
        _timer?.Dispose();
        _timer = null;
    }

    private void Poll()
    {
        if (Interlocked.Exchange(ref _polling, 1) == 1) return;
        try
        {
            var factory = _printerFactory;
            if (factory is null)
            {
                State = PrinterState.Offline;
                return;
            }

            IReceiptPrinter? printer;
            try
            {
                printer = factory();
            }
            catch
            {
                State = PrinterState.Error;
                return;
            }

            if (printer is null)
            {
                // TODO: wire to runtime printer config; currently always reports Offline
                State = PrinterState.Offline;
                return;
            }

            try
            {
                State = printer.IsAvailable() ? PrinterState.Connected : PrinterState.Offline;
            }
            catch
            {
                State = PrinterState.Error;
            }
        }
        finally
        {
            Interlocked.Exchange(ref _polling, 0);
        }
    }

    private static IBrush ResolveBrush(string key)
    {
        var app = Application.Current;
        if (app is not null && app.Resources.TryGetResource(key, app.ActualThemeVariant, out var res) && res is IBrush brush)
        {
            return brush;
        }
        return Brushes.Gray;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
