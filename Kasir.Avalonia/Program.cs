using Avalonia;
using System;
using System.Diagnostics;
using Kasir.Avalonia.Diagnostics;

namespace Kasir.Avalonia;

class Program
{
    // Startup stopwatch — started before Avalonia initialises; stopped when main window opens.
    internal static readonly Stopwatch StartupWatch = Stopwatch.StartNew();

    // Initialization code. Don't use any Avalonia, third-party APIs or any
    // SynchronizationContext-reliant code before AppMain is called: things aren't initialized
    // yet and stuff might break.
    [STAThread]
    public static void Main(string[] args) => BuildAvaloniaApp()
        .StartWithClassicDesktopLifetime(args);

    // Avalonia configuration, don't remove; also used by visual designer.
    public static AppBuilder BuildAvaloniaApp()
    {
        var b = AppBuilder.Configure<App>().UsePlatformDetect();
#if DEBUG
        // DevTools (F12) crashes on macOS when the native bundle isn't installed.
        // See AvaloniaUI/Avalonia#14457 — F12 gesture is hardcoded in DiagnosticsSupport.
        if (!OperatingSystem.IsMacOS())
            b = b.WithDeveloperTools();
#endif
        return b.WithInterFont().LogToTrace();
    }
}
