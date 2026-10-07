using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Kasir.Services
{
    /// <summary>
    /// Copies a verified, staged release over the installed app. Port of the old
    /// Updater.exe logic (wait for the POS to exit, back up, copy, never touch data,
    /// roll back on failure, relaunch), run by the NEW app build itself:
    ///
    ///   update-staging\app\Kasir.Avalonia.exe --apply-update --source &lt;staged app&gt;
    ///       --target &lt;install dir&gt; --pid &lt;old POS pid&gt; --exe Kasir.Avalonia.exe
    ///
    /// The staged folder is a complete self-contained release, so it runs on its own
    /// while the install folder is being replaced. It never opens the database.
    /// </summary>
    public static class UpdateApplier
    {
        public const string ApplyFlag = "--apply-update";
        public const string StateFile = "update-state.txt";
        public const string LogFile = "updater.log";
        public const string BackupDir = "update-backup";
        public const string MarkerFile = "update-complete.marker";
        public const string StagingDir = "update-staging";
        public const string HelpJson = "help.json";

        // Never copied into (or backed up from) the install folder: data and runtime state.
        private static readonly HashSet<string> SkipDirs = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "data", "logs", StagingDir, BackupDir, "backup"
        };

        private static readonly HashSet<string> SkipFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            StateFile, LogFile, MarkerFile, "perf.log"
        };

        public sealed class ApplyArgs
        {
            public string Source;
            public string Target;
            public int Pid;
            public string Exe;
        }

        public static bool IsApplyInvocation(string[] args)
        {
            if (args == null) return false;
            foreach (var a in args)
                if (string.Equals(a, ApplyFlag, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        public static bool TryParseArgs(string[] args, out ApplyArgs parsed)
        {
            parsed = null;
            if (!IsApplyInvocation(args)) return false;
            var a = new ApplyArgs { Pid = -1 };
            for (int i = 0; i < args.Length - 1; i++)
            {
                switch (args[i])
                {
                    case "--source": a.Source = args[++i]; break;
                    case "--target": a.Target = args[++i]; break;
                    case "--pid": int.TryParse(args[++i], out a.Pid); break;
                    case "--exe": a.Exe = args[++i]; break;
                }
            }
            if (string.IsNullOrEmpty(a.Source) || string.IsNullOrEmpty(a.Target) || a.Pid < 0 || string.IsNullOrEmpty(a.Exe))
                return false;
            parsed = a;
            return true;
        }

        /// <summary>
        /// Relative paths (forward slashes) of the files to install from
        /// <paramref name="source"/>. Skips data/state folders and files, and never
        /// overwrites an existing help.json in the target (site values win, and older
        /// installs may carry a Bantuan login there); <see cref="Run"/> merges new
        /// keys into it instead via <see cref="MergeHelpJson"/>.
        /// </summary>
        public static List<string> FilesToInstall(string source, string target)
        {
            var result = new List<string>();
            string root = Path.GetFullPath(source);
            foreach (var full in Directory.GetFiles(root, "*", SearchOption.AllDirectories))
            {
                string rel = Path.GetRelativePath(root, full).Replace('\\', '/');
                string first = rel.Contains("/") ? rel.Substring(0, rel.IndexOf('/')) : null;
                if (first != null && SkipDirs.Contains(first)) continue;
                string name = Path.GetFileName(rel);
                if (first == null && SkipFiles.Contains(name)) continue;
                if (first == null && name.EndsWith(".db", StringComparison.OrdinalIgnoreCase)) continue;
                if (first == null && string.Equals(name, HelpJson, StringComparison.OrdinalIgnoreCase)
                    && File.Exists(Path.Combine(target, HelpJson)))
                    continue;
                result.Add(rel);
            }
            return result;
        }

        /// <summary>Runs the update. Returns 0 on success, 1 on failure (after rolling back).</summary>
        public static int Run(ApplyArgs a, Func<int, bool> waitForExit = null, Action<string> launch = null)
        {
            waitForExit = waitForExit ?? WaitForProcessExit;
            launch = launch ?? Launch;
            string target = Path.GetFullPath(a.Target);
            string backup = Path.Combine(target, BackupDir);

            try
            {
                Log(target, $"Update started. Source={a.Source} Target={target} PID={a.Pid}");
                WriteState(target, "WAITING");
                if (!waitForExit(a.Pid))
                {
                    Log(target, "ERROR: POS did not exit within timeout; update cancelled.");
                    WriteState(target, "ROLLED_BACK");
                    launch(Path.Combine(target, a.Exe));
                    return 1;
                }

                var files = FilesToInstall(a.Source, target);
                string packagedHelp = Path.Combine(a.Source, HelpJson);
                string installedHelp = Path.Combine(target, HelpJson);
                bool mergeHelp = File.Exists(packagedHelp) && File.Exists(installedHelp);

                // Back up every file the update will overwrite (relative layout preserved).
                if (Directory.Exists(backup)) Directory.Delete(backup, true);
                Directory.CreateDirectory(backup);
                foreach (var rel in files)
                {
                    string existing = Path.Combine(target, rel);
                    if (!File.Exists(existing)) continue;
                    string dest = Path.Combine(backup, rel);
                    Directory.CreateDirectory(Path.GetDirectoryName(dest));
                    File.Copy(existing, dest, true);
                }
                // help.json is merged in place rather than copied, so back it up too:
                // rollback must restore the pre-merge file.
                if (mergeHelp) File.Copy(installedHelp, Path.Combine(backup, HelpJson), true);
                WriteState(target, "BACKUP_COMPLETE");
                Log(target, $"Backup complete ({files.Count} files in package).");

                WriteState(target, "COPY_IN_PROGRESS");
                foreach (var rel in files)
                {
                    string dest = Path.Combine(target, rel);
                    Directory.CreateDirectory(Path.GetDirectoryName(dest));
                    File.Copy(Path.Combine(a.Source, rel), dest, true);
                }
                if (mergeHelp)
                    MergeHelpJson(packagedHelp, installedHelp, msg => Log(target, msg));
                WriteState(target, "COPY_COMPLETE");

                string versionFile = Path.Combine(a.Source, "version.txt");
                string version = File.Exists(versionFile) ? File.ReadAllText(versionFile).Trim() : "unknown";
                File.WriteAllText(Path.Combine(target, MarkerFile), version);
                Log(target, "Update complete: " + version);

                launch(Path.Combine(target, a.Exe));
                return 0;
            }
            catch (Exception ex)
            {
                Log(target, "ERROR: " + ex);
                try
                {
                    Rollback(target);
                }
                catch (Exception rex)
                {
                    Log(target, "ROLLBACK FAILED: " + rex.Message);
                }
                try { launch(Path.Combine(target, a.Exe)); } catch { /* best effort */ }
                return 1;
            }
        }

        /// <summary>
        /// Adds top-level keys that the new release's help.json has but the installed
        /// one lacks (e.g. DashboardUrl added in a later release). Installed values are
        /// never changed: they may be operator/site edits. Legacy keys only present in
        /// the installed file (MachineEmail/MachinePassword from pre-2.9 zips) are kept:
        /// the file is local to the PC, and HelpConfigLoader still falls back to them on
        /// registers that were never paired, so stripping them would break Bantuan there.
        ///
        /// Writes a temp file then replaces the original, so a crash never leaves a
        /// half-written help.json. If either file is malformed (or not a JSON object),
        /// the installed file is left untouched and the reason is logged. Never throws.
        /// Returns the number of keys added.
        /// </summary>
        public static int MergeHelpJson(string packagedPath, string installedPath, Action<string> log)
        {
            string tmp = installedPath + ".tmp";
            try
            {
                if (!(JsonNode.Parse(File.ReadAllText(packagedPath)) is JsonObject packaged))
                {
                    log("help.json merge skipped: packaged help.json is not a JSON object.");
                    return 0;
                }
                if (!(JsonNode.Parse(File.ReadAllText(installedPath)) is JsonObject installed))
                {
                    log("help.json merge skipped: installed help.json is not a JSON object.");
                    return 0;
                }

                var added = new List<string>();
                foreach (var kv in packaged)
                {
                    if (installed.ContainsKey(kv.Key)) continue;
                    installed[kv.Key] = kv.Value?.DeepClone();
                    added.Add(kv.Key);
                }
                if (added.Count == 0) return 0;

                // Relaxed escaping keeps URLs readable ('&' not '\u0026'): the file is
                // local and operator-editable, never embedded in HTML.
                var options = new JsonSerializerOptions
                {
                    WriteIndented = true,
                    Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
                };
                File.WriteAllText(tmp, installed.ToJsonString(options));
                File.Move(tmp, installedPath, true);
                log("help.json merged; added keys: " + string.Join(", ", added));
                return added.Count;
            }
            catch (Exception ex)
            {
                log("help.json merge skipped, installed file kept: " + ex.GetType().Name + ": " + ex.Message);
                try { if (File.Exists(tmp)) File.Delete(tmp); } catch { /* best effort */ }
                return 0;
            }
        }

        /// <summary>
        /// Called at normal app start. If a previous update was interrupted mid-copy
        /// (power loss, crash), restore the backed-up files; then clear stale state and
        /// the staging folder. Never throws.
        /// </summary>
        public static void RecoverInterrupted(string target)
        {
            try
            {
                string statePath = Path.Combine(target, StateFile);
                string state = File.Exists(statePath) ? File.ReadAllText(statePath).Trim() : "";
                if (state == "COPY_IN_PROGRESS" || state == "BACKUP_COMPLETE")
                {
                    Log(target, "Interrupted update detected (" + state + "); restoring backup.");
                    Rollback(target);
                }
                else if (state == "COPY_COMPLETE" || state == "ROLLED_BACK")
                {
                    File.Delete(statePath);
                }

                if (state != "WAITING")
                {
                    string staging = Path.Combine(target, StagingDir);
                    if (Directory.Exists(staging)) Directory.Delete(staging, true);
                }
            }
            catch
            {
                // Best effort: a locked staging folder is retried on the next start.
            }
        }

        private static void Rollback(string target)
        {
            string backup = Path.Combine(target, BackupDir);
            if (Directory.Exists(backup))
            {
                foreach (var full in Directory.GetFiles(backup, "*", SearchOption.AllDirectories))
                {
                    string rel = Path.GetRelativePath(backup, full);
                    string dest = Path.Combine(target, rel);
                    Directory.CreateDirectory(Path.GetDirectoryName(dest));
                    File.Copy(full, dest, true);
                }
            }
            WriteState(target, "ROLLED_BACK");
            Log(target, "Rolled back to previous version.");
        }

        private static bool WaitForProcessExit(int pid)
        {
            try
            {
                using (var p = Process.GetProcessById(pid))
                    return p.WaitForExit(30000);
            }
            catch (ArgumentException)
            {
                return true; // already exited
            }
            catch (InvalidOperationException)
            {
                return true;
            }
        }

        private static void Launch(string exePath)
        {
            if (File.Exists(exePath))
                Process.Start(new ProcessStartInfo(exePath) { UseShellExecute = true, WorkingDirectory = Path.GetDirectoryName(exePath) });
        }

        private static void WriteState(string target, string state)
        {
            File.WriteAllText(Path.Combine(target, StateFile), state);
        }

        private static void Log(string target, string message)
        {
            try
            {
                File.AppendAllText(Path.Combine(target, LogFile),
                    DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + " " + message + Environment.NewLine);
            }
            catch
            {
                // logging must never break the update
            }
        }
    }
}
