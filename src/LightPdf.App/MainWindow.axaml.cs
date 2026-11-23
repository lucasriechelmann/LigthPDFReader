using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Platform.Storage;
using LightPdf.Core;

namespace LightPdf.App;

/// <summary>M0 spike: open a PDF and show one page at a time, fit to width.</summary>
public partial class MainWindow : Window
{
    private readonly StartupOptions _options;
    private PdfDocument? _document;
    private int _pageIndex;

    public MainWindow() : this(new StartupOptions(null, false)) { } // designer

    public MainWindow(StartupOptions options)
    {
        _options = options;
        InitializeComponent();
        AddHandler(DragDrop.DropEvent, OnDrop);
    }

    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);
        if (_options.FilePath is { } path)
            OpenFile(path);
        else if (_options.Bench)
            Close();
    }

    private async void OnOpenClick(object? sender, RoutedEventArgs e)
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Open PDF",
            AllowMultiple = false,
            FileTypeFilter = [new FilePickerFileType("PDF documents") { Patterns = ["*.pdf"] }],
        });
        if (files.Count > 0 && files[0].TryGetLocalPath() is { } path)
            OpenFile(path);
    }

    private void OnDrop(object? sender, DragEventArgs e)
    {
        var file = e.DataTransfer.TryGetFiles()?.FirstOrDefault();
        if (file?.TryGetLocalPath() is { } path)
            OpenFile(path);
    }

    private void OnPrevClick(object? sender, RoutedEventArgs e) => ShowPage(_pageIndex - 1);
    private void OnNextClick(object? sender, RoutedEventArgs e) => ShowPage(_pageIndex + 1);

    private void OpenFile(string path)
    {
        try
        {
            var doc = PdfDocument.Open(path);
            _document?.Dispose();
            _document = doc;
            Title = $"{Path.GetFileName(path)} - LightPdf";
            ShowPage(0);
        }
        catch (PdfException ex)
        {
            StatusText.Text = ex.Message;
            if (_options.Bench) { Console.Error.WriteLine(ex.Message); Close(); }
        }
    }

    private void ShowPage(int index)
    {
        if (_document is null || index < 0 || index >= _document.PageCount)
            return;
        _pageIndex = index;

        var sw = Stopwatch.StartNew();
        var bitmap = RenderFitWidth(_document, index);
        long renderMs = sw.ElapsedMilliseconds;
        PageImage.Source = bitmap;
        if (_options.SnapshotPath is { } snapshot && index == 0)
            bitmap.Save(snapshot, new PngBitmapEncoderOptions());

        PageText.Text = $"{index + 1} / {_document.PageCount}";
        PrevButton.IsEnabled = index > 0;
        NextButton.IsEnabled = index < _document.PageCount - 1;
        StatusText.Text = $"render {renderMs} ms";
        Scroller.Offset = default;

        // Report once the frame containing the page has actually been presented.
        RequestAnimationFrame(_ =>
        {
            double startupMs = (DateTime.Now - Process.GetCurrentProcess().StartTime).TotalMilliseconds;
            if (_options.Bench)
            {
                Console.WriteLine($"startup_to_first_page_ms={startupMs:F0} render_ms={renderMs}");
                Close();
            }
        });
    }

    /// <summary>Renders at device pixels so text stays crisp on high-DPI screens.</summary>
    private WriteableBitmap RenderFitWidth(PdfDocument doc, int index)
    {
        PageSize size = doc.GetPageSize(index);
        double scaling = RenderScaling;
        double targetWidthDip = Math.Max(200, Scroller.Bounds.Width is > 0 and var w ? w - 48 : ClientSize.Width - 48);
        double dipPerPoint = targetWidthDip / size.Width;

        int pxWidth = (int)Math.Round(size.Width * dipPerPoint * scaling);
        int pxHeight = (int)Math.Round(size.Height * dipPerPoint * scaling);

        var bitmap = new WriteableBitmap(new PixelSize(pxWidth, pxHeight), new Vector(96 * scaling, 96 * scaling),
            PixelFormat.Bgra8888, AlphaFormat.Premul);
        using (var fb = bitmap.Lock())
            doc.RenderPage(index, fb.Address, fb.Size.Width, fb.Size.Height, fb.RowBytes);
        return bitmap;
    }

    protected override void OnClosed(EventArgs e)
    {
        _document?.Dispose();
        base.OnClosed(e);
    }
}
