using System.Collections.Generic;
using Kasir.Sync;

namespace Kasir.Tests.TestHelpers.Fakes
{
    public class FakeSyncFileWriter : ISyncFileWriter
    {
        public Dictionary<string, string> Files { get; private set; }
        public List<string> MovedFiles { get; private set; }

        public FakeSyncFileWriter()
        {
            Files = new Dictionary<string, string>();
            MovedFiles = new List<string>();
        }

        public void Write(string path, string content)
        {
            Files[path] = content;
        }

        public void SafeMove(string tempPath, string destPath)
        {
            if (Files.ContainsKey(tempPath))
            {
                string content = Files[tempPath];
                Files.Remove(tempPath);
                Files[destPath] = content;
            }
            MovedFiles.Add(destPath);
        }
    }
}
