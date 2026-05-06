using Avalonia;
using Avalonia.Headless.NUnit;
using Avalonia.Styling;
using NUnit.Framework;

namespace KasirAvaloniaTests;

public class FormParseTests
{
    [AvaloniaTest]
    public void ShellWindow_loads_under_dark_variant()
    {
        Application.Current!.RequestedThemeVariant = ThemeVariant.Dark;
        var window = new Kasir.Avalonia.ShellWindow();
        Assert.That(window, Is.Not.Null);
    }

    [AvaloniaTest]
    public void ShellWindow_loads_under_light_variant()
    {
        Application.Current!.RequestedThemeVariant = ThemeVariant.Light;
        var window = new Kasir.Avalonia.ShellWindow();
        Assert.That(window, Is.Not.Null);
    }
}
