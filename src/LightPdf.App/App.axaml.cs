using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Styling;
using Avalonia.Threading;
using LightPdf.Core;

namespace LightPdf.App;

public partial class App : Application
{
    /// <summary>Set by Program.Main before Avalonia starts.</summary>
    internal static StartupOptions? StartupOptions { get; set; }

    /// <summary>Non-null when this process is the session's primary reader (not in automated runs).</summary>
    internal static SingleInstance? Instance { get; set; }

    /// <summary>User preferences (theme, night reading). In-memory only for automated runs.</summary>
    internal static SettingsStore Settings { get; private set; } = new(null);

    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public static void ApplyTheme(ThemePreference theme) => Current!.RequestedThemeVariant = theme switch
    {
        ThemePreference.Light => ThemeVariant.Light,
        ThemePreference.Dark => ThemeVariant.Dark,
        _ => ThemeVariant.Default, // follow Windows
    };

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var options = StartupOptions ?? LightPdf.App.StartupOptions.Parse(desktop.Args ?? []);
            Settings = options.Automated ? new SettingsStore(null) : SettingsStore.CreateDefault();
            Settings.Load();
            ApplyTheme(Settings.Current.Theme); // before the window exists: no flash of the wrong theme

            var window = new MainWindow(options);
            desktop.MainWindow = window;

            // A bug in one handler (often an async void one) shouldn't take open documents down with it:
            // log it, tell the user, keep running. Non-UI-thread failures are at least logged.
            Dispatcher.UIThread.UnhandledException += (_, e) =>
            {
                ErrorLog.Write(e.Exception, "UI thread");
                e.Handled = true;
                window.ReportUnexpectedError(e.Exception);
            };
            AppDomain.CurrentDomain.UnhandledException += (_, e) => ErrorLog.Write(e.ExceptionObject as Exception, "fatal");
            TaskScheduler.UnobservedTaskException += (_, e) =>
            {
                ErrorLog.Write(e.Exception, "unobserved task");
                e.SetObserved();
            };
            // Files from later launches (double-clicks, "Open with") arrive here, on the pipe thread.
            Instance?.Listen(paths => Dispatcher.UIThread.Post(() => window.OpenFromOtherInstance(paths)));
        }

        base.OnFrameworkInitializationCompleted();
    }
}

/// <summary>
/// Command line: <c>LightPdf.exe [--maximized] [--bench | --bench-scroll N] [--snapshot out.png]
/// [--print-to-pdf out.pdf] [file.pdf ...]</c>.
/// --bench prints startup timing and exits; --bench-scroll N also pages down N times and reports frame pacing,
/// memory and CPU; --snapshot saves the first rendered page; --print-to-pdf prints the first file through
/// "Microsoft Print to PDF" (no dialog) and exits. Automated modes leave the recent files and settings untouched.
/// </summary>
public sealed record StartupOptions(IReadOnlyList<string> FilePaths, bool Bench,
    string? SnapshotPath = null, string? PrintToPdfPath = null, int BenchScrollPages = 0, bool StartMaximized = false)
{
    public bool Automated => Bench || PrintToPdfPath is not null;

    public static StartupOptions Parse(string[] args)
    {
        bool bench = false, maximized = false;
        int scrollPages = 0;
        string? snapshot = null, printToPdf = null;
        var files = new List<string>();
        for (int i = 0; i < args.Length; i++)
        {
            if (args[i].Equals("--bench", StringComparison.OrdinalIgnoreCase)) bench = true;
            else if (args[i].Equals("--maximized", StringComparison.OrdinalIgnoreCase)) maximized = true;
            else if (args[i].Equals("--bench-scroll", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length
                     && int.TryParse(args[++i], out scrollPages)) bench = true;
            else if (args[i].Equals("--snapshot", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length) snapshot = args[++i];
            else if (args[i].Equals("--print-to-pdf", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length) printToPdf = Path.GetFullPath(args[++i]);
            else if (!args[i].StartsWith("--", StringComparison.Ordinal)) files.Add(FullPath(args[i]));
        }
        return new StartupOptions(files, bench, snapshot, printToPdf, scrollPages, maximized);
    }

    /// <summary>
    /// Absolute paths, because they may be handed to another instance with a different working directory.
    /// A malformed path is kept as-is, so opening it reports a normal "cannot open" error.
    /// </summary>
    private static string FullPath(string path)
    {
        try { return Path.GetFullPath(path); }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException) { return path; }
    }
}
