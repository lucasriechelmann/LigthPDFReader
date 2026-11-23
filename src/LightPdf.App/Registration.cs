using System.Runtime.InteropServices;
using LightPdf.Core;

namespace LightPdf.App;

/// <summary>
/// .pdf registration for this exe, shared by the ⋮ menu and the <c>--register</c> / <c>--unregister</c>
/// command-line flags (used by the installer).
/// </summary>
internal static partial class Registration
{
    private const string DocumentIconFile = "pdf-document.ico";

    public static void Register()
    {
        string exe = Environment.ProcessPath ?? throw new InvalidOperationException("Unknown executable path.");
        string icon = Path.Combine(AppContext.BaseDirectory, DocumentIconFile);
        FileAssociation.ForCurrentUser().Register(exe, File.Exists(icon) ? icon : $"{exe},0");
        NotifyAssociationsChanged();
    }

    public static void Unregister()
    {
        string exe = Environment.ProcessPath ?? throw new InvalidOperationException("Unknown executable path.");
        FileAssociation.ForCurrentUser().Unregister(exe);
        NotifyAssociationsChanged();
    }

    private const int SHCNE_ASSOCCHANGED = 0x08000000;
    private const uint SHCNF_IDLIST = 0;

    [LibraryImport("shell32.dll")]
    private static partial void SHChangeNotify(int eventId, uint flags, IntPtr item1, IntPtr item2);

    /// <summary>Tells Explorer to refresh icons and "Open with" after registry changes.</summary>
    private static void NotifyAssociationsChanged() => SHChangeNotify(SHCNE_ASSOCCHANGED, SHCNF_IDLIST, IntPtr.Zero, IntPtr.Zero);
}
