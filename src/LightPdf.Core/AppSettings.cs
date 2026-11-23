namespace LightPdf.Core;

public enum ThemePreference
{
    /// <summary>Follow Windows' light/dark app setting.</summary>
    System,
    Light,
    Dark,
}

/// <summary>User preferences, stored in <c>%AppData%\LightPdf\settings.json</c>.</summary>
public sealed class AppSettings
{
    public ThemePreference Theme { get; set; } = ThemePreference.System;

    /// <summary>Night reading: pages drawn light-on-dark (lightness inverted, hues kept).</summary>
    public bool InvertPageColors { get; set; }
}

public sealed class SettingsStore
{
    private readonly string? _filePath;

    /// <param name="filePath">JSON file to load/save; null keeps settings in memory only.</param>
    public SettingsStore(string? filePath) => _filePath = filePath;

    public static SettingsStore CreateDefault() => new(JsonFile.DefaultPath("settings.json"));

    public AppSettings Current { get; private set; } = new();

    /// <summary>Loads the file; a missing or corrupt file gives the defaults.</summary>
    public void Load() => Current = JsonFile.Load(_filePath, LightPdfJsonContext.Default.AppSettings) ?? new AppSettings();

    public void Save() => JsonFile.Save(_filePath, Current, LightPdfJsonContext.Default.AppSettings);
}
