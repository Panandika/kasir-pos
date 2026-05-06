using System;
using System.IO;
using System.Text.Json;

namespace Kasir.Avalonia.Infrastructure;

public sealed class CloudSyncCreds
{
    public string Host { get; set; } = "";
    public int Port { get; set; } = 6543;
    public string Database { get; set; } = "postgres";
    public string Username { get; set; } = "";
    public string Password { get; set; } = "";
}

public static class CloudSyncCredsService
{
    private static readonly string ConfigDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Kasir");
    public static readonly string ConfigPath = Path.Combine(ConfigDir, "cloudsync.json");

    public static CloudSyncCreds? Load()
    {
        try
        {
            if (!File.Exists(ConfigPath)) return null;
            return JsonSerializer.Deserialize<CloudSyncCreds>(File.ReadAllText(ConfigPath));
        }
        catch { return null; }
    }

    public static bool Save(CloudSyncCreds creds)
    {
        try
        {
            Directory.CreateDirectory(ConfigDir);
            File.WriteAllText(ConfigPath, JsonSerializer.Serialize(creds, new JsonSerializerOptions { WriteIndented = true }));
            return true;
        }
        catch { return false; }
    }

    public static void Delete()
    {
        try { if (File.Exists(ConfigPath)) File.Delete(ConfigPath); } catch { }
    }

    public static string BuildConnectionString(CloudSyncCreds creds)
        => $"Host={creds.Host};Port={creds.Port};Database={creds.Database};Username={creds.Username};Password={creds.Password};SslMode=Require";
}
