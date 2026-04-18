using System.IO;

namespace Kasir.Sync
{
    public class SyncFileWriter : ISyncFileWriter
    {
        public void Write(string path, string content)
        {
            string dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }

            File.WriteAllText(path, content);
        }

        public void SafeMove(string tempPath, string destPath)
        {
            // Safe write pattern: temp → delete dest → move temp to dest
            // File.Move(src, dest, overwrite) does NOT exist on .NET Framework 4.8
            if (File.Exists(destPath))
            {
                File.Delete(destPath);
            }

            File.Move(tempPath, destPath);
        }
    }
}
