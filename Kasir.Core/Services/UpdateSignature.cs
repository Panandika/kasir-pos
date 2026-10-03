using System;
using System.Security.Cryptography;

namespace Kasir.Services
{
    /// <summary>
    /// Verifies the release signature that proves an update package was built by
    /// this repository's GitHub release workflow.
    ///
    /// release.yml signs the exact bytes of checksum.sha256 with an ECDSA P-256 /
    /// SHA-256 private key (GitHub secret UPDATE_SIGNING_KEY) and writes the
    /// signature, base64-encoded in IEEE P1363 format (r||s, 64 bytes — the .NET
    /// SignData/VerifyData default), to checksum.sha256.sig. Only the PUBLIC key is
    /// shipped in the app, so nothing on a register can forge an update.
    /// </summary>
    public static class UpdateSignature
    {
        public const string ChecksumFileName = "checksum.sha256";
        public const string SignatureFileName = "checksum.sha256.sig";

        // Public half of the release signing key (private half: GitHub secret
        // UPDATE_SIGNING_KEY on Panandika/kasir-pos). To rotate: generate a new
        // P-256 key pair, replace this constant and the secret in the same release.
        public const string PublicKeyPem =
            "-----BEGIN PUBLIC KEY-----\n" +
            "MFkwEwYHKoZIzj0CAQYIKoZIzj0DAQcDQgAErnNwzxNtgaKJh9t0iBMxFkWwvrdB\n" +
            "9ZbhjtmLzjHvTSr6PVzrUBSGCapq9kXgj3vP8kI6jq7ieC/sGWrutNekug==\n" +
            "-----END PUBLIC KEY-----";

        /// <summary>
        /// True only when <paramref name="signatureBase64"/> is a valid ECDSA
        /// P-256/SHA-256 signature of <paramref name="data"/> under
        /// <paramref name="publicKeyPem"/>. Never throws.
        /// </summary>
        public static bool Verify(byte[] data, string signatureBase64, string publicKeyPem = PublicKeyPem)
        {
            if (data == null || string.IsNullOrWhiteSpace(signatureBase64) || string.IsNullOrWhiteSpace(publicKeyPem))
                return false;
            try
            {
                byte[] signature = Convert.FromBase64String(signatureBase64.Trim());
                using (var ecdsa = ECDsa.Create())
                {
                    ecdsa.ImportFromPem(publicKeyPem);
                    return ecdsa.VerifyData(data, signature, HashAlgorithmName.SHA256);
                }
            }
            catch (FormatException)
            {
                return false;
            }
            catch (CryptographicException)
            {
                return false;
            }
            catch (ArgumentException)
            {
                return false;
            }
        }
    }
}
