using System.Diagnostics;
using System.Runtime.InteropServices;
using Avalonia.Controls;
using Avalonia.Interactivity;
using LightPdf.Core;

namespace LightPdf.App;

/// <summary>Windows integration: files from other launches, and the .pdf file association.</summary>
public partial class MainWindow
{
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
        try
        {
            Registration.Register();
            // Windows only lets the user pick the default; take them straight to this app's page.
            Process.Start(new ProcessStartInfo(FileAssociation.SettingsUri) { UseShellExecute = true });
            ShowStatus("Registered. In Settings, choose LightPdf for .pdf files.");
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException or InvalidOperationException
                                   or System.ComponentModel.Win32Exception)
        {
            ShowError($"Could not register: {ex.Message}");
        }
    }

    private void OnUnregisterClick(object? sender, RoutedEventArgs e)
    {
        try
        {
            Registration.Unregister();
            ShowStatus("LightPdf removed from the .pdf \"Open with\" list.");
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException or InvalidOperationException)
        {
            ShowError($"Could not unregister: {ex.Message}");
        }
    }

    private static partial class Native
    {
        [LibraryImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static partial bool SetForegroundWindow(IntPtr hwnd);
    }
}
