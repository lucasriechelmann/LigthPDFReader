using LightPdf.Pdfium;

namespace LightPdf.Core;

public sealed class PdfException(PdfiumError error, string message) : Exception(message)
{
    public PdfiumError Error { get; } = error;

    internal static PdfException FromLastError(string context)
    {
        var error = (PdfiumError)PdfiumNative.FPDF_GetLastError();
        string reason = error switch
        {
            PdfiumError.File => "the file could not be found or opened",
            PdfiumError.Format => "the file is not a PDF or is corrupted",
            PdfiumError.Password => "a password is required or the password is wrong",
            PdfiumError.Security => "the document uses an unsupported security scheme",
            PdfiumError.Page => "the page could not be found or contains an error",
            _ => "an unknown error occurred",
        };
        return new PdfException(error, $"{context}: {reason}.");
    }
}
