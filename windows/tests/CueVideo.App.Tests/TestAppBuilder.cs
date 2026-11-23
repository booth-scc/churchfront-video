using Avalonia;
using Avalonia.Headless;

[assembly: AvaloniaTestApplication(typeof(CueVideo.App.Tests.TestAppBuilder))]

namespace CueVideo.App.Tests;

/// <summary>Boots the real app markup and resources without a desktop window or renderer.</summary>
public static class TestAppBuilder
{
    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UseHeadless(new AvaloniaHeadlessPlatformOptions());
}
