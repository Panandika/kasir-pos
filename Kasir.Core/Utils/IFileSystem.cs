namespace Kasir.Utils
{
    public interface IFileSystem
    {
        bool DirectoryExists(string path);
        bool FileExists(string path);
        string ReadAllText(string path);
        string[] GetFiles(string path, string pattern, bool recurse);
        string[] GetDirectories(string path);
        void CopyFile(string source, string dest, bool overwrite);
        void CreateDirectory(string path);
        void DeleteDirectory(string path, bool recursive);
        long GetAvailableDiskSpace(string path);
        long GetFileSize(string path);
    }
}
