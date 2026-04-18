namespace Kasir.Sync
{
    public interface ISyncFileReader
    {
        string[] ListFiles(string directory, string pattern);
        string Read(string path);
        void Delete(string path);
        void MoveToArchive(string path);
    }
}
