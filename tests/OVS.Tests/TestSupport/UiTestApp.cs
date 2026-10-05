using Avalonia;
using Avalonia.Headless;
using OVS.Client;
using OVS.Tests.TestSupport;

[assembly: AvaloniaTestApplication(typeof(UiTestApp))]

namespace OVS.Tests.TestSupport;

public static class UiTestApp
{
    // Skia instead of the drawing stub: server logos (PNG, JPG) must really decode and scale in the tests.
    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>().UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false });
}
