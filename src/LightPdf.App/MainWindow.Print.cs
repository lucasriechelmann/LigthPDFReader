using Avalonia.Interactivity;
using LightPdf.App.Printing;
using LightPdf.Core;

namespace LightPdf.App;

/// <summary>Printing for the main window: dialog, progress, cancel.</summary>
public partial class MainWindow
{
    private const string PdfPrinterName = "Microsoft Print to PDF";

    private CancellationTokenSource? _printCts;

    private bool IsPrinting => _printCts is not null;

    private async void OnPrintClick(object? sender, RoutedEventArgs e) => await PrintActiveTabAsync();

    private async Task PrintActiveTabAsync()
    {
        if (ActiveTab is not { } tab || IsPrinting || tab.Pages.PageCount == 0) return;
        IntPtr owner = TryGetPlatformHandle()?.Handle ?? IntPtr.Zero;
        var request = Printer.ShowDialog(owner, tab.Pages.PageCount, tab.Pages.CurrentPage);
        if (request is null) return; // cancelled
        await RunPrintJobAsync(tab, request.PrinterDc, request.Pages, outputFile: null);
    }

    /// <summary>Automated check of the whole print path: prints every page to a PDF file, then exits.</summary>
    private async Task PrintToPdfAndExitAsync(string outputFile)
    {
        var tab = ActiveTab!;
        IntPtr dc = Printer.CreatePrinterDc(PdfPrinterName);
        if (dc == IntPtr.Zero)
        {
            Console.Error.WriteLine($"Printer '{PdfPrinterName}' not found.");
            Close();
            return;
        }
        bool ok = await RunPrintJobAsync(tab, dc, Enumerable.Range(0, tab.Pages.PageCount).ToList(), outputFile);
        Console.WriteLine(ok ? $"printed_pages={tab.Pages.PageCount} output={outputFile}" : $"print_failed: {LastMessage}");
        Close();
    }

    /// <summary>Runs a job with progress in the status bar and a Cancel button. The DC is always deleted.</summary>
    private async Task<bool> RunPrintJobAsync(DocumentTab tab, IntPtr printerDc, IReadOnlyList<int> pages, string? outputFile)
    {
        var cts = _printCts = new CancellationTokenSource();
        void Progress(string text) => ShowProgress(text, "Cancel printing", cts.Cancel);
        Progress($"Printing {pages.Count} page{(pages.Count == 1 ? "" : "s")}…");
        UpdateToolbar();

        // Reports arrive on the UI thread (the job awaits from here), so update the bar directly.
        var progress = new InlineProgress<int>(done => Progress($"Printing page {done} of {pages.Count}…"));
        try
        {
            await Printer.RunJobAsync(_worker, tab.Document, printerDc, tab.FileName, pages, outputFile, progress, cts.Token);
            ShowStatus($"Sent {pages.Count} page{(pages.Count == 1 ? "" : "s")} to the printer");
            return true;
        }
        catch (OperationCanceledException)
        {
            ShowStatus("Printing cancelled");
        }
        catch (Exception ex) when (ex is IOException or PdfException or ObjectDisposedException)
        {
            ShowError($"Printing failed: {ex.Message}");
        }
        finally
        {
            _printCts = null;
            cts.Dispose();
            UpdateToolbar();
        }
        return false;
    }

    private sealed class InlineProgress<T>(Action<T> report) : IProgress<T>
    {
        public void Report(T value) => report(value);
    }
}
