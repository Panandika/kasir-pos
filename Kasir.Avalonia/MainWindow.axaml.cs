using System;
using Avalonia.Controls;
using Avalonia.Threading;

namespace Kasir.Avalonia;

public partial class MainWindow : Window
{
    private readonly DispatcherTimer _clockTimer;

    public MainWindow()
    {
        InitializeComponent();
        _clockTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _clockTimer.Tick += (_, _) => UpdateClock();
        _clockTimer.Start();
        UpdateClock();
        Closed += (_, _) => _clockTimer.Stop();
    }

    private void UpdateClock()
    {
        if (ClockLabel != null)
            ClockLabel.Text = DateTime.Now.ToString("HH:mm:ss");
    }
}
