namespace Kasir.Sync
{
    public interface ISyncFileReader
    {
        string[] ListFiles(string directory, string pattern);
        string Read(string path);
        void Delete(string path);
        void MoveToArchive(string path);
        // Move a permanently-bad (unparseable / bad-signature / invalid) file out of the
        // inbox so it cannot re-consume the inbox window on every pull (F44).
        void MoveToQuarantine(string path);
    }
}
