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
}

[StructLayout(LayoutKind.Sequential)]
public struct FsSizeF
{
    public float Width;
    public float Height;
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
