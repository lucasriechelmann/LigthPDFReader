using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Avalonia;
using LightPdf.Core;

// The app is Windows-only by design (Win32 printing, registry file association, win-x64 PDFium).
[assembly: SupportedOSPlatform("windows")]

namespace LightPdf.App;

internal static partial class Program
{
    private const int ASFW_ANY = -1;

    // Don't use Avalonia APIs before AppMain: nothing is initialized yet.
    [STAThread]
    public static int Main(string[] args)
    {
        var options = StartupOptions.Parse(args);

        // One reader per session: a second launch hands its files to the first and exits right away.
        // Automated runs (--bench, --print-to-pdf) always run on their own.
        SingleInstance? instance = null;
        if (!options.Automated)
        {
            string id = SingleInstance.DefaultId;
            instance = SingleInstance.TryAcquire(id);
            if (instance is null)
            {
                // We were just launched by the user, so we may take the foreground; let the primary have it.
                AllowSetForegroundWindow(ASFW_ANY);
                if (SingleInstance.TrySend(id, options.FilePaths, TimeSpan.FromSeconds(3)))
                    return 0;
                // The primary didn't answer (hung or shutting down): run standalone rather than do nothing.
            }
        }

        App.StartupOptions = options;
        App.Instance = instance;
        try
        {
            return BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
        }
        finally
        {
            instance?.Dispose();
        }
    }

    // Avalonia configuration, don't remove; also used by visual designer.
    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
#if DEBUG
            .WithDeveloperTools()
#endif
            .LogToTrace();

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool AllowSetForegroundWindow(int processId);
}
