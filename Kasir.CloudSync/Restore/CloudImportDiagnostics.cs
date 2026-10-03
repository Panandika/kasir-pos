using System;
using System.IO;
using System.Text;
using System.Text.Json;

namespace Kasir.CloudSync.Restore
{
    // Error body returned by register-pair / snapshot-download, or by the Supabase
    // gateway in front of them. Our functions answer {"error":"code_expired"};
    // the gateway answers {"code":"UNAUTHORIZED_NO_AUTH_HEADER","message":"..."}
    // before our function even runs.
    public sealed class ServerError
    {
        public string Code { get; set; }
        public string Message { get; set; }

        public static ServerError Parse(string body)
        {
            var result = new ServerError { Code = "", Message = "" };
            if (string.IsNullOrWhiteSpace(body)) return result;
            try
            {
                using var doc = JsonDocument.Parse(body);
                var root = doc.RootElement;
                if (root.ValueKind != JsonValueKind.Object) return result;
                result.Code = FirstString(root, "error", "code", "error_code");
                result.Message = FirstString(root, "message", "detail", "msg", "error_description");
            }
            catch (JsonException)
            {
                // Not JSON (HTML error page, proxy text): keep a short excerpt as the message.
                result.Message = body.Length > 200 ? body.Substring(0, 200) : body;
            }
            return result;
        }

        private static string FirstString(JsonElement root, params string[] names)
        {
            foreach (var name in names)
            {
                if (root.TryGetProperty(name, out var el))
                {
                    if (el.ValueKind == JsonValueKind.String) return el.GetString() ?? "";
                    if (el.ValueKind == JsonValueKind.Number) return el.GetRawText();
                }
            }
            return "";
        }
    }

    // Plain-language explanation shown to the operator: what happened, the likely
    // cause, what to do, plus the technical detail support needs.
    public sealed class CloudImportExplanation
    {
        public string Title { get; set; }
        public string Cause { get; set; }
        public string Action { get; set; }
        public string Technical { get; set; }

        public override string ToString()
        {
            var sb = new StringBuilder();
            sb.AppendLine(Title);
            if (!string.IsNullOrEmpty(Cause)) sb.AppendLine("Penyebab: " + Cause);
            if (!string.IsNullOrEmpty(Action)) sb.AppendLine("Yang harus dilakukan: " + Action);
            if (!string.IsNullOrEmpty(Technical)) sb.Append("Detail teknis: " + Technical);
            return sb.ToString().TrimEnd();
        }
    }

    public static class CloudImportErrors
    {
        private const string AskAdmin = "Hubungi admin dan kirimkan detail teknis di bawah serta file log.";

