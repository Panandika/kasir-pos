using System.IO;
using System.Runtime.InteropServices;

namespace Kasir.Utils
{
    public class FileSystemImpl : IFileSystem
    {
        public bool DirectoryExists(string path)
        {
            return Directory.Exists(path);
        }

        public bool FileExists(string path)
        {
            return File.Exists(path);
        }

        public string ReadAllText(string path)
        {
            return File.ReadAllText(path);
        }

        public string[] GetFiles(string path, string pattern, bool recurse)
        {
            return Directory.GetFiles(path, pattern,
                recurse ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly);
        }

        public string[] GetDirectories(string path)
        {
            return Directory.GetDirectories(path);
        }

        public void CopyFile(string source, string dest, bool overwrite)
        {
            File.Copy(source, dest, overwrite);
        }

        public void CreateDirectory(string path)
        {
            Directory.CreateDirectory(path);
        }

        public void DeleteDirectory(string path, bool recursive)
        {
            Directory.Delete(path, recursive);
        }

        public long GetFileSize(string path)
        {
            return new FileInfo(path).Length;
        }

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Auto)]
        private static extern bool GetDiskFreeSpaceEx(
            string lpDirectoryName,
            out ulong lpFreeBytesAvailableToCaller,
            out ulong lpTotalNumberOfBytes,
            out ulong lpTotalNumberOfFreeBytes);

        public long GetAvailableDiskSpace(string path)
        {
            ulong freeBytesAvailable, totalBytes, totalFreeBytes;
            if (GetDiskFreeSpaceEx(path, out freeBytesAvailable, out totalBytes, out totalFreeBytes))
            {
                return (long)freeBytesAvailable;
            }

            // Fallback for local paths
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

            return long.MaxValue;
        }
    }
}
