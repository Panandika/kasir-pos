using Avalonia;
using System;
using System.Diagnostics;
using System.Threading.Tasks;
using Kasir.Avalonia.Diagnostics;
using Kasir.Services;
using Kasir.Utils;

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

        RegisterProcessCrashHandlers();

        try
        {
            // Finish/undo an update interrupted mid-copy and remove the leftover staging folder.
            UpdateApplier.RecoverInterrupted(AppContext.BaseDirectory);

            return BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
        }
        catch (Exception ex)
        {
            // Startup / UI-loop failure: leave a trace in logs\crash.log instead of vanishing.
            CrashLog.Write("Program.Main", ex);
            return 1;
        }
    }

    // Process-wide last-chance handlers, registered before Avalonia starts so failures
    // during framework init are logged too. UI-thread exceptions are handled (and the app
    // kept alive) by App's Dispatcher.UIThread.UnhandledException hook.
    private static void RegisterProcessCrashHandlers()
    {
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        {
            var ex = e.ExceptionObject as Exception
                ?? new Exception("Non-exception object thrown: " + e.ExceptionObject);
            CrashLog.Write(e.IsTerminating ? "AppDomain.UnhandledException (terminating)"
                                           : "AppDomain.UnhandledException", ex);
        };
        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            CrashLog.Write("TaskScheduler.UnobservedTaskException", e.Exception);
            e.SetObserved();
        };
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
