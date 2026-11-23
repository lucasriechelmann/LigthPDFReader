using System.Runtime.InteropServices;

namespace LightPdf.Pdfium;

/// <summary>
/// Raw PDFium imports (see fpdfview.h). Source-generated P/Invoke, so it is Native AOT safe.
/// PDFium is NOT thread-safe: every call must come from one thread at a time.
/// </summary>
public static partial class PdfiumNative
{
    private const string Lib = "pdfium";

    // Bitmap formats (FPDFBitmap_*)
    public const int BitmapFormatBgra = 4;

    // Render flags (FPDF_*)
    public const int RenderAnnotations = 0x01;
    public const int RenderLcdText = 0x02;
    public const int RenderPrinting = 0x800;

    [LibraryImport(Lib)]
    public static partial void FPDF_InitLibrary();

    [LibraryImport(Lib)]
    public static partial void FPDF_DestroyLibrary();

    /// <summary>Path and password are UTF-8 encoded.</summary>
    [LibraryImport(Lib, StringMarshalling = StringMarshalling.Utf8)]
    public static partial SafeDocumentHandle FPDF_LoadDocument(string filePath, string? password);

    [LibraryImport(Lib)]
    internal static partial void FPDF_CloseDocument(IntPtr document);

    /// <summary>Returns a <see cref="PdfiumError"/> code. C <c>unsigned long</c> is 32-bit on Windows.</summary>
    [LibraryImport(Lib)]
    public static partial uint FPDF_GetLastError();

    [LibraryImport(Lib)]
    public static partial int FPDF_GetPageCount(SafeDocumentHandle document);

    [LibraryImport(Lib)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool FPDF_GetPageSizeByIndexF(SafeDocumentHandle document, int pageIndex, out FsSizeF size);

    [LibraryImport(Lib)]
    public static partial SafePageHandle FPDF_LoadPage(SafeDocumentHandle document, int pageIndex);

    [LibraryImport(Lib)]
    internal static partial void FPDF_ClosePage(IntPtr page);

    /// <summary>Wraps caller-owned memory when <paramref name="firstScan"/> is non-null (no copy).</summary>
    [LibraryImport(Lib)]
    public static partial IntPtr FPDFBitmap_CreateEx(int width, int height, int format, IntPtr firstScan, int stride);

    [LibraryImport(Lib)]
    public static partial void FPDFBitmap_Destroy(IntPtr bitmap);

    [LibraryImport(Lib)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool FPDFBitmap_FillRect(IntPtr bitmap, int left, int top, int width, int height, uint color);

    [LibraryImport(Lib)]
    public static partial void FPDF_RenderPageBitmap(IntPtr bitmap, SafePageHandle page,
        int startX, int startY, int sizeX, int sizeY, int rotate, int flags);

    /// <summary>
    /// Windows only: draws a page onto a GDI device context (e.g. a printer DC) as vector drawing
    /// commands. Coordinates are in device pixels relative to the DC's origin.
    /// </summary>
    [LibraryImport(Lib)]
    public static partial void FPDF_RenderPage(IntPtr dc, SafePageHandle page,
        int startX, int startY, int sizeX, int sizeY, int rotate, int flags);

    /// <summary>Maps a point in page space (points, origin bottom-left, before /Rotate) to device pixels.</summary>
    [LibraryImport(Lib)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool FPDF_PageToDevice(SafePageHandle page, int startX, int startY, int sizeX, int sizeY,
        int rotate, double pageX, double pageY, out int deviceX, out int deviceY);

    // ---- fpdf_text.h ------------------------------------------------------------------

    /// <summary>Returns a text page handle (IntPtr.Zero on failure). Close with <see cref="FPDFText_ClosePage"/> before the page.</summary>
    [LibraryImport(Lib)]
    public static partial IntPtr FPDFText_LoadPage(SafePageHandle page);

    [LibraryImport(Lib)]
    public static partial void FPDFText_ClosePage(IntPtr textPage);

    [LibraryImport(Lib)]
    public static partial int FPDFText_CountChars(IntPtr textPage);

    /// <summary>Writes UTF-16 text for [start, start+count) plus a terminator; returns units written including it.</summary>
    [LibraryImport(Lib)]
    public static unsafe partial int FPDFText_GetText(IntPtr textPage, int startIndex, int count, char* result);

    /// <summary>Character box covering the font's full ascent/descent (good for highlights), in page space.</summary>
    [LibraryImport(Lib)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool FPDFText_GetLooseCharBox(IntPtr textPage, int index, out FsRectF rect);
}

[StructLayout(LayoutKind.Sequential)]
public struct FsSizeF
{
    public float Width;
    public float Height;
}

/// <summary>FS_RECTF: page-space rectangle (top &gt; bottom, since y grows upwards).</summary>
[StructLayout(LayoutKind.Sequential)]
public struct FsRectF
{
    public float Left;
    public float Top;
    public float Right;
    public float Bottom;
}

/// <summary>FPDF_ERR_* codes.</summary>
public enum PdfiumError : uint
{
    Success = 0,
    Unknown = 1,
    File = 2,
    Format = 3,
    Password = 4,
    Security = 5,
    Page = 6,
}