        public static CloudImportExplanation ForPair(int? httpStatus, string serverCode, string serverMessage)
        {
            string code = serverCode ?? "";
            var e = new CloudImportExplanation { Technical = Technical("register-pair", httpStatus, code, serverMessage) };

            switch (code)
            {
                case "code_not_found":
                    e.Title = "Kode pairing tidak dikenal.";
                    e.Cause = "Kode salah ketik, atau kode dibuat untuk toko lain.";
                    e.Action = "Periksa 6 digit di dashboard, lalu ketik ulang. Jika masih gagal, klik Buat kode lagi.";
                    return e;
                case "code_expired":
                    e.Title = "Kode pairing sudah kedaluwarsa.";
                    e.Cause = "Kode hanya berlaku 10 menit sejak dibuat.";
                    e.Action = "Di dashboard klik Buat kode lagi, lalu segera ketik kode baru di sini.";
                    return e;
                case "code_already_used":
                    e.Title = "Kode pairing sudah pernah dipakai.";
                    e.Cause = "Setiap kode hanya bisa dipakai satu kali.";
                    e.Action = "Di dashboard klik Buat kode lagi untuk mendapatkan kode baru.";
                    return e;
                case "code_format_invalid":
                    e.Title = "Format kode tidak valid.";
                    e.Cause = "Kode harus tepat 6 angka.";
                    e.Action = "Ketik ulang 6 angka dari dashboard tanpa spasi atau huruf.";
                    return e;
                case "rate_limited":
                    e.Title = "Terlalu banyak percobaan pairing.";
                    e.Cause = "Server membatasi percobaan untuk keamanan" +
                              (string.IsNullOrEmpty(serverMessage) ? "." : " (" + serverMessage + ").");
                    e.Action = "Tunggu beberapa menit lalu coba lagi dengan kode baru.";
                    return e;
                case "bypass_disabled":
                case "bypass_auto_disabled":
                    e.Title = "Kode darurat (bypass) sedang dinonaktifkan.";
                    e.Cause = "Kode bypass dimatikan oleh admin atau otomatis karena dipakai terlalu sering.";
                    e.Action = "Gunakan kode biasa dari dashboard (Daftarkan register baru → Buat kode). " + AskAdmin;
                    return e;
                case "fingerprint_required":
                case "invalid_json":
                case "method_not_allowed":
                    e.Title = "Permintaan pairing ditolak server.";
                    e.Cause = "Versi aplikasi POS dan server tidak cocok.";
                    e.Action = "Perbarui aplikasi POS ke versi terbaru. " + AskAdmin;
                    return e;
                case "retries_exhausted":
                    e.Title = "Tidak bisa menghubungi server pairing.";
                    e.Cause = "Koneksi internet terputus, atau server sedang bermasalah (sudah dicoba 3 kali).";
                    e.Action = "Periksa internet PC ini (coba buka situs web di browser), lalu coba lagi.";
                    return e;
            }

            if (IsGatewayAuthError(code) || httpStatus == 401)
            {
                e.Title = "Server pairing belum dikonfigurasi dengan benar.";
                e.Cause = "Fungsi register-pair di server menolak permintaan tanpa login (pengaturan \"verify JWT\" masih aktif), sehingga kode belum diperiksa sama sekali.";
                e.Action = "Ini bukan kesalahan kode Anda. " + AskAdmin;
                return e;
            }
            if (httpStatus == 404 || code == "NOT_FOUND")
            {
                e.Title = "Layanan pairing tidak ditemukan di server.";
                e.Cause = "Fungsi register-pair belum di-deploy ke Supabase.";
                e.Action = AskAdmin;
                return e;
            }
            if (httpStatus >= 500)
            {
                e.Title = "Server pairing sedang bermasalah.";
                e.Cause = "Server mengembalikan error internal.";
                e.Action = "Coba lagi beberapa menit lagi. " + AskAdmin;
                return e;
            }

            e.Title = "Pairing gagal karena alasan yang tidak dikenali.";
            e.Cause = string.IsNullOrEmpty(serverMessage) ? "Server tidak memberi keterangan." : serverMessage;
            e.Action = AskAdmin;
            return e;
        }

