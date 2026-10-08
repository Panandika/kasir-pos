using Avalonia;
using Avalonia.Headless;

[assembly: AvaloniaTestApplication(typeof(KasirAvaloniaTests.TestAppBuilder))]

namespace KasirAvaloniaTests;

public class TestAppBuilder
{
    public static AppBuilder BuildAvaloniaApp() => AppBuilder
        .Configure<Kasir.Avalonia.App>()
        // Real Skia text shaping (embedded app fonts) instead of the headless stub, so
        // layout tests (ButtonLayoutTests) measure labels the way the store PCs render them.
        .UseSkia()
        .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false });
}
