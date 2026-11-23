using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using LightPdf.Core;

namespace LightPdf.App;

public partial class App : Application
{
    /// <summary>Set by Program.Main before Avalonia starts.</summary>
    internal static StartupOptions? StartupOptions { get; set; }

    /// <summary>Non-null when this process is the session's primary reader (not in automated runs).</summary>
    internal static SingleInstance? Instance { get; set; }

    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var window = new MainWindow(StartupOptions ?? LightPdf.App.StartupOptions.Parse(desktop.Args ?? []));
            desktop.MainWindow = window;
            // Files from later launches (double-clicks, "Open with") arrive here, on the pipe thread.
            Instance?.Listen(paths => Dispatcher.UIThread.Post(() => window.OpenFromOtherInstance(paths)));
        }

        base.OnFrameworkInitializationCompleted();
    }
}

/// <summary>
/// Command line: <c>LightPdf.exe [--bench] [--snapshot out.png] [--print-to-pdf out.pdf] [file.pdf ...]</c>.
/// --bench prints startup timing and exits; --snapshot saves the first rendered page;
/// --print-to-pdf prints the first file through "Microsoft Print to PDF" (no dialog) and exits.
/// The automated modes leave the recent-files list untouched.
/// </summary>
public sealed record StartupOptions(IReadOnlyList<string> FilePaths, bool Bench,
    string? SnapshotPath = null, string? PrintToPdfPath = null)
{
    public bool Automated => Bench || PrintToPdfPath is not null;

    public static StartupOptions Parse(string[] args)
    {
        bool bench = false;
        string? snapshot = null, printToPdf = null;
        var files = new List<string>();
        for (int i = 0; i < args.Length; i++)
        {
            if (args[i].Equals("--bench", StringComparison.OrdinalIgnoreCase)) bench = true;
            else if (args[i].Equals("--snapshot", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length) snapshot = args[++i];
            else if (args[i].Equals("--print-to-pdf", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length) printToPdf = Path.GetFullPath(args[++i]);
            else if (!args[i].StartsWith("--", StringComparison.Ordinal)) files.Add(FullPath(args[i]));
        }
        return new StartupOptions(files, bench, snapshot, printToPdf);
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
