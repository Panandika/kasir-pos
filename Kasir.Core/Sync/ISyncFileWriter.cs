namespace Kasir.Sync
{
    public interface ISyncFileWriter
    {
        void Write(string path, string content);
        void SafeMove(string tempPath, string destPath);
    }
}
