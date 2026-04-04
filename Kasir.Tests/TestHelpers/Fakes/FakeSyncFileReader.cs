using System.Collections.Generic;
using System.Linq;
using Kasir.Sync;

namespace Kasir.Tests.TestHelpers.Fakes
{
    public class FakeSyncFileReader : ISyncFileReader
    {
        public Dictionary<string, string> Files { get; private set; }
        public List<string> ArchivedFiles { get; private set; }

        public FakeSyncFileReader()
        {
            Files = new Dictionary<string, string>();
            ArchivedFiles = new List<string>();
        }

        public string[] ListFiles(string directory, string pattern)
        {
            // Normalize: ensure directory ends with separator for prefix matching
            string prefix = directory.TrimEnd('\\', '/');
            return Files.Keys
                .Where(f => f.StartsWith(prefix + "\\") || f.StartsWith(prefix + "/"))
                .OrderBy(f => f)
                .ToArray();
        }

        public string Read(string path)
        {
            string content;
            return Files.TryGetValue(path, out content) ? content : null;
        }

        public void Delete(string path)
        {
            Files.Remove(path);
        }

        public void MoveToArchive(string path)
        {
            ArchivedFiles.Add(path);
            Files.Remove(path);
        }
    }
}
