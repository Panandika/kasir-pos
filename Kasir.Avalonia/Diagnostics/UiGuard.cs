using System;
using System.Threading.Tasks;

namespace Kasir.Avalonia.Diagnostics;

/// <summary>
/// Wraps the body of an <c>async void</c> UI handler so an exception (e.g. a
/// SqliteException on the barcode path) is logged to crash.log and handed to the
/// screen's own error display instead of escaping the handler (F08/F28).
/// The action runs synchronously up to its first await, so key handlers can still
/// set <c>e.Handled</c> inside it. Never throws.
/// </summary>
public static class UiGuard
{
    public static async Task RunAsync(string source, Func<Task> action, Func<Exception, Task> onError,
        Action<string, Exception>? log = null)
    {
        log ??= CrashLog.Write;
        try
        {
            await action();
        }
        catch (Exception ex)
        {
            log(source, ex);
            try
            {
                await onError(ex);
            }
            catch (Exception handlerEx)
            {
                // The error display itself failed; keep the trace, never rethrow.
                log(source + " (error handler)", handlerEx);
            }
        }
    }
}
