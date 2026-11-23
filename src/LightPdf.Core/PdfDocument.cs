using LightPdf.Pdfium;

namespace LightPdf.Core;

/// <summary>Page size in PDF points (1/72 inch).</summary>
public readonly record struct PageSize(double Width, double Height);

/// <summary>
/// An open PDF file. Not thread-safe: use one instance from one thread
/// (M1 adds a dedicated render thread that owns all documents).
/// </summary>
public sealed class PdfDocument : IDisposable
{
    private readonly SafeDocumentHandle _handle;

    private PdfDocument(SafeDocumentHandle handle, string path)
    {
        _handle = handle;
        FilePath = path;
        PageCount = PdfiumNative.FPDF_GetPageCount(handle);
    }

    public string FilePath { get; }
    public int PageCount { get; }

    public static PdfDocument Open(string path, string? password = null)
    {
        PdfLibrary.EnsureInitialized();
        var handle = PdfiumNative.FPDF_LoadDocument(Path.GetFullPath(path), password);
        if (handle.IsInvalid)
        {
            handle.Dispose();
            throw PdfException.FromLastError($"Cannot open '{Path.GetFileName(path)}'");
        }
        return new PdfDocument(handle, path);
    }

    /// <summary>Page size in points, without loading or rendering the page.</summary>
    public PageSize GetPageSize(int pageIndex)
    {
        CheckPage(pageIndex);
        if (!PdfiumNative.FPDF_GetPageSizeByIndexF(_handle, pageIndex, out var size))
            throw PdfException.FromLastError($"Cannot read size of page {pageIndex + 1}");
        return new PageSize(size.Width, size.Height);
    }

    /// <summary>All page sizes in points. Reads metadata only (no rendering), so it is fast even for large files.</summary>
    public PageSize[] GetPageSizes()
    {
        var sizes = new PageSize[PageCount];
        for (int i = 0; i < sizes.Length; i++)
            sizes[i] = GetPageSize(i);
        return sizes;
    }

    /// <summary>
    /// Renders a page into caller-owned BGRA (premultiplied, opaque) memory, scaled to
    /// <paramref name="width"/> x <paramref name="height"/> pixels. No intermediate copy.
    /// </summary>
    public void RenderPage(int pageIndex, IntPtr buffer, int width, int height, int stride)
    {
        CheckPage(pageIndex);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);
        ArgumentOutOfRangeException.ThrowIfLessThan(stride, width * 4);

        using var page = PdfiumNative.FPDF_LoadPage(_handle, pageIndex);
        if (page.IsInvalid)
            throw PdfException.FromLastError($"Cannot load page {pageIndex + 1}");

