namespace LightPdf.Core;

/// <summary>A printer page, in device pixels.</summary>
/// <param name="PaperWidth">Physical paper width (PHYSICALWIDTH).</param>
/// <param name="PaperHeight">Physical paper height (PHYSICALHEIGHT).</param>
/// <param name="PrintableX">Left edge of the printable area on the paper (PHYSICALOFFSETX).</param>
/// <param name="PrintableY">Top edge of the printable area on the paper (PHYSICALOFFSETY).</param>
/// <param name="PrintableWidth">Printable area width (HORZRES).</param>
/// <param name="PrintableHeight">Printable area height (VERTRES).</param>
/// <param name="DpiX">Device pixels per inch horizontally (LOGPIXELSX).</param>
/// <param name="DpiY">Device pixels per inch vertically (LOGPIXELSY).</param>
public readonly record struct PrinterPage(
    int PaperWidth, int PaperHeight,
    int PrintableX, int PrintableY, int PrintableWidth, int PrintableHeight,
    int DpiX, int DpiY);

/// <summary>Where to draw a PDF page, in printable-area coordinates (what a printer DC uses), plus its rotation.</summary>
/// <param name="Rotate">Quarter turns clockwise, as FPDF_RenderPage expects (0 or 1 here).</param>
public readonly record struct PrintPlacement(int X, int Y, int Width, int Height, int Rotate, double Scale);

public static class PrintLayout
{
    /// <summary>
    /// Actual size, centered on the physical sheet, when the page fits in the printable area;
    /// otherwise shrunk to fit the printable area (never enlarged). A landscape page on portrait paper
    /// (or vice versa) is rotated a quarter turn so it uses the sheet well.
    /// </summary>
    public static PrintPlacement Place(PageSize page, PrinterPage printer)
    {
        bool pageLandscape = page.Width > page.Height;
        bool paperLandscape = printer.PaperWidth > printer.PaperHeight;
        int rotate = pageLandscape != paperLandscape && page.Width != page.Height ? 1 : 0;

        // Page size on the sheet (after rotation), in device pixels at 100%.
        double w = (rotate == 1 ? page.Height : page.Width) / 72.0 * printer.DpiX;
        double h = (rotate == 1 ? page.Width : page.Height) / 72.0 * printer.DpiY;

        double scale = 1;
        if (w > printer.PrintableWidth || h > printer.PrintableHeight)
            scale = Math.Min(printer.PrintableWidth / w, printer.PrintableHeight / h);
        w *= scale;
        h *= scale;

        // At actual size, center on the paper; when shrunk, center in the printable area.
        // Either way, convert to printable-area coordinates (the DC's origin is the printable corner).
        double x, y;
        if (scale == 1)
        {
            x = (printer.PaperWidth - w) / 2 - printer.PrintableX;
            y = (printer.PaperHeight - h) / 2 - printer.PrintableY;
            // Uneven hardware margins can push an edge outside; nudge it back in.
            x = Math.Clamp(x, 0, Math.Max(0, printer.PrintableWidth - w));
            y = Math.Clamp(y, 0, Math.Max(0, printer.PrintableHeight - h));
        }
        else
        {
            x = (printer.PrintableWidth - w) / 2;
            y = (printer.PrintableHeight - h) / 2;
        }
        return new PrintPlacement((int)Math.Round(x), (int)Math.Round(y), (int)Math.Round(w), (int)Math.Round(h), rotate, scale);
    }

    /// <summary>
    /// 1-based inclusive ranges (as the print dialog returns them) to 0-based page indexes, in the order
    /// given, clamped to the document. Ranges may be reversed ("5-3") and may repeat pages.
    /// </summary>
    public static List<int> ExpandRanges(IEnumerable<(int From, int To)> ranges, int pageCount)
    {
        var pages = new List<int>();
        foreach (var (from, to) in ranges)
        {
            int a = Math.Clamp(from, 1, pageCount), b = Math.Clamp(to, 1, pageCount);
            int step = a <= b ? 1 : -1;
            for (int p = a; ; p += step)
            {
                pages.Add(p - 1);
                if (p == b) break;
            }
        }
        return pages;
    }
}
