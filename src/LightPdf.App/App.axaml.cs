using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;

namespace LightPdf.App;

public partial class App : Application
{
    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            desktop.MainWindow = new MainWindow(StartupOptions.Parse(desktop.Args ?? []));

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
            else if (!args[i].StartsWith("--", StringComparison.Ordinal)) files.Add(args[i]);
        }
        return new StartupOptions(files, bench, snapshot, printToPdf);
    }
}
