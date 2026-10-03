namespace Kasir.Services
{
    public static class UpdateMessages
    {
        public const string Checking = "Memeriksa update di GitHub...";
        public const string Available = "Update tersedia: v{0}";
        public const string UpToDate = "Sudah versi terbaru";
        public const string Offline = "Tidak bisa terhubung ke GitHub \u2014 periksa koneksi internet PC ini";
        public const string Timeout = "GitHub tidak merespons (waktu habis) \u2014 coba lagi nanti";
        public const string NoRelease = "Belum ada rilis aplikasi di GitHub";
        public const string RateLimited = "Batas akses GitHub tercapai \u2014 coba lagi sekitar 1 jam lagi";
        public const string ServerError = "Server GitHub mengembalikan kesalahan (HTTP {0}) \u2014 coba lagi nanti";
        public const string BadResponse = "Jawaban GitHub tidak bisa dibaca \u2014 coba lagi nanti";
        public const string NoAsset = "Rilis v{0} tidak berisi file aplikasi untuk register ini";
        public const string DownloadFailed = "Gagal mengunduh update";
        public const string Downloading = "Mengunduh update... {0}%";
        public const string Verifying = "Memeriksa tanda tangan dan isi file...";
        public const string SignatureFailed = "Tanda tangan rilis tidak valid \u2014 update dibatalkan (file bukan dari rilis resmi atau rusak)";
        public const string ChecksumFailed = "Isi file update tidak cocok dengan daftar yang ditandatangani \u2014 update dibatalkan";
        public const string VersionMismatch = "Versi paket ({0}) berbeda dari rilis ({1}) \u2014 update dibatalkan";
        public const string ZipInvalid = "File ZIP update rusak \u2014 update dibatalkan";
        public const string InsufficientDisk = "Ruang disk tidak cukup ({0}MB dibutuhkan, {1}MB tersedia)";
        public const string NothingToInstall = "Tidak ada update untuk dipasang \u2014 periksa dulu (F5)";
        public const string Cancelled = "Update dibatalkan";
        public const string PrepareFailed = "Gagal menyiapkan update";
        public const string StagedExeMissing = "File {0} tidak ditemukan di paket update \u2014 update dibatalkan";
        public const string InProgress = "Memasang update... aplikasi akan ditutup dan dibuka lagi otomatis";
        public const string RolledBack = "Update gagal, dikembalikan ke versi sebelumnya";
        public const string Success = "Update berhasil ke v{0}";
        public const string Confirm = "Update ke versi {0}?\nAplikasi akan ditutup dan dibuka lagi otomatis. Data penjualan tidak berubah.";
        public const string CurrentVersion = "Versi saat ini: {0}";
        public const string WalCheckpointFailed = "Gagal checkpoint database \u2014 update dibatalkan";
        public const string Preparing = "Menyiapkan update...";
    }
}
