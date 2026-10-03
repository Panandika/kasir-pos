using Avalonia;
using System;
using System.Diagnostics;
using Kasir.Avalonia.Diagnostics;
using Kasir.Services;

namespace Kasir.Avalonia;

class Program
{
    // Startup stopwatch — started before Avalonia initialises; stopped when main window opens.
    internal static readonly Stopwatch StartupWatch = Stopwatch.StartNew();

    // Initialization code. Don't use any Avalonia, third-party APIs or any
    // SynchronizationContext-reliant code before AppMain is called: things aren't initialized
    // yet and stuff might break.
    [STAThread]
    public static int Main(string[] args)
    {
        // Self-update: the staged NEW build is started with --apply-update to replace the
        // installed files once the old POS has exited (UpdateService.ApplyUpdate). It runs
        // headless and never opens the database.
        if (UpdateApplier.IsApplyInvocation(args))
        {
            return UpdateApplier.TryParseArgs(args, out var applyArgs) ? UpdateApplier.Run(applyArgs) : 2;
        }

        // Finish/undo an update interrupted mid-copy and remove the leftover staging folder.
        UpdateApplier.RecoverInterrupted(AppContext.BaseDirectory);

        return BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

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
