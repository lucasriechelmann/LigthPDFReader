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
        // Installer hooks: register/unregister .pdf for this exe and exit, without any UI.
        if (args is ["--register"]) { Registration.Register(); return 0; }
        if (args is ["--unregister"]) { Registration.Unregister(); return 0; }

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
    {

        // Default GPU path (ANGLE → Direct3D). Measured in M6: software rendering starts ~170 ms faster and
        // uses far less memory, but drops to ~12 fps when scrolling a maximized 3440 px window; WGL was worse
        // on every measure. Smooth scrolling wins.
        return AppBuilder.Configure<App>()
            .UsePlatformDetect()
#if DEBUG
            .WithDeveloperTools()
#endif
            .LogToTrace();
    }

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool AllowSetForegroundWindow(int processId);
}
