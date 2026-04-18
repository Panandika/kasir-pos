using System.IO;
using System.Linq;

namespace Kasir.Sync
{
    public class SyncFileReader : ISyncFileReader
    {
        public string[] ListFiles(string directory, string pattern)
        {
            if (!Directory.Exists(directory))
            {
                return new string[0];
            }

            return Directory.GetFiles(directory, pattern)
                .OrderBy(f => f)
                .Take(SyncConfig.MaxInboxFiles)
                .ToArray();
        }

        public string Read(string path)
        {
            if (!File.Exists(path))
            {
                return null;
            }

            var fileInfo = new FileInfo(path);
            if (fileInfo.Length > SyncConfig.MaxFileSizeBytes)
            {
                throw new System.IO.InvalidDataException(
                    string.Format("Sync file too large: {0} bytes (max {1})",
                        fileInfo.Length, SyncConfig.MaxFileSizeBytes));
            }

            return File.ReadAllText(path);
        }

        public void Delete(string path)
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }

        public void MoveToArchive(string path)
        {
            string dir = Path.GetDirectoryName(path);
            string parentDir = Path.GetDirectoryName(dir);
            string archiveDir = Path.Combine(parentDir, "archive");

            if (!Directory.Exists(archiveDir))
            {
                Directory.CreateDirectory(archiveDir);
            }

            string destPath = Path.Combine(archiveDir, Path.GetFileName(path));

            if (File.Exists(destPath))
            {
                File.Delete(destPath);
            }

            File.Move(path, destPath);
        }
    }
}
