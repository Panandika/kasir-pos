namespace Kasir.Services
{
    public static class UpdateMessages
    {
        public const string Checking = "Sedang memeriksa...";
        public const string Available = "Update tersedia: v{0}";
        public const string UpToDate = "Sudah versi terbaru";
        public const string Unreachable = "Tidak dapat terhubung ke server update";
        public const string ChecksumFailed = "Checksum tidak valid \u2014 update dibatalkan";
        public const string HmacFailed = "Tanda tangan HMAC tidak valid \u2014 update dibatalkan";
        public const string InsufficientDisk = "Ruang disk tidak cukup ({0}MB dibutuhkan, {1}MB tersedia)";
        public const string InProgress = "Update sedang berlangsung...";
        public const string RolledBack = "Update gagal, dikembalikan ke versi sebelumnya";
        public const string Success = "Update berhasil ke v{0}";
        public const string Confirm = "Update ke versi {0}?\nAplikasi akan ditutup dan restart.";
        public const string CurrentVersion = "Versi saat ini: {0}";
        public const string ZipImported = "Import ZIP berhasil";
        public const string ZipInvalid = "File ZIP tidak valid atau rusak";
        public const string WalCheckpointFailed = "Gagal checkpoint database \u2014 update dibatalkan";
        public const string DowngradeBlocked = "Versi {0} lebih lama dari versi saat ini \u2014 update dibatalkan";
        public const string Preparing = "Menyiapkan update...";
        public const string CopyingFiles = "Menyalin file update...";
    }
}
