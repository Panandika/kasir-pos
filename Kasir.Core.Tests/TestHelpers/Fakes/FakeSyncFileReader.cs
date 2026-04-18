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

        private static string Normalize(string path)
        {
            return path == null ? null : path.Replace('\\', '/').TrimEnd('/');
        }

        public string[] ListFiles(string directory, string pattern)
        {
            // Normalize separators so tests using Windows-style paths work on macOS/Linux,
            // where Path.Combine uses '/' and mixes separators.
            string prefix = Normalize(directory);
            return Files.Keys
                .Where(f => Normalize(f).StartsWith(prefix + "/"))
                .OrderBy(f => f)
                .ToArray();
        }

        public string Read(string path)
        {
            string content;
            if (Files.TryGetValue(path, out content)) return content;
            // Fallback: case/separator-insensitive lookup
            string target = Normalize(path);
            foreach (var kvp in Files)
            {
                if (Normalize(kvp.Key) == target) return kvp.Value;
            }
            return null;
        }

        public void Delete(string path)
        {
            if (!Files.Remove(path))
            {
                string target = Normalize(path);
                string key = null;
                foreach (var k in Files.Keys)
                {
                    if (Normalize(k) == target) { key = k; break; }
                }
                if (key != null) Files.Remove(key);
            }
        }

        public void MoveToArchive(string path)
        {
            ArchivedFiles.Add(path);
            Delete(path);
        }
    }
}
