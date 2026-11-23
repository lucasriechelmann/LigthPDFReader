using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Media;
using LightPdf.Core;

namespace LightPdf.App;

/// <summary>The content of one tab: a scrollable page view that owns one open document.</summary>
internal sealed class DocumentTab : UserControl
{
    public DocumentTab(PdfWorker worker, PdfDocument document, PageSize[] pageSizes, ViewState? initialView, bool invertColors)
    {
        Document = document;
        Pages = new PdfPagesView
        {
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center,
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Top,
            InvertColors = invertColors,
        };
        Scroller = new ScrollViewer
        {
            Focusable = true,
            FocusAdorner = null,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Content = Pages,
        };
        // Theme-aware backdrop: follows light/dark switches live.
        Scroller.Bind(BackgroundProperty, Scroller.GetResourceObservable("ViewerBackdrop"));
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
