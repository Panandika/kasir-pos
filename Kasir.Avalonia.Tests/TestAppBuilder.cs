using Avalonia;
using Avalonia.Headless;

[assembly: AvaloniaTestApplication(typeof(KasirAvaloniaTests.TestAppBuilder))]

namespace KasirAvaloniaTests;

public class TestAppBuilder
{
    public static AppBuilder BuildAvaloniaApp() => AppBuilder
        .Configure<Kasir.Avalonia.App>()
        .UseHeadless(new AvaloniaHeadlessPlatformOptions());
}
