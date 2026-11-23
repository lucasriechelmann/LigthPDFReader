using LightPdf.Pdfium;

namespace LightPdf.Core;

/// <summary>Process-wide PDFium initialization. Safe to call repeatedly.</summary>
public static class PdfLibrary
{
    private static int _initialized;

    public static void EnsureInitialized()
    {
        if (Interlocked.Exchange(ref _initialized, 1) == 0)
            PdfiumNative.FPDF_InitLibrary();
    }
}
