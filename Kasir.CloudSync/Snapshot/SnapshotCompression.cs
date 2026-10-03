using System;
using System.Buffers;
using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;

namespace Kasir.CloudSync.Snapshot
{
    // Brotli encoding of cloud-import snapshots. The raw SQLite snapshot is ~189 MB
    // (876K rows, 2026-10); Supabase free-plan Storage caps a single object at 50 MB.
    // Brotli quality 9 / window 24 brings it to ~37 MB. The publisher compresses; the
    // POS restorer decompresses after verifying the SHA-256 of the downloaded bytes.
    //
    // Encoding is signalled by the Storage object name: "snapshot-{id}.db.br".
    // snapshot-download derives "encoding":"br" from that suffix, so no schema change.
    public static class SnapshotCompression
    {
        public const string BrotliEncoding = "br";
        public const string BrotliSuffix = ".br";
        public const int DefaultQuality = 9;   // 0-11; 9 ≈ best size/time trade-off for SQLite pages
        public const int DefaultWindow = 24;   // log2 window, 10-24; 24 = 16 MB, the maximum

        public sealed class CompressResult
        {
            public string Path;
            public long SizeBytes;
            public string Sha256;
        }

        // Compresses src into dst with the given Brotli quality/window and returns the
        // size + lowercase-hex SHA-256 of the compressed bytes (what clients download).
        public static CompressResult CompressFile(string src, string dst,
            int quality = DefaultQuality, int window = DefaultWindow)
        {
            if (quality < 0 || quality > 11) throw new ArgumentOutOfRangeException(nameof(quality));
            if (window < 10 || window > 24) throw new ArgumentOutOfRangeException(nameof(window));

            using var encoder = new BrotliEncoder(quality, window);
            using var input = new FileStream(src, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 16);
            using var output = new FileStream(dst, FileMode.Create, FileAccess.Write, FileShare.None, 1 << 16);
            using var sha = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);

            byte[] inBuf = ArrayPool<byte>.Shared.Rent(1 << 16);
            byte[] outBuf = ArrayPool<byte>.Shared.Rent(1 << 17);
            long written = 0;
            try
            {
                int read;
                while ((read = input.Read(inBuf, 0, inBuf.Length)) > 0)
                {
                    ReadOnlySpan<byte> source = inBuf.AsSpan(0, read);
                    while (!source.IsEmpty)
                    {
                        var status = encoder.Compress(source, outBuf, out int consumed, out int produced, isFinalBlock: false);
                        if (status == OperationStatus.InvalidData) throw new InvalidDataException("brotli encoder rejected input");
                        Write(output, sha, outBuf, produced, ref written);
                        source = source.Slice(consumed);
                    }
                }

                // Finish the stream: flush remaining state until the encoder reports Done.
                OperationStatus final;
                do
                {
                    final = encoder.Compress(ReadOnlySpan<byte>.Empty, outBuf, out _, out int produced, isFinalBlock: true);
                    if (final == OperationStatus.InvalidData) throw new InvalidDataException("brotli encoder failed to finish");
                    Write(output, sha, outBuf, produced, ref written);
                } while (final == OperationStatus.DestinationTooSmall);
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(inBuf);
                ArrayPool<byte>.Shared.Return(outBuf);
            }

            output.Flush(true);
            return new CompressResult
            {
                Path = dst,
                SizeBytes = written,
                Sha256 = Convert.ToHexString(sha.GetHashAndReset()).ToLowerInvariant(),
            };
        }

        private static void Write(Stream output, IncrementalHash sha, byte[] buf, int count, ref long written)
        {
            if (count <= 0) return;
            output.Write(buf, 0, count);
            sha.AppendData(buf, 0, count);
            written += count;
        }

        // Streams a Brotli file back to the original bytes. Throws InvalidDataException
        // on corrupt / truncated input and IOException when the disk fills up.
        public static async Task<long> DecompressFileAsync(string src, string dst, CancellationToken ct)
        {
            await using var input = new FileStream(src, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 16, useAsync: true);
            await using var brotli = new BrotliStream(input, CompressionMode.Decompress);
            await using var output = new FileStream(dst, FileMode.Create, FileAccess.Write, FileShare.None, 1 << 16, useAsync: true);
            await brotli.CopyToAsync(output, 1 << 16, ct).ConfigureAwait(false);
            await output.FlushAsync(ct).ConfigureAwait(false);
            return output.Length;
        }

        // Encoding from a Storage object path / name ("snapshots/snapshot-x.db.br" -> "br").
        public static string EncodingFromPath(string path) =>
            path != null && path.EndsWith(".db" + BrotliSuffix, StringComparison.OrdinalIgnoreCase)
                ? BrotliEncoding
                : null;

        public static bool IsBrotli(string encoding) =>
            string.Equals(encoding, BrotliEncoding, StringComparison.OrdinalIgnoreCase);

        public static bool IsPlain(string encoding) =>
            string.IsNullOrEmpty(encoding) || string.Equals(encoding, "identity", StringComparison.OrdinalIgnoreCase);
    }
}