        public static CloudImportExplanation ForRestore(string stage, int? httpStatus, string serverCode, string message)
        {
            string code = serverCode ?? "";
            var e = new CloudImportExplanation
            {
                Technical = Technical("snapshot-download/" + (stage ?? "?"), httpStatus, code, message),
            };

            switch (code)
            {
                case "no_snapshot_available":
                    e.Title = "Belum ada snapshot data di cloud.";
                    e.Cause = "Data toko belum pernah dikemas (snapshot) untuk diunduh register baru.";
                    e.Action = "Di dashboard (Pemilik → Register) klik Bangun snapshot sekarang, tunggu sampai selesai, lalu buat kode baru dan coba lagi.";
                    return e;
                case "schema_version_unsupported":
                    e.Title = "Versi data di cloud tidak cocok dengan aplikasi ini.";
                    e.Cause = "Snapshot dibuat untuk versi aplikasi yang berbeda.";
                    e.Action = "Perbarui aplikasi POS ke versi terbaru, atau minta admin membangun ulang snapshot.";
                    return e;
                case "unauthorized":
                    e.Title = "Izin unduh snapshot ditolak.";
                    e.Cause = "Token pairing tidak valid atau sudah lewat 15 menit.";
                    e.Action = "Mulai ulang: buat kode baru di dashboard lalu ketik di sini.";
                    return e;
                case "lookup_failed":
                case "signing_failed":
                    e.Title = "Server gagal menyiapkan file snapshot.";
                    e.Cause = "Error di sisi server saat mencari atau menandatangani file.";
                    e.Action = "Coba lagi beberapa menit lagi. " + AskAdmin;
                    return e;
            }

            if (IsGatewayAuthError(code))
            {
                e.Title = "Server unduhan belum dikonfigurasi dengan benar.";
                e.Cause = "Fungsi snapshot-download menolak token pairing di gateway Supabase (pengaturan \"verify JWT\" masih aktif).";
                e.Action = AskAdmin;
                return e;
            }
            if (httpStatus == 404)
            {
                e.Title = "Layanan unduh snapshot tidak ditemukan.";
                e.Cause = "Fungsi snapshot-download belum di-deploy, atau file snapshot sudah dihapus.";
                e.Action = AskAdmin;
                return e;
            }

            switch (stage)
            {
                case "verifying":
                    e.Title = "File yang diunduh rusak atau tidak lengkap.";
                    e.Cause = "Pemeriksaan keutuhan file (SHA-256 / integritas database) gagal, biasanya karena koneksi terputus saat mengunduh.";
                    e.Action = "Coba lagi dengan kode baru. Jika berulang, minta admin membangun ulang snapshot.";
                    return e;
                case "downloading":
                    e.Title = "Unduhan snapshot gagal.";
                    e.Cause = "Koneksi terputus atau terlalu lambat saat mengunduh data (sekitar 40 MB).";
                    e.Action = "Pastikan internet stabil, lalu coba lagi dengan kode baru.";
                    return e;
                case "manifest" when message != null && message.StartsWith("Insufficient disk space", StringComparison.Ordinal):
                case "decompressing" when message != null && message.StartsWith("Insufficient disk space", StringComparison.Ordinal):
                    e.Title = "Ruang disk tidak cukup.";
                    e.Cause = "Data diunduh dalam bentuk terkompresi lalu dibuka; butuh ruang kosong sekitar 300 MB di drive aplikasi POS.";
                    e.Action = "Kosongkan ruang di drive aplikasi POS, lalu coba lagi.";
                    return e;
                case "decompressing":
                    e.Title = "File snapshot tidak bisa dibuka.";
                    e.Cause = "Data terkompresi yang diunduh rusak atau tidak lengkap, walaupun lolos pemeriksaan unduhan.";
                    e.Action = "Coba lagi dengan kode baru. Jika berulang, minta admin membangun ulang snapshot (Bangun snapshot sekarang).";
                    return e;
                case "manifest" when message != null && message.StartsWith("Unsupported snapshot encoding", StringComparison.Ordinal):
                    e.Title = "Format snapshot tidak dikenal aplikasi ini.";
                    e.Cause = "Server mengirim snapshot dengan format kompresi yang lebih baru.";
                    e.Action = "Perbarui aplikasi POS ke versi terbaru, lalu coba lagi.";
                    return e;
            }

            e.Title = "Mengunduh data toko gagal.";
            e.Cause = string.IsNullOrEmpty(message) ? "Tidak ada keterangan dari server." : message;
            e.Action = AskAdmin;
            return e;
        }

        // isTimeout: HttpClient's 30s timeout fired (TaskCanceledException without the
        // user pressing Batal).
        public static CloudImportExplanation ForUnexpected(Exception ex, bool isTimeout = false)
        {
            bool timeout = isTimeout || ex is TimeoutException;
            return new CloudImportExplanation
            {
                Title = timeout ? "Server tidak menjawab (timeout)." : "Terjadi kesalahan tak terduga.",
                Cause = timeout
                    ? "Koneksi internet lambat atau server tidak merespons dalam 30 detik."
                    : ex.Message,
                Action = timeout ? "Periksa internet lalu coba lagi." : AskAdmin,
                Technical = ex.GetType().Name + ": " + ex.Message,
            };
        }

