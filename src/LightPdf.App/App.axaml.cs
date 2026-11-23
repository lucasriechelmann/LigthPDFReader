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
/// Command line: <c>LightPdf.exe [--bench] [--snapshot out.png] [file.pdf]</c>.
/// --bench prints startup timing and exits; --snapshot saves the first rendered page.
/// </summary>
public sealed record StartupOptions(string? FilePath, bool Bench, string? SnapshotPath = null)
{
    public static StartupOptions Parse(string[] args)
    {
        bool bench = false;
        string? snapshot = null, file = null;
        for (int i = 0; i < args.Length; i++)
        {
            if (args[i].Equals("--bench", StringComparison.OrdinalIgnoreCase)) bench = true;
            else if (args[i].Equals("--snapshot", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length) snapshot = args[++i];
            else if (!args[i].StartsWith("--", StringComparison.Ordinal)) file ??= args[i];
        }
        return new StartupOptions(file, bench, snapshot);
    }
}
