using System.Diagnostics;
using System.Runtime.InteropServices;
using Avalonia.Controls;
using Avalonia.Interactivity;
using LightPdf.Core;

namespace LightPdf.App;

/// <summary>Windows integration: files from other launches, and the .pdf file association.</summary>
public partial class MainWindow
{
    private const string DocumentIconFile = "pdf-document.ico";

    /// <summary>Another launch (double-click, "Open with", command line) handed us its files.</summary>
    internal async void OpenFromOtherInstance(IReadOnlyList<string> paths)
    {
        if (WindowState == WindowState.Minimized)
            WindowState = WindowState.Normal;
        Activate();
        // The launching process allowed us to take the foreground (AllowSetForegroundWindow).
        if (TryGetPlatformHandle()?.Handle is { } hwnd and not 0)
            Native.SetForegroundWindow(hwnd);

        if (paths.Count > 0)
            await OpenFilesAsync(paths);
    }

    private void OnMakeDefaultClick(object? sender, RoutedEventArgs e)
    {
        if (Environment.ProcessPath is not { } exe) return;
        string icon = Path.Combine(AppContext.BaseDirectory, DocumentIconFile);
        try
        {
            FileAssociation.ForCurrentUser().Register(exe, File.Exists(icon) ? icon : $"{exe},0");
            Native.NotifyAssociationsChanged();
            // Windows only lets the user pick the default; take them straight to this app's page.
            Process.Start(new ProcessStartInfo(FileAssociation.SettingsUri) { UseShellExecute = true });
            ShowStatus("Registered. In Settings, choose LightPdf for .pdf files.");
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException or System.ComponentModel.Win32Exception)
        {
            ShowStatus($"Could not register: {ex.Message}");
        }
    }

    private void OnUnregisterClick(object? sender, RoutedEventArgs e)
    {
        if (Environment.ProcessPath is not { } exe) return;
        try
        {
            FileAssociation.ForCurrentUser().Unregister(exe);
            Native.NotifyAssociationsChanged();
            ShowStatus("LightPdf removed from the .pdf \"Open with\" list.");
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
        {
            ShowStatus($"Could not unregister: {ex.Message}");
        }
    }

    private static partial class Native
    {
        private const int SHCNE_ASSOCCHANGED = 0x08000000;
        private const uint SHCNF_IDLIST = 0;

        [LibraryImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static partial bool SetForegroundWindow(IntPtr hwnd);

        [LibraryImport("shell32.dll")]
        private static partial void SHChangeNotify(int eventId, uint flags, IntPtr item1, IntPtr item2);

        /// <summary>Tells Explorer to refresh icons and "Open with" after registry changes.</summary>
        public static void NotifyAssociationsChanged() => SHChangeNotify(SHCNE_ASSOCCHANGED, SHCNF_IDLIST, IntPtr.Zero, IntPtr.Zero);
    }
}
