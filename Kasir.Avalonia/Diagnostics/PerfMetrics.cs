using System;
using System.Diagnostics;
using System.IO;
using System.Text;

namespace Kasir.Avalonia.Diagnostics;

public static class PerfMetrics
{
    public record Budget(string Name, long LimitMs);

    public static readonly Budget KeypressEcho      = new("keypress_echo",           16);
    public static readonly Budget BarcodeScanLine   = new("barcode_scan_to_line",    50);
    public static readonly Budget F8PaymentVisible  = new("f8_to_payment_visible",  100);
    public static readonly Budget SaleCommit        = new("sale_commit",             200);
    public static readonly Budget ProductSearch     = new("fts5_product_search",      30);
    public static readonly Budget FormOpenCold      = new("form_open_cold",          150);
    public static readonly Budget FormOpenWarm      = new("form_open_warm",           50);
    public static readonly Budget AppStartup        = new("app_startup",            3000);

    private static readonly string _logPath =
        Path.Combine(AppContext.BaseDirectory, "perf.log");

    // Returns a disposable scope; on Dispose it records elapsed time.
    public static IDisposable Measure(Budget b)
    {
        return new Scope(b);
    }

    // One-shot record — writes "[PERF] name Xms PASS|FAIL(budget=Y)" to console + perf.log.
    public static void Record(Budget b, long elapsedMs)
    {
        bool pass = elapsedMs <= b.LimitMs;
        var sb = new StringBuilder(64);
        sb.Append("[PERF] ");
        sb.Append(b.Name);
        sb.Append(' ');
        sb.Append(elapsedMs);
        sb.Append("ms ");
        if (pass)
        {
            sb.Append("PASS");
        }
        else
        {
            sb.Append("FAIL(budget=");
            sb.Append(b.LimitMs);
            sb.Append(')');
        }

        string line = sb.ToString();
        Console.WriteLine(line);
        try
        {
            File.AppendAllText(_logPath, line + Environment.NewLine);
        }
        catch
        {
            // Never let logging crash the app.
        }
    }

    private sealed class Scope : IDisposable
    {
        private readonly Budget _budget;
        private readonly Stopwatch _sw;

        public Scope(Budget budget)
        {
            _budget = budget;
            _sw = Stopwatch.StartNew();
        }

        public void Dispose()
        {
            _sw.Stop();
            Record(_budget, _sw.ElapsedMilliseconds);
        }
    }
}
