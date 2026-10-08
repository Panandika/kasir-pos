using System;
using System.Threading.Tasks;
using Kasir.Data.Repositories;
using Kasir.Hardware;

namespace Kasir.Avalonia.Infrastructure;

/// <summary>Outcome of the one-shot printer check SaleView runs when it opens.</summary>
public readonly record struct PrinterHealth(bool Configured, bool Ok, string? Warning);

/// <summary>
/// One-shot printer check for the sale screen footer. The ConfigRepository usually wraps
/// the UI thread's shared connection (DbConnection.GetConnection()), which is not
/// thread-safe: every config read happens here on the calling thread, and only the
/// hardware probe runs on the thread pool.
/// </summary>
public static class PrinterHealthCheck
{
    public static Task<PrinterHealth> RunAsync(ConfigRepository config)
        => RunAsync(config, PrinterDiscovery.GetWindowsPrinterStatus);

    public static async Task<PrinterHealth> RunAsync(ConfigRepository config, Func<string, string?> windowsStatus)
    {
        string kind = config.Get("printer_kind") ?? "";
        string name = config.Get("printer_name") ?? "";

        if (string.IsNullOrEmpty(name)) return new PrinterHealth(false, false, null);

        bool windowsQueue = IsWindowsQueue(kind, name);
        // ReceiptPrinter(ConfigRepository) reads printer_kind/name/baud in its constructor,
        // so build it here; the probe below only touches the device.
        IReceiptPrinter? printer = windowsQueue ? null : new ReceiptPrinter(config);

        var (warning, ok) = await Task.Run(() =>
        {
            if (printer is null)
            {
                string? status = windowsStatus(name);
                return status switch
                {
                    null or "ready" or "printing" or "warmup" or "other" or "unknown" => ((string?)null, true),
                    "paused"     => ($"Printer '{name}' di-pause di Windows", false),
                    "offline"    => ($"Printer '{name}' offline", false),
                    "not_found"  => ($"Printer '{name}' tidak ditemukan", false),
                    _            => ($"Printer '{name}' status: {status}", false),
                };
            }

            if (printer.IsAvailable()) return ((string?)null, true);
            return (printer.LastError ?? "tidak tersedia", false);
        }).ConfigureAwait(false);

        return new PrinterHealth(true, ok, warning);
    }

    // For Windows queues the WMI status check is instant and doesn't open a print job.
    public static bool IsWindowsQueue(string kind, string name)
        => kind == "windows"
           || (string.IsNullOrEmpty(kind)
               && !name.StartsWith("COM", StringComparison.OrdinalIgnoreCase)
               && !name.StartsWith("LPT", StringComparison.OrdinalIgnoreCase)
               && !name.StartsWith("/dev/", StringComparison.OrdinalIgnoreCase));
}
