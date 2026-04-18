using System.IO;

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

        public long GetAvailableDiskSpace(string path)
        {
            try
            {
                string root = Path.GetPathRoot(Path.GetFullPath(path));
                if (!string.IsNullOrEmpty(root))
                {
                    return new DriveInfo(root).AvailableFreeSpace;
                }
            }
            catch
            {
                // ignore — unknown drive or network path
            }

            return long.MaxValue;
        }
    }
}
