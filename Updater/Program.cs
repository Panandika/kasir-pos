using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;

namespace Updater
{
    class Program
    {
        private const string StateFile = "update-state.txt";
        private const string MarkerFile = "update-complete.marker";
        private const string LogFile = "updater.log";
        private const string BackupDir = "backup";
        private const int WaitTimeoutMs = 30000;
        private const int PollIntervalMs = 500;

        private static string _logPath;

        static int Main(string[] args)
        {
            string source = null;
            string target = null;
            int pid = -1;

            // Parse arguments
            for (int i = 0; i < args.Length - 1; i++)
            {
                switch (args[i])
                {
                    case "--source": source = args[++i]; break;
                    case "--target": target = args[++i]; break;
                    case "--pid": int.TryParse(args[++i], out pid); break;
                }
            }

            if (string.IsNullOrEmpty(source) || string.IsNullOrEmpty(target) || pid < 0)
            {
                Console.WriteLine("Usage: Updater.exe --source <path> --target <path> --pid <id>");
                return 1;
            }

            _logPath = Path.Combine(target, LogFile);

            try
            {
                return RunUpdate(source, target, pid);
            }
            catch (Exception ex)
            {
                Log("FATAL: " + ex.ToString());
                TryRecover(target);
                return 1;
            }
        }

        private static int RunUpdate(string source, string target, int pid)
        {
            Log(string.Format("Updater started. Source={0}, Target={1}, PID={2}", source, target, pid));

            // Step 1: Write initial state
            WriteState(target, "WAITING");

            // Step 2: Wait for the main app to exit
            Log("Waiting for process " + pid + " to exit...");
            if (!WaitForProcessExit(pid))
            {
                Log("ERROR: Process did not exit within timeout.");
                return 1;
            }
            Log("Process exited.");

            // Step 3: Pre-flight disk space check
            long sourceSize = GetDirectorySize(source);
            long requiredSpace = sourceSize * 3;
            long freeSpace = GetAvailableDiskSpace(target);
            Log(string.Format("Disk check: source={0}MB, required={1}MB, free={2}MB",
                sourceSize / (1024 * 1024), requiredSpace / (1024 * 1024), freeSpace / (1024 * 1024)));

            if (freeSpace < requiredSpace)
            {
                Log("ERROR: Insufficient disk space.");
                return 1;
            }

            // Step 4: Backup current files
            string backupPath = Path.Combine(target, BackupDir);
            BackupCurrentFiles(target, backupPath);
            WriteState(target, "BACKUP_COMPLETE");
            Log("Backup complete.");

            // Step 5: Copy new files
            WriteState(target, "COPY_IN_PROGRESS");
            CopyNewFiles(source, target);
            Log("Copy complete.");

            // Step 6: Mark completion
            WriteState(target, "COPY_COMPLETE");

            // Step 7: Write update marker
            string version = ReadVersionFromSource(source);
            File.WriteAllText(Path.Combine(target, MarkerFile), version);
            Log("Update marker written: " + version);

            // Step 8: Clean up staging directory
            try
            {
                if (Directory.Exists(source))
                {
                    Directory.Delete(source, true);
                }
            }
            catch (Exception ex)
            {
                Log("Warning: Could not clean staging dir: " + ex.Message);
            }

            // Step 9: Launch the updated app
            string exePath = Path.Combine(target, "Kasir.exe");
            if (File.Exists(exePath))
            {
                Log("Launching " + exePath);
                Process.Start(exePath);
            }
            else
            {
                Log("WARNING: Kasir.exe not found at " + exePath);
            }

            Log("Updater finished successfully.");
            return 0;
        }

        private static bool WaitForProcessExit(int pid)
        {
            try
            {
                using (var proc = Process.GetProcessById(pid))
                {
                    return proc.WaitForExit(WaitTimeoutMs);
                }
            }
            catch (ArgumentException)
            {
                // Process already exited
                return true;
            }
            catch (Exception ex)
            {
                Log("Warning waiting for PID: " + ex.Message);
                // Assume it exited
                return true;
            }
        }

        private static void BackupCurrentFiles(string target, string backupPath)
        {
            // Clean previous backup
            if (Directory.Exists(backupPath))
            {
                Directory.Delete(backupPath, true);
            }
            Directory.CreateDirectory(backupPath);

            string[] extensions = new string[] { "*.exe", "*.dll", "*.pdb" };
            foreach (string pattern in extensions)
            {
                foreach (string file in Directory.GetFiles(target, pattern))
                {
                    string name = Path.GetFileName(file);

                    // Skip files that should not be backed up
                    if (name.Equals("Updater.exe", StringComparison.OrdinalIgnoreCase))
                        continue;

                    string dest = Path.Combine(backupPath, name);
                    Log("Backup: " + name);
                    File.Move(file, dest);
                }
            }

            // Backup x86 subdirectory (native DLLs)
            string x86Dir = Path.Combine(target, "x86");
            if (Directory.Exists(x86Dir))
            {
                string x86Backup = Path.Combine(backupPath, "x86");
                Directory.CreateDirectory(x86Backup);
                foreach (string file in Directory.GetFiles(x86Dir))
                {
                    string name = Path.GetFileName(file);
                    File.Move(file, Path.Combine(x86Backup, name));
                }
            }
        }

