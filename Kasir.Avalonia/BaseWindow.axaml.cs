using System;
using System.Threading;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;

namespace Kasir.Avalonia;

public partial class BaseWindow : Window
{
    private readonly DispatcherTimer _clockTimer;

    public BaseWindow()
    {
        InitializeComponent();
        _clockTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _clockTimer.Tick += (_, _) => UpdateClock();
        _clockTimer.Start();
        UpdateClock();
        Closed += (_, _) => _clockTimer.Stop();
    }

    protected void SetStatus(string text)
    {
        if (StatusLabel != null)
            StatusLabel.Text = text;
    }

    protected void ClearStatus() => SetStatus(string.Empty);

    private void UpdateClock()
    {
        if (ClockLabel != null)
            ClockLabel.Text = DateTime.Now.ToString("HH:mm:ss");
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (KeyboardRouter.IsEscape(e))
        {
            e.Handled = true;
            Close();
        }
    }
}
