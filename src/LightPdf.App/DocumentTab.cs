using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Media;
using LightPdf.Core;

namespace LightPdf.App;

/// <summary>The content of one tab: a scrollable page view that owns one open document.</summary>
internal sealed class DocumentTab : UserControl
{
    private static readonly IBrush Backdrop = new SolidColorBrush(Color.FromRgb(0x3a, 0x3a, 0x3a));

    public DocumentTab(PdfWorker worker, PdfDocument document, PageSize[] pageSizes, ViewState? initialView)
    {
        Document = document;
        Pages = new PdfPagesView
        {
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center,
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Top,
        };
        Scroller = new ScrollViewer
        {
            Background = Backdrop,
            Focusable = true,
            FocusAdorner = null,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Content = Pages,
        };
        Content = Scroller;
        Pages.SetDocument(worker, document, pageSizes, initialView);
    }

    public PdfDocument Document { get; }
    public string FilePath => Document.FilePath;
    public string FileName => Path.GetFileName(Document.FilePath);
    public PdfPagesView Pages { get; }
    public ScrollViewer Scroller { get; }

    /// <summary>Stops rendering and closes the document on the PDFium thread (after any in-flight work).</summary>
    public void Close(PdfWorker worker)
    {
        Pages.ClearDocument();
        _ = worker.Run(Document.Dispose);
    }
}
