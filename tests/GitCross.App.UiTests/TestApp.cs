using Avalonia;
using Avalonia.Headless;
using GitCross.App;

[assembly: AvaloniaTestApplication(typeof(GitCross.App.UiTests.TestAppBuilder))]

namespace GitCross.App.UiTests;

public static class TestAppBuilder
{
    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>().WithInterFont().UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false });
}
