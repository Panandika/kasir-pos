#nullable enable
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Security.Cryptography;

namespace Kasir.Security
{
    /// <summary>
    /// Turns secret bytes into at-rest bytes and back. Used by <see cref="ProtectedFile"/>
    /// for every local credential file (Bantuan machine credentials, Bantuan refresh
    /// token, cloud sync Postgres credentials).
    /// </summary>
    public interface ISecretProtector
    {
        /// <summary>True when <see cref="Protect"/> actually encrypts (DPAPI).</summary>
        bool Encrypts { get; }

        byte[] Protect(byte[] plain);

        byte[] Unprotect(byte[] stored);
    }

    /// <summary>
    /// Windows DPAPI, <see cref="DataProtectionScope.CurrentUser"/>, no extra entropy.
    ///
    /// This is the convention every Kasir secret file has used since auth.dat
    /// (SupabaseMachineAuth) and machine-credentials.dat (MachineCredentialStore);
    /// keep it so files written by earlier versions still decrypt.
    ///
    /// Where the key lives: there is no key file in the app folder. DPAPI derives the
    /// key from the Windows user's master key (%APPDATA%\Microsoft\Protect\&lt;SID&gt;\),
    /// which Windows itself protects with the user's logon password. Consequences:
    ///   - only the same Windows user on the same PC can decrypt;
    ///   - copying the file / folder to another PC, or running the POS as another
    ///     Windows user, makes it unreadable;
    ///   - an administrator *resetting* a local account's password (not the user
    ///     changing it themselves) makes it unreadable.
    /// In all of those cases the app treats the credentials as missing and the owner
    /// re-enters them (cloud setup screen / re-pair).
    ///
    /// CurrentUser (not LocalMachine) on purpose: LocalMachine would let any process
    /// of any Windows account on the PC decrypt the file.
    /// No extra entropy on purpose: it adds nothing against a process already running
    /// as this user, and keeps the documented PowerShell read-out a one-liner.
    /// </summary>
    [SupportedOSPlatform("windows")]
    public sealed class DpapiCurrentUserProtector : ISecretProtector
    {
        public static readonly DpapiCurrentUserProtector Instance = new DpapiCurrentUserProtector();

        public bool Encrypts => true;

        public byte[] Protect(byte[] plain) =>
            ProtectedData.Protect(plain, null, DataProtectionScope.CurrentUser);

        public byte[] Unprotect(byte[] stored) =>
            ProtectedData.Unprotect(stored, null, DataProtectionScope.CurrentUser);
    }

    /// <summary>
    /// No encryption. Used on non-Windows dev machines (macOS/Linux) where DPAPI does
    /// not exist; <see cref="ProtectedFile"/> still restricts the file to mode 0600.
    /// </summary>
    public sealed class PlainSecretProtector : ISecretProtector
    {
        public static readonly PlainSecretProtector Instance = new PlainSecretProtector();

        public bool Encrypts => false;

        public byte[] Protect(byte[] plain) => (byte[])plain.Clone();

        public byte[] Unprotect(byte[] stored) => (byte[])stored.Clone();
    }

    public static class SecretProtectors
    {
        /// <summary>DPAPI CurrentUser on Windows, plain (0600 file) elsewhere.</summary>
        public static ISecretProtector PlatformDefault =>
            RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
                ? WindowsDpapi()
                : PlainSecretProtector.Instance;

#pragma warning disable CA1416 // only reached on Windows (checked above)
        private static ISecretProtector WindowsDpapi() => DpapiCurrentUserProtector.Instance;
#pragma warning restore CA1416
    }
}