        IntPtr bitmap = PdfiumNative.FPDFBitmap_CreateEx(width, height, PdfiumNative.BitmapFormatBgra, buffer, stride);
        if (bitmap == IntPtr.Zero)
            throw new PdfException(PdfiumError.Unknown, "Cannot create render bitmap.");
        try
        {
            PdfiumNative.FPDFBitmap_FillRect(bitmap, 0, 0, width, height, 0xFFFFFFFF); // opaque white paper
            PdfiumNative.FPDF_RenderPageBitmap(bitmap, page, 0, 0, width, height, rotate: 0,
                PdfiumNative.RenderAnnotations | PdfiumNative.RenderLcdText);
        }
        finally
        {
            PdfiumNative.FPDFBitmap_Destroy(bitmap); // frees the wrapper only; buffer is ours
        }
    }

    /// <summary>
    /// Prints a page onto a printer device context (between StartPage and EndPage) as vector output,
    /// at the position and rotation from <see cref="PrintLayout.Place"/>.
    /// </summary>
    public void PrintPage(int pageIndex, IntPtr printerDc, PrintPlacement placement)
    {
        CheckPage(pageIndex);
        using var page = PdfiumNative.FPDF_LoadPage(_handle, pageIndex);
        if (page.IsInvalid)
            throw PdfException.FromLastError($"Cannot load page {pageIndex + 1}");
        PdfiumNative.FPDF_RenderPage(printerDc, page, placement.X, placement.Y, placement.Width, placement.Height,
            placement.Rotate, PdfiumNative.RenderAnnotations | PdfiumNative.RenderPrinting);
    }

    /// <summary>Extracts a page's text and per-character boxes (normalized, rotation applied).</summary>
    public unsafe PageText GetPageText(int pageIndex)
    {
        CheckPage(pageIndex);
        using var page = PdfiumNative.FPDF_LoadPage(_handle, pageIndex);
        if (page.IsInvalid)
            throw PdfException.FromLastError($"Cannot load page {pageIndex + 1}");

        IntPtr textPage = PdfiumNative.FPDFText_LoadPage(page);
        if (textPage == IntPtr.Zero)
            return new PageText(pageIndex, "", []);
        try
        {
            int count = Math.Max(0, PdfiumNative.FPDFText_CountChars(textPage));
            string text = "";
            if (count > 0)
            {
                var buffer = new char[count + 1]; // + terminator
                fixed (char* p = buffer)
                    PdfiumNative.FPDFText_GetText(textPage, 0, count, p);
                text = new string(buffer, 0, count);
            }

            var toNorm = PageTransform.Create(page);
            var boxes = new NormRect[count];
            for (int i = 0; i < count; i++)
            {
                if (PdfiumNative.FPDFText_GetLooseCharBox(textPage, i, out FsRectF r) && r.Right > r.Left && r.Top > r.Bottom)
                {
                    var (x1, y1) = toNorm.Apply(r.Left, r.Top);
                    var (x2, y2) = toNorm.Apply(r.Right, r.Bottom);
                    boxes[i] = NormRect.FromPoints(x1, y1, x2, y2);
                }
            }
            return new PageText(pageIndex, text, boxes, toNorm.MapsPageXToVertical);
        }
        finally
        {
            PdfiumNative.FPDFText_ClosePage(textPage); // before the page itself is closed
        }
    }

    /// <summary>
    /// Affine map from page space (points, y up, unrotated) to normalized display coordinates (0..1, y down,
    /// /Rotate applied), derived from three FPDF_PageToDevice probes on a large virtual device for precision.
    /// </summary>
    private readonly record struct PageTransform(double Ox, double Oy, double XxPerPt, double XyPerPt, double YxPerPt, double YyPerPt)
    {
        private const int Device = 1_000_000;
        private const double Probe = 1000;

        public static PageTransform Create(SafePageHandle page)
        {
            static (double, double) Map(SafePageHandle p, double x, double y)
            {
                PdfiumNative.FPDF_PageToDevice(p, 0, 0, Device, Device, 0, x, y, out int dx, out int dy);
                return (dx / (double)Device, dy / (double)Device);
            }
            var (ox, oy) = Map(page, 0, 0);
            var (ax, ay) = Map(page, Probe, 0);
            var (bx, by) = Map(page, 0, Probe);
            return new PageTransform(ox, oy, (ax - ox) / Probe, (ay - oy) / Probe, (bx - ox) / Probe, (by - oy) / Probe);
        }

        public (double X, double Y) Apply(double x, double y)
            => (Ox + x * XxPerPt + y * YxPerPt, Oy + x * XyPerPt + y * YyPerPt);

        /// <summary>True for /Rotate 90 or 270: text written along page x runs vertically on screen.</summary>
        public bool MapsPageXToVertical => Math.Abs(XyPerPt) > Math.Abs(XxPerPt);
    }

    private void CheckPage(int pageIndex)
    {
        ObjectDisposedException.ThrowIf(_handle.IsClosed, this);
        ArgumentOutOfRangeException.ThrowIfNegative(pageIndex);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(pageIndex, PageCount);
    }

    public void Dispose() => _handle.Dispose();
}
