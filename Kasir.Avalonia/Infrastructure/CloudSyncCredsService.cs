using Kasir.Security;

namespace Kasir.Avalonia.Infrastructure;

/// <summary>
/// POS-side facade over <see cref="CloudSyncCredentialStore"/> (Kasir.Core), which
/// keeps the cloud sync credentials DPAPI-encrypted at
/// %LOCALAPPDATA%\Kasir\cloudsync.dat and migrates the old plaintext
/// cloudsync.json on first load. The Kasir.CloudSync worker reads the same store.
/// </summary>
public static class CloudSyncCredsService
{
    public static string ConfigPath => CloudSyncCredentialStore.FilePath;

    public static bool IsEncrypted => CloudSyncCredentialStore.IsEncrypted;

    public static CloudSyncCreds? Load() => CloudSyncCredentialStore.TryLoad();

    public static bool Save(CloudSyncCreds creds) => CloudSyncCredentialStore.TrySave(creds);

    public static void Delete() => CloudSyncCredentialStore.TryDelete();

    public static string BuildConnectionString(CloudSyncCreds creds)
        => $"Host={creds.Host};Port={creds.Port};Database={creds.Database};Username={creds.Username};Password={creds.Password};SslMode=Require";
}
