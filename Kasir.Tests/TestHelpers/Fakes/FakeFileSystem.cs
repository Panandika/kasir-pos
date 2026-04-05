using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Kasir.Utils;

namespace Kasir.Tests.TestHelpers.Fakes
{
    public class FakeFileSystem : IFileSystem
    {
        private readonly Dictionary<string, string> _files = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> _directories = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private long _diskSpace = long.MaxValue;

        public void AddFile(string path, string content)
        {
            string normalized = NormalizePath(path);
            _files[normalized] = content;
            // Auto-create parent directory using normalized path
            string dir = GetParentPath(normalized);
            if (!string.IsNullOrEmpty(dir))
            {
                _directories.Add(dir);
            }
        }

        public void AddDirectory(string path)
        {
            _directories.Add(NormalizePath(path));
        }

        public void SetDiskSpace(long bytes)
        {
            _diskSpace = bytes;
        }

        public bool DirectoryExists(string path)
        {
            return _directories.Contains(NormalizePath(path));
        }

        public bool FileExists(string path)
        {
            return _files.ContainsKey(NormalizePath(path));
        }

        public string ReadAllText(string path)
        {
            string key = NormalizePath(path);
            if (_files.ContainsKey(key))
            {
                return _files[key];
            }
            throw new FileNotFoundException("File not found: " + path);
        }

        public string[] GetFiles(string path, string pattern, bool recurse)
        {
            string normalDir = NormalizePath(path);
            return _files.Keys
                .Where(f => recurse
                    ? f.StartsWith(normalDir + "\\", StringComparison.OrdinalIgnoreCase)
                    : GetParentPath(f).Equals(normalDir, StringComparison.OrdinalIgnoreCase))
                .ToArray();
        }

        public string[] GetDirectories(string path)
        {
            string normalDir = NormalizePath(path);
            return _directories
                .Where(d => !d.Equals(normalDir, StringComparison.OrdinalIgnoreCase) &&
                            GetParentPath(d).Equals(normalDir, StringComparison.OrdinalIgnoreCase))
                .ToArray();
        }

        private static string GetParentPath(string normalizedPath)
        {
            int idx = normalizedPath.LastIndexOf('\\');
            return idx < 0 ? "" : normalizedPath.Substring(0, idx);
        }

        public void CopyFile(string source, string dest, bool overwrite)
        {
            string key = NormalizePath(source);
            if (!_files.ContainsKey(key))
            {
                throw new FileNotFoundException("Source not found: " + source);
            }
            _files[NormalizePath(dest)] = _files[key];
        }

        public void CreateDirectory(string path)
        {
            _directories.Add(NormalizePath(path));
        }

        public void DeleteDirectory(string path, bool recursive)
        {
            string normalDir = NormalizePath(path);
            _directories.Remove(normalDir);
            if (recursive)
            {
                var toRemove = _files.Keys
                    .Where(f => f.StartsWith(normalDir + "\\", StringComparison.OrdinalIgnoreCase))
                    .ToList();
                foreach (var key in toRemove)
                {
                    _files.Remove(key);
                }
                var dirsToRemove = _directories
                    .Where(d => d.StartsWith(normalDir + "\\", StringComparison.OrdinalIgnoreCase))
                    .ToList();
                foreach (var d in dirsToRemove)
                {
                    _directories.Remove(d);
                }
            }
        }

        public long GetAvailableDiskSpace(string path)
        {
            return _diskSpace;
        }

        public long GetFileSize(string path)
        {
            string key = NormalizePath(path);
            if (_files.ContainsKey(key))
            {
                return _files[key].Length;
            }
            return 0;
        }

        private static string NormalizePath(string path)
        {
            return path.Replace("/", "\\").TrimEnd('\\');
        }
    }
}
