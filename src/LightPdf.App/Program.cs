using Avalonia;

namespace LightPdf.App;

internal static class Program
{
    // Don't use Avalonia APIs before AppMain: nothing is initialized yet.
    [STAThread]
    public static void Main(string[] args) => BuildAvaloniaApp()
        .StartWithClassicDesktopLifetime(args);

    // Also used by the XAML designer.
    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
#if DEBUG
            .WithDeveloperTools()
#endif
            .LogToTrace();
}
