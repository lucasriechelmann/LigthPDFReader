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

    private void CheckPage(int pageIndex)
    {
        ObjectDisposedException.ThrowIf(_handle.IsClosed, this);
        ArgumentOutOfRangeException.ThrowIfNegative(pageIndex);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(pageIndex, PageCount);
    }

    public void Dispose() => _handle.Dispose();
}
