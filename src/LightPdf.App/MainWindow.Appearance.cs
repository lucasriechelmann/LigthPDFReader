using System.Reflection;
using Avalonia.Controls;
using Avalonia.Interactivity;
using LightPdf.Core;

namespace LightPdf.App;

/// <summary>Theme, night reading, and About.</summary>
public partial class MainWindow
{
    private static AppSettings Settings => App.Settings.Current;

    /// <summary>Reflects the saved settings in the ⋮ menu's check marks.</summary>
    private void SyncAppearanceMenu()
    {
        InvertColorsItem.IsChecked = Settings.InvertPageColors;
        ThemeSystemItem.IsChecked = Settings.Theme == ThemePreference.System;
        ThemeLightItem.IsChecked = Settings.Theme == ThemePreference.Light;
        ThemeDarkItem.IsChecked = Settings.Theme == ThemePreference.Dark;
    }

    private void OnInvertColorsClick(object? sender, RoutedEventArgs e) => ToggleInvertColors();

    private void ToggleInvertColors()
    {
        Settings.InvertPageColors = !Settings.InvertPageColors;
        App.Settings.Save();
        foreach (var item in TabItems)
            if (item.Content is DocumentTab tab)
                tab.Pages.InvertColors = Settings.InvertPageColors;
        SyncAppearanceMenu();
    }

    private void OnThemeClick(object? sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem { Tag: string tag } || !Enum.TryParse(tag, out ThemePreference theme)) return;
        Settings.Theme = theme;
        App.Settings.Save();
        App.ApplyTheme(theme);
        SyncAppearanceMenu();
    }

    private void OnAboutClick(object? sender, RoutedEventArgs e)
    {
        string version = Assembly.GetExecutingAssembly()
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion.Split('+')[0] ?? "?";
        ShowInfo($"LightPdf {version} · a fast, minimal PDF reader. PDF rendering by PDFium (BSD), UI by Avalonia (MIT). " +
                 "Licenses: THIRD-PARTY-NOTICES.txt next to LightPdf.exe.");
    }
}
