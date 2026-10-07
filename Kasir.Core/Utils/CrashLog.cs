using System;
using System.Globalization;
using System.IO;
using System.Text;

namespace Kasir.Utils
{
    /// <summary>
    /// Append-only local error log at &lt;app folder&gt;\logs\crash.log. Used by the global
    /// unhandled-exception handlers and by features that fail gracefully, so a failure
    /// leaves a trace even when nothing reaches the cloud. Never throws.
    /// </summary>
    public static class CrashLog
    {
        public const long DefaultMaxBytes = 1024 * 1024; // rotate to crash.log.old at 1 MB
        private const int MaxInnerDepth = 10;
        private static readonly object Gate = new object();

        public static string LogPath =>
            Path.Combine(AppContext.BaseDirectory, "logs", "crash.log");

        public static void Write(string source, Exception ex)
        {
            WriteTo(LogPath, source, ex, DateTime.Now, DefaultMaxBytes);
        }

        /// <summary>Appends one entry to <paramref name="path"/>; false when it could not be written.</summary>
        public static bool WriteTo(string path, string source, Exception ex, DateTime timestamp, long maxBytes)
        {
            try
            {
                if (string.IsNullOrEmpty(path)) return false;
                string entry = FormatEntry(source, ex, timestamp, SafeVersion());
                lock (Gate)
                {
                    string dir = Path.GetDirectoryName(path);
                    if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
                    var info = new FileInfo(path);
                    if (info.Exists && info.Length > maxBytes)
                    {
                        File.Move(path, path + ".old", overwrite: true);
                    }
                    File.AppendAllText(path, entry);
                }
                return true;
            }
            catch
            {
                // Logging must never take the app down.
                return false;
            }
        }

        public static string FormatEntry(string source, Exception ex, DateTime timestamp, string version)
        {
            var sb = new StringBuilder();
            try
            {
                sb.Append("==== ").Append(timestamp.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture))
                  .Append(" | v").Append(version)
                  .Append(" | ").Append(source).AppendLine(" ====");
                if (ex == null)
                {
                    sb.AppendLine("(no exception object)");
                }
                else
                {
                    AppendException(sb, ex, 0);
                }
            }
            catch
            {
                sb.AppendLine("(failed to format exception)");
            }
            sb.AppendLine();
            return sb.ToString();
        }

        private static void AppendException(StringBuilder sb, Exception ex, int depth)
        {
            if (depth > 0) sb.Append("--- Inner exception (").Append(depth).AppendLine(") ---");
            sb.Append("Type: ").AppendLine(ex.GetType().FullName);
            sb.Append("Message: ").AppendLine(Safe(() => ex.Message));
            sb.AppendLine("Stack:");
            sb.AppendLine(Safe(() => ex.StackTrace) ?? "   (no stack trace)");

            if (depth >= MaxInnerDepth) return;
            if (ex is AggregateException agg)
            {
                foreach (var inner in agg.InnerExceptions)
                {
                    if (inner != null) AppendException(sb, inner, depth + 1);
                }
            }
            else if (ex.InnerException != null)
            {
                AppendException(sb, ex.InnerException, depth + 1);
            }
        }

        private static string Safe(Func<string> get)
        {
            try { return get(); }
            catch (Exception e) { return "(unreadable: " + e.GetType().Name + ")"; }
        }

        private static string SafeVersion()
        {
            try { return AppVersion.Current; }
            catch { return "?"; }
        }
    }
}