        private static void CopyNewFiles(string source, string target)
        {
            foreach (string file in Directory.GetFiles(source))
            {
                string name = Path.GetFileName(file);

                // Never overwrite config
                if (name.Equals("Kasir.exe.config", StringComparison.OrdinalIgnoreCase))
                    continue;

                string dest = Path.Combine(target, name);
                Log("Copy: " + name);
                File.Copy(file, dest, true);
            }

            // Copy subdirectories (e.g., x86/)
            foreach (string dir in Directory.GetDirectories(source))
            {
                string dirName = Path.GetFileName(dir);

                // Skip data directory
                if (dirName.Equals("data", StringComparison.OrdinalIgnoreCase))
                    continue;

                string destDir = Path.Combine(target, dirName);
                if (!Directory.Exists(destDir))
                {
                    Directory.CreateDirectory(destDir);
                }

                foreach (string file in Directory.GetFiles(dir))
                {
                    string name = Path.GetFileName(file);
                    string dest = Path.Combine(destDir, name);
                    Log("Copy: " + dirName + "/" + name);
                    File.Copy(file, dest, true);
                }
            }
        }

        private static void TryRecover(string target)
        {
            string state = ReadState(target);
            if (state != "COPY_IN_PROGRESS")
                return;

            Log("Attempting recovery from backup...");
            string backupPath = Path.Combine(target, BackupDir);
            if (!Directory.Exists(backupPath))
            {
                Log("ERROR: No backup directory found. Cannot recover.");
                return;
            }

            try
            {
                // Restore files from backup
                foreach (string file in Directory.GetFiles(backupPath))
                {
                    string name = Path.GetFileName(file);
                    string dest = Path.Combine(target, name);
                    if (File.Exists(dest))
                    {
                        File.Delete(dest);
                    }
                    File.Move(file, dest);
                }

                // Restore x86 subdirectory
                string x86Backup = Path.Combine(backupPath, "x86");
                if (Directory.Exists(x86Backup))
                {
                    string x86Dir = Path.Combine(target, "x86");
                    if (!Directory.Exists(x86Dir))
                    {
                        Directory.CreateDirectory(x86Dir);
                    }
                    foreach (string file in Directory.GetFiles(x86Backup))
                    {
                        string name = Path.GetFileName(file);
                        string dest = Path.Combine(x86Dir, name);
                        if (File.Exists(dest))
                        {
                            File.Delete(dest);
                        }
                        File.Move(file, dest);
                    }
                }

                WriteState(target, "ROLLED_BACK");
                Log("Recovery complete. Rolled back to previous version.");

                // Try to launch the restored app
                string exePath = Path.Combine(target, "Kasir.exe");
                if (File.Exists(exePath))
                {
                    Process.Start(exePath);
                }
            }
            catch (Exception ex)
            {
                Log("RECOVERY FAILED: " + ex.Message);
            }
        }

        private static string ReadVersionFromSource(string source)
        {
            string versionFile = Path.Combine(source, "version.txt");
            if (File.Exists(versionFile))
            {
                return File.ReadAllText(versionFile).Trim();
            }
            return "unknown";
        }

        private static void WriteState(string target, string state)
        {
            File.WriteAllText(Path.Combine(target, StateFile), state);
        }

        private static string ReadState(string target)
        {
            string path = Path.Combine(target, StateFile);
            if (File.Exists(path))
            {
                return File.ReadAllText(path).Trim();
            }
            return "";
        }

        private static long GetDirectorySize(string path)
        {
            long size = 0;
            foreach (string file in Directory.GetFiles(path, "*", SearchOption.AllDirectories))
            {
                size += new FileInfo(file).Length;
            }
            return size;
        }

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Auto)]
        private static extern bool GetDiskFreeSpaceEx(
            string lpDirectoryName,
            out ulong lpFreeBytesAvailableToCaller,
            out ulong lpTotalNumberOfBytes,
            out ulong lpTotalNumberOfFreeBytes);

        private static long GetAvailableDiskSpace(string path)
        {
            ulong freeBytesAvailable, totalBytes, totalFreeBytes;
            if (GetDiskFreeSpaceEx(path, out freeBytesAvailable, out totalBytes, out totalFreeBytes))
            {
                return (long)freeBytesAvailable;
            }

            // Fallback: try DriveInfo for local paths
            try
            {
                string root = Path.GetPathRoot(path);
                if (!string.IsNullOrEmpty(root) && root.Length >= 2)
                {
                    var drive = new DriveInfo(root.Substring(0, 1));
                    return drive.AvailableFreeSpace;
                }
            }
            catch
            {
                // Ignore
            }

            return long.MaxValue; // Assume enough space if we can't check
        }

        private static void Log(string message)
        {
            string line = string.Format("[{0}] {1}", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"), message);
            Console.WriteLine(line);
            try
            {
                if (!string.IsNullOrEmpty(_logPath))
                {
                    File.AppendAllText(_logPath, line + Environment.NewLine);
                }
            }
            catch
            {
                // Don't fail if we can't write the log
            }
        }
    }
}
