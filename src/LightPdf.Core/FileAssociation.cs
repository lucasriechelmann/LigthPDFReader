using System.Runtime.Versioning;
using Microsoft.Win32;

namespace LightPdf.Core;

/// <summary>
/// Per-user registration for .pdf files (no admin rights): a ProgID, an "Open with" entry, and the
/// Capabilities / RegisteredApplications entries that list the app in Settings › Default apps.
/// Windows 10/11 don't let an app make itself the default; the user confirms that in Settings.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class FileAssociation(RegistryKey root)
{
    public const string ProgId = "LightPdf.Document";
    public const string AppName = "LightPdf";
    public const string CapabilitiesPath = @"Software\LightPdf\Capabilities";

    /// <summary>Opens the app's own page in Settings › Default apps (Windows 11).</summary>
    public const string SettingsUri = "ms-settings:defaultapps?registeredAppUser=" + AppName;

    /// <summary>The real thing: HKEY_CURRENT_USER.</summary>
    public static FileAssociation ForCurrentUser() => new(Registry.CurrentUser);

    public void Register(string exePath, string documentIconPath)
    {
        string openCommand = $"\"{exePath}\" \"%1\"";
        using (var classes = root.CreateSubKey(@"Software\Classes"))
        {
            using (var prog = classes.CreateSubKey(ProgId))
            {
                prog.SetValue("", "PDF Document");
                using (var icon = prog.CreateSubKey("DefaultIcon")) icon.SetValue("", documentIconPath);
                using (var cmd = prog.CreateSubKey(@"shell\open\command")) cmd.SetValue("", openCommand);
            }
            using (var openWith = classes.CreateSubKey(@".pdf\OpenWithProgids"))
                openWith.SetValue(ProgId, Array.Empty<byte>(), RegistryValueKind.None);
            using (var app = classes.CreateSubKey(@"Applications\" + Path.GetFileName(exePath)))
            {
                app.SetValue("FriendlyAppName", AppName);
                using (var types = app.CreateSubKey("SupportedTypes")) types.SetValue(".pdf", "");
                using (var cmd = app.CreateSubKey(@"shell\open\command")) cmd.SetValue("", openCommand);
            }
        }
        using (var caps = root.CreateSubKey(CapabilitiesPath))
        {
            caps.SetValue("ApplicationName", AppName);
            caps.SetValue("ApplicationDescription", "A fast, minimal PDF reader.");
            caps.SetValue("ApplicationIcon", $"{exePath},0");
            using var assoc = caps.CreateSubKey("FileAssociations");
            assoc.SetValue(".pdf", ProgId);
        }
        using (var registered = root.CreateSubKey(@"Software\RegisteredApplications"))
            registered.SetValue(AppName, CapabilitiesPath);
    }

    /// <summary>Removes everything <see cref="Register"/> added, and nothing else.</summary>
    public void Unregister(string exePath)
    {
        using (var classes = root.OpenSubKey(@"Software\Classes", writable: true))
        {
            if (classes is not null)
            {
                classes.DeleteSubKeyTree(ProgId, throwOnMissingSubKey: false);
                classes.DeleteSubKeyTree(@"Applications\" + Path.GetFileName(exePath), throwOnMissingSubKey: false);
                using var openWith = classes.OpenSubKey(@".pdf\OpenWithProgids", writable: true);
                openWith?.DeleteValue(ProgId, throwOnMissingValue: false);
            }
        }
        root.DeleteSubKeyTree(CapabilitiesPath, throwOnMissingSubKey: false);
        using (var app = root.OpenSubKey(@"Software\LightPdf", writable: true))
            if (app is not null && app.SubKeyCount == 0 && app.ValueCount == 0)
                root.DeleteSubKey(@"Software\LightPdf", throwOnMissingSubKey: false);
        using var registered = root.OpenSubKey(@"Software\RegisteredApplications", writable: true);
        registered?.DeleteValue(AppName, throwOnMissingValue: false);
    }

    /// <summary>True when .pdf files are registered to open with this exact exe.</summary>
    public bool IsRegistered(string exePath)
    {
        using var cmd = root.OpenSubKey($@"Software\Classes\{ProgId}\shell\open\command");
        return string.Equals(cmd?.GetValue("") as string, $"\"{exePath}\" \"%1\"", StringComparison.OrdinalIgnoreCase);
    }
}
