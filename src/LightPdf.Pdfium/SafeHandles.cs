using Microsoft.Win32.SafeHandles;

namespace LightPdf.Pdfium;

// Dispose these explicitly, on the PDFium thread. Finalizer release is only a
// last-resort safety net (it would run on the GC finalizer thread).

public sealed class SafeDocumentHandle : SafeHandleZeroOrMinusOneIsInvalid
{
    public SafeDocumentHandle() : base(ownsHandle: true) { }

    protected override bool ReleaseHandle()
    {
        PdfiumNative.FPDF_CloseDocument(handle);
        return true;
    }
}

public sealed class SafePageHandle : SafeHandleZeroOrMinusOneIsInvalid
{
    public SafePageHandle() : base(ownsHandle: true) { }

    protected override bool ReleaseHandle()
    {
        PdfiumNative.FPDF_ClosePage(handle);
        return true;
    }
}
