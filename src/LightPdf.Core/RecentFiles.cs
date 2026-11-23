using System.Text.Json.Serialization;

namespace LightPdf.Core;

/// <summary>Where the reader was in a document: restored when the file is opened again.</summary>
public readonly record struct ViewState(int Page, double Zoom, ZoomMode Mode)
{
    public static ViewState Default => new(0, 1.0, ZoomMode.FitWidth);
}

public sealed class RecentFile
{
    public string Path { get; set; } = "";
    public DateTime LastOpenedUtc { get; set; }
    public int Page { get; set; }
    public double Zoom { get; set; } = 1.0;
    public ZoomMode Mode { get; set; } = ZoomMode.FitWidth;

    [JsonIgnore]
    public ViewState View => new(Page, Zoom, Mode);
}

public sealed class RecentFilesData
{
    public List<RecentFile> Recent { get; set; } = [];
}

/// <summary>
/// Most-recently-opened files with their last view, persisted as JSON
/// (<c>%AppData%\LightPdf\recent.json</c> by default). Not thread-safe: use from the UI thread.
/// </summary>
public sealed class RecentFilesStore
{
    public const int MaxEntries = 20;

    private readonly string? _filePath;
    private List<RecentFile> _items = [];

    /// <param name="filePath">JSON file to load/save; null keeps everything in memory only.</param>
    public RecentFilesStore(string? filePath) => _filePath = filePath;

    public static RecentFilesStore CreateDefault() => new(JsonFile.DefaultPath("recent.json"));

    public IReadOnlyList<RecentFile> Items => _items;

    /// <summary>Loads the file; a missing or corrupt file just means an empty list.</summary>
    public void Load()
    {
        var data = JsonFile.Load(_filePath, LightPdfJsonContext.Default.RecentFilesData);
        _items = (data?.Recent ?? [])
            .Where(r => !string.IsNullOrWhiteSpace(r.Path))
            .DistinctBy(r => Normalize(r.Path), StringComparer.OrdinalIgnoreCase)
            .Take(MaxEntries)
            .ToList();
    }

    /// <summary>Writes atomically (temp file + rename), so a crash never leaves half a file.</summary>
    public void Save() => JsonFile.Save(_filePath, new RecentFilesData { Recent = _items }, LightPdfJsonContext.Default.RecentFilesData);

    public RecentFile? Find(string path)
    {
        string key = Normalize(path);
        return _items.Find(r => string.Equals(Normalize(r.Path), key, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>Moves <paramref name="path"/> to the top; updates its view when one is given.</summary>
    public void Touch(string path, ViewState? view = null)
    {
        var entry = Find(path) ?? new RecentFile { Path = Normalize(path) };
        _items.Remove(entry);
        _items.Insert(0, entry);
        entry.LastOpenedUtc = DateTime.UtcNow;
        if (view is { } v)
        {
            entry.Page = Math.Max(0, v.Page);
            entry.Zoom = Zoom.Clamp(v.Zoom);
            entry.Mode = v.Mode;
        }
        if (_items.Count > MaxEntries)
            _items.RemoveRange(MaxEntries, _items.Count - MaxEntries);
    }

    public void Remove(string path)
    {
        if (Find(path) is { } entry) _items.Remove(entry);
    }

    public void Clear() => _items.Clear();

    private static string Normalize(string path)
    {
        try { return System.IO.Path.GetFullPath(path); }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException) { return path; }
    }
}
