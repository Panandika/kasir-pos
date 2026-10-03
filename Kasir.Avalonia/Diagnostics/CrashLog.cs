using System;
using System.IO;

namespace Kasir.Avalonia.Diagnostics;

/// <summary>
/// Append-only local error log at &lt;app folder&gt;\logs\crash.log. Used by the global
/// unhandled-exception handlers and by features that fail gracefully, so a failure
/// leaves a trace even when nothing reaches the cloud. Never throws.
/// </summary>
public static class CrashLog
{
    private const long MaxBytes = 1024 * 1024; // rotate to crash.log.old at 1 MB
    private static readonly object Gate = new object();

    public static string LogPath =>
        Path.Combine(AppContext.BaseDirectory, "logs", "crash.log");

    public static void Write(string source, Exception ex)
    {
        try
        {
            lock (Gate)
            {
                string path = LogPath;
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                var info = new FileInfo(path);
                if (info.Exists && info.Length > MaxBytes)
                {
                    File.Copy(path, path + ".old", overwrite: true);
                    File.Delete(path);
                }
                File.AppendAllText(path,
                    $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {source}{Environment.NewLine}{ex}{Environment.NewLine}{Environment.NewLine}");
            }
        }
        catch
        {
            // Logging must never take the app down.
        }
    }
}
