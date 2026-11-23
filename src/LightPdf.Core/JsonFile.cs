using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;

namespace LightPdf.Core;

// Source-generated: no runtime reflection, so it works under Native AOT and trimming.
[JsonSerializable(typeof(RecentFilesData))]
[JsonSerializable(typeof(AppSettings))]
[JsonSourceGenerationOptions(WriteIndented = true, UseStringEnumConverter = true)]
internal sealed partial class LightPdfJsonContext : JsonSerializerContext;

/// <summary>Small JSON files in %AppData%\LightPdf: tolerant loading, atomic saving.</summary>
internal static class JsonFile
{
    public static string DefaultPath(string fileName) => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "LightPdf", fileName);

    /// <summary>The stored value, or null when the file is missing, unreadable or corrupt.</summary>
    public static T? Load<T>(string? path, JsonTypeInfo<T> type) where T : class
    {
        if (path is null || !File.Exists(path)) return null;
        try
        {
            using var stream = File.OpenRead(path);
            return JsonSerializer.Deserialize(stream, type);
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>Writes via a temp file + rename, so a crash never leaves half a file. Failures are ignored.</summary>
    public static void Save<T>(string? path, T value, JsonTypeInfo<T> type)
    {
        if (path is null) return;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            string temp = path + ".tmp";
            using (var stream = File.Create(temp))
                JsonSerializer.Serialize(stream, value, type);
            File.Move(temp, path, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Losing a preference is not worth interrupting the user for.
        }
    }
}
