namespace LightPdf.App;

/// <summary>Appends unexpected errors to <c>%LocalAppData%\LightPdf\errors.log</c> (kept under 1 MB).</summary>
internal static class ErrorLog
{
    private const long MaxBytes = 1 << 20;
    private static readonly object Gate = new();

    public static string FilePath { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LightPdf", "errors.log");

    public static void Write(Exception? exception, string context)
    {
        if (exception is null) return;
        try
        {
            lock (Gate)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
                if (File.Exists(FilePath) && new FileInfo(FilePath).Length > MaxBytes)
                    File.Move(FilePath, FilePath + ".old", overwrite: true); // keep one previous log
                File.AppendAllText(FilePath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {context}{Environment.NewLine}{exception}{Environment.NewLine}{Environment.NewLine}");
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Nowhere left to report to.
        }
    }
}