        public static bool IsGatewayAuthError(string code)
        {
            return !string.IsNullOrEmpty(code) && code.StartsWith("UNAUTHORIZED_", StringComparison.Ordinal);
        }

        private static string Technical(string endpoint, int? status, string code, string message)
        {
            var parts = new StringBuilder(endpoint);
            parts.Append(" · HTTP ").Append(status.HasValue ? status.Value.ToString() : "-");
            if (!string.IsNullOrEmpty(code)) parts.Append(" · ").Append(code);
            if (!string.IsNullOrEmpty(message))
            {
                string m = message.Length > 160 ? message.Substring(0, 160) + "…" : message;
                parts.Append(" · ").Append(m);
            }
            return parts.ToString();
        }
    }

    // Append-only diagnostics log for cloud-import attempts. One line per HTTP call
    // or failure. Never records the pair code or any JWT.
    public sealed class CloudImportLog
    {
        public const int MaxBodyChars = 2048;
        private static readonly object Gate = new object();

        public string FilePath { get; }

        public CloudImportLog(string filePath)
        {
            FilePath = filePath ?? throw new ArgumentNullException(nameof(filePath));
        }

        public void Write(string step, string endpoint, int? httpStatus, string body,
            Exception ex, string deviceFingerprint, string note = null)
        {
            try
            {
                var sb = new StringBuilder();
                sb.Append(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff zzz"));
                sb.Append(" | step=").Append(step);
                if (!string.IsNullOrEmpty(endpoint)) sb.Append(" | endpoint=").Append(StripQuery(endpoint));
                sb.Append(" | http=").Append(httpStatus.HasValue ? httpStatus.Value.ToString() : "-");
                if (!string.IsNullOrEmpty(deviceFingerprint)) sb.Append(" | device=").Append(deviceFingerprint);
                if (!string.IsNullOrEmpty(note)) sb.Append(" | note=").Append(OneLine(note));
                if (ex != null) sb.Append(" | exception=").Append(ex.GetType().Name).Append(": ").Append(OneLine(ex.Message));
                if (!string.IsNullOrEmpty(body)) sb.Append(" | body=").Append(OneLine(Truncate(Redact(body))));

                lock (Gate)
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(FilePath)) ?? ".");
                    File.AppendAllText(FilePath, sb.ToString() + Environment.NewLine, Encoding.UTF8);
                }
            }
            catch
            {
                // Logging must never break the import flow.
            }
        }

        // Signed URLs carry a token in the query string; log only scheme/host/path.
        internal static string StripQuery(string url)
        {
            int q = url.IndexOf('?');
            return q >= 0 ? url.Substring(0, q) : url;
        }

        // Defensive: a success body contains the bootstrap JWT ("jwt":"eyJ...") and a
        // signed URL. Bodies are only logged on failure, but redact anyway.
        internal static string Redact(string body)
        {
            if (string.IsNullOrEmpty(body)) return body;
            string redacted = System.Text.RegularExpressions.Regex.Replace(
                body, "eyJ[A-Za-z0-9_\\-]+\\.[A-Za-z0-9_\\-]+\\.[A-Za-z0-9_\\-]+", "[jwt]");
            redacted = System.Text.RegularExpressions.Regex.Replace(
                redacted, "(token=)[^&\"\\s]+", "$1[redacted]");
            return redacted;
        }

        private static string Truncate(string s)
        {
            return s.Length > MaxBodyChars ? s.Substring(0, MaxBodyChars) + "…[truncated]" : s;
        }

        private static string OneLine(string s)
        {
            return s.Replace("\r", " ").Replace("\n", " ");
        }
    }
}
