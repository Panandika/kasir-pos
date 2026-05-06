using System;
using System.IO;
using System.Text.Json;
using Avalonia;
using Avalonia.Styling;

namespace Kasir.Avalonia.Infrastructure;

/// <summary>
/// Singleton service that owns runtime theme variant (Dark/Light).
/// Persists user preference to LocalApplicationData/Kasir/theme.json.
/// </summary>
public sealed class ThemeService
{
    private static readonly Lazy<ThemeService> _instance = new(() => new ThemeService());
    public static ThemeService Current => _instance.Value;

    private static readonly string ConfigDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Kasir");
    private static readonly string ConfigPath = Path.Combine(ConfigDir, "theme.json");

    public ThemeVariant ActiveVariant { get; private set; } = ThemeVariant.Dark;

    public event EventHandler<ThemeVariant>? ThemeChanged;

    private ThemeService() { }

    public void LoadAndApplyAtStartup()
    {
        ActiveVariant = LoadFromDisk();
        ApplyToApplication();
    }

    public void Toggle()
    {
        ActiveVariant = ActiveVariant == ThemeVariant.Dark ? ThemeVariant.Light : ThemeVariant.Dark;
        ApplyToApplication();
        SaveToDisk();
        ThemeChanged?.Invoke(this, ActiveVariant);
    }

    public void Apply(ThemeVariant variant)
    {
        ActiveVariant = variant;
        ApplyToApplication();
        SaveToDisk();
        ThemeChanged?.Invoke(this, ActiveVariant);
    }

    private void ApplyToApplication()
    {
        if (Application.Current is { } app)
        {
            app.RequestedThemeVariant = ActiveVariant;
        }
    }

    private static ThemeVariant LoadFromDisk()
    {
        try
        {
            if (!File.Exists(ConfigPath)) return ThemeVariant.Dark;
            var json = File.ReadAllText(ConfigPath);
            var prefs = JsonSerializer.Deserialize<ThemePrefs>(json);
            return prefs?.Theme?.ToLowerInvariant() switch
            {
                "light" => ThemeVariant.Light,
                _ => ThemeVariant.Dark,
            };
        }
        catch
        {
            return ThemeVariant.Dark;
        }
    }

    private void SaveToDisk()
    {
        try
        {
            Directory.CreateDirectory(ConfigDir);
            var prefs = new ThemePrefs { Theme = ActiveVariant == ThemeVariant.Light ? "light" : "dark" };
            File.WriteAllText(ConfigPath, JsonSerializer.Serialize(prefs));
        }
        catch
        {
            // Best-effort; ignore IO failures
        }
    }

    private sealed class ThemePrefs
    {
        public string? Theme { get; set; }
    }
}
