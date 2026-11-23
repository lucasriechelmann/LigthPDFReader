using System.Diagnostics;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using LightPdf.Core;
using LightPdf.Pdfium;

namespace LightPdf.App;

public partial class MainWindow : Window
{
    private readonly StartupOptions _options;
    private readonly PdfWorker _worker = new();
    private PdfDocument? _document;
    private bool _benchReported;

    public MainWindow() : this(new StartupOptions(null, false)) { } // designer

    public MainWindow(StartupOptions options)
    {
        _options = options;
        InitializeComponent();
        AddHandler(DragDrop.DropEvent, OnDrop);
        Pages.ViewChanged += (_, _) => UpdateToolbar();
        Pages.PageRendered += OnPageRendered;
        PageBox.KeyDown += OnPageBoxKeyDown;
        PageBox.LostFocus += (_, _) => UpdateToolbar();
        UpdateToolbar();
    }

    protected override async void OnOpened(EventArgs e)
    {
        base.OnOpened(e);
        if (_options.FilePath is { } path)
            await OpenFileAsync(path);
        else if (_options.Bench)
            Close();
    }

    // ---------------------------------------------------------------- opening

    private async void OnOpenClick(object? sender, RoutedEventArgs e) => await ShowOpenDialogAsync();

    private async Task ShowOpenDialogAsync()
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Open PDF",
            AllowMultiple = false,
            FileTypeFilter = [new FilePickerFileType("PDF documents") { Patterns = ["*.pdf"] }],
        });
        if (files.Count > 0 && files[0].TryGetLocalPath() is { } path)
            await OpenFileAsync(path);
    }

    private async void OnDrop(object? sender, DragEventArgs e)
    {
        var file = e.DataTransfer.TryGetFiles()?.FirstOrDefault();
        if (file?.TryGetLocalPath() is { } path)
            await OpenFileAsync(path);
    }

    private async Task OpenFileAsync(string path)
    {
        string? password = null;
        while (true)
        {
            try
            {
                var (document, sizes) = await _worker.Run(() =>
                {
                    var doc = PdfDocument.Open(path, password);
                    try { return (doc, doc.GetPageSizes()); }
                    catch { doc.Dispose(); throw; }
                });
                ShowDocument(document, sizes);
                return;
            }
            catch (PdfException ex) when (ex.Error == PdfiumError.Password && !_options.Bench)
            {
                password = await PasswordDialog.AskAsync(this, Path.GetFileName(path), retry: password is not null);
                if (password is null) return; // cancelled
            }
            catch (PdfException ex)
            {
                StatusText.Text = ex.Message;
                if (_options.Bench) { Console.Error.WriteLine(ex.Message); Close(); }
                return;
            }
        }
    }

    private void ShowDocument(PdfDocument document, PageSize[] sizes)
    {
        PdfDocument? previous = _document;
        _document = document;
        Pages.SetDocument(_worker, document, sizes);
        if (previous is not null)
            _ = _worker.Run(previous.Dispose); // after any in-flight work for it (FIFO)

        Title = $"{Path.GetFileName(document.FilePath)} - LightPdf";
        StatusText.Text = "";
        Scroller.Focus();
    }

    // ---------------------------------------------------------------- toolbar

    private void UpdateToolbar()
    {
        bool hasDoc = Pages.PageCount > 0;
        PageBox.IsEnabled = hasDoc;
        if (!PageBox.IsFocused)
            PageBox.Text = hasDoc ? (Pages.CurrentPage + 1).ToString() : "";
        PageCountText.Text = $"/ {Pages.PageCount}";
        PrevButton.IsEnabled = hasDoc && Pages.CurrentPage > 0;
        NextButton.IsEnabled = hasDoc && Pages.CurrentPage < Pages.PageCount - 1;
        ZoomText.Text = $"{Pages.Zoom * 100:0}%";
        FitWidthButton.IsChecked = Pages.ZoomMode == ZoomMode.FitWidth;
        FitPageButton.IsChecked = Pages.ZoomMode == ZoomMode.FitPage;
    }

    private void OnPrevClick(object? sender, RoutedEventArgs e) => Pages.GoToPage(Pages.CurrentPage - 1);
    private void OnNextClick(object? sender, RoutedEventArgs e) => Pages.GoToPage(Pages.CurrentPage + 1);
    private void OnZoomInClick(object? sender, RoutedEventArgs e) => Pages.ZoomIn();
    private void OnZoomOutClick(object? sender, RoutedEventArgs e) => Pages.ZoomOut();
    private void OnFitWidthClick(object? sender, RoutedEventArgs e) => Pages.SetZoomMode(ZoomMode.FitWidth);
    private void OnFitPageClick(object? sender, RoutedEventArgs e) => Pages.SetZoomMode(ZoomMode.FitPage);

    private void OnPageBoxKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            if (int.TryParse(PageBox.Text, out int page))
                Pages.GoToPage(page - 1);
            Scroller.Focus(); // leaving the box refreshes it with the actual page
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            Scroller.Focus();
            e.Handled = true;
        }
    }

    // ---------------------------------------------------------------- keyboard

    protected override async void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Handled) return;
        bool ctrl = e.KeyModifiers.HasFlag(KeyModifiers.Control);
        e.Handled = true;

        if (ctrl)
        {
            switch (e.Key)
            {
                case Key.O: await ShowOpenDialogAsync(); break;
                case Key.OemPlus or Key.Add: Pages.ZoomIn(); break;
                case Key.OemMinus or Key.Subtract: Pages.ZoomOut(); break;
                case Key.D0 or Key.NumPad0: Pages.SetZoomMode(ZoomMode.FitPage); break;
                case Key.D1 or Key.NumPad1: Pages.SetZoom(1.0); break;
                case Key.D2 or Key.NumPad2: Pages.SetZoomMode(ZoomMode.FitWidth); break;
                case Key.G: PageBox.Focus(); PageBox.SelectAll(); break;
                case Key.Home: Pages.GoToPage(0); break;
                case Key.End: Pages.GoToPage(Pages.PageCount - 1); break;
                default: e.Handled = false; break;
            }
            return;
        }
        if (e.Source is TextBox) { e.Handled = false; return; }

        switch (e.Key)
        {
            case Key.Home: Scroller.ScrollToHome(); break;
            case Key.End: Scroller.ScrollToEnd(); break;
            case Key.PageDown: Scroller.PageDown(); break;
            case Key.PageUp: Scroller.PageUp(); break;
            case Key.Space when e.KeyModifiers.HasFlag(KeyModifiers.Shift): Scroller.PageUp(); break;
            case Key.Space: Scroller.PageDown(); break;
            case Key.Down: Scroller.LineDown(); break;
            case Key.Up: Scroller.LineUp(); break;
            case Key.Left: Pages.GoToPage(Pages.CurrentPage - 1); break;
            case Key.Right: Pages.GoToPage(Pages.CurrentPage + 1); break;
            default: e.Handled = false; break;
        }
    }

    // ---------------------------------------------------------------- bench / snapshot

    private void OnPageRendered(object? sender, PageRenderedEventArgs e)
    {
        if (e.PageIndex != 0 || _benchReported) return;
        if (_options.SnapshotPath is { } snapshot)
            e.Bitmap.Save(snapshot, new PngBitmapEncoderOptions());
        if (!_options.Bench) return;

        _benchReported = true;
        RequestAnimationFrame(_ => // after the frame showing the page is presented
        {
            double ms = (DateTime.Now - Process.GetCurrentProcess().StartTime).TotalMilliseconds;
            Console.WriteLine($"startup_to_first_page_ms={ms:F0}");
            Close();
        });
    }

    protected override void OnClosed(EventArgs e)
    {
        Pages.ClearDocument();
        if (_document is { } doc)
            _ = _worker.Run(doc.Dispose);
        _worker.Dispose();
        base.OnClosed(e);
    }
}
