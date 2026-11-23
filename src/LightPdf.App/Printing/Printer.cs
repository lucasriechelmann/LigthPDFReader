using System.Runtime.InteropServices;
using LightPdf.Core;
using static LightPdf.App.Printing.Win32Print;

namespace LightPdf.App.Printing;

/// <summary>What the user chose in the print dialog: a printer DC (owned by the caller) and the pages.</summary>
internal sealed record PrintRequest(IntPtr PrinterDc, IReadOnlyList<int> Pages);

internal static class Printer
{
    private const int MaxRanges = 32;

    /// <summary>
    /// Shows the standard Windows print dialog (printer, page ranges like "1-3, 7", copies, preferences).
    /// Copies and collation are handled by the driver. Returns null if cancelled. UI thread only (STA).
    /// </summary>
    public static PrintRequest? ShowDialog(IntPtr ownerWindow, int pageCount, int currentPage)
    {
        IntPtr ranges = Marshal.AllocHGlobal(Marshal.SizeOf<PRINTPAGERANGE>() * MaxRanges);
        var pd = new PRINTDLGEX
        {
            lStructSize = (uint)Marshal.SizeOf<PRINTDLGEX>(),
            hwndOwner = ownerWindow,
            Flags = PD_RETURNDC | PD_USEDEVMODECOPIESANDCOLLATE | PD_NOSELECTION,
            nMinPage = 1,
            nMaxPage = (uint)pageCount,
            nCopies = 1,
            nMaxPageRanges = MaxRanges,
            lpPageRanges = ranges,
            nStartPage = START_PAGE_GENERAL,
        };
        try
        {
            int hr = PrintDlgEx(ref pd);
            if (hr != 0 || pd.dwResultAction != PD_RESULT_PRINT)
            {
                if (pd.hDC != IntPtr.Zero) DeleteDC(pd.hDC);
                return null;
            }

            List<int> pages;
            if ((pd.Flags & PD_PAGENUMS) != 0)
            {
                var list = new List<(int, int)>();
                for (int i = 0; i < pd.nPageRanges; i++)
                {
                    var r = Marshal.PtrToStructure<PRINTPAGERANGE>(ranges + i * Marshal.SizeOf<PRINTPAGERANGE>());
                    list.Add(((int)r.nFromPage, (int)r.nToPage));
                }
                pages = PrintLayout.ExpandRanges(list, pageCount);
            }
            else if ((pd.Flags & PD_CURRENTPAGE) != 0)
            {
                pages = [currentPage];
            }
            else
            {
                pages = Enumerable.Range(0, pageCount).ToList();
            }
            return new PrintRequest(pd.hDC, pages);
        }
        finally
        {
            Marshal.FreeHGlobal(ranges);
            if (pd.hDevMode != IntPtr.Zero) GlobalFree(pd.hDevMode);
            if (pd.hDevNames != IntPtr.Zero) GlobalFree(pd.hDevNames);
        }
    }

    /// <summary>A DC for a named printer without any dialog (used for automated print-to-PDF checks).</summary>
    public static IntPtr CreatePrinterDc(string printerName) => CreateDC("WINSPOOL", printerName, null, IntPtr.Zero);

    /// <summary>
    /// Prints <paramref name="pages"/> as one job, one PDFium work item per page (so the viewer keeps
    /// rendering in between), then deletes the DC. On cancellation or error the job is aborted.
    /// </summary>
    /// <param name="outputFile">Print to this file instead of the printer's port (null = normal printing).</param>
    public static async Task RunJobAsync(PdfWorker worker, PdfDocument document, IntPtr printerDc, string jobName,
        IReadOnlyList<int> pages, string? outputFile, IProgress<int> progress, CancellationToken cancellationToken)
    {
        bool started = false;
        try
        {
            PrinterPage printer = await worker.Run(() => ReadPrinterPage(printerDc));
            started = await worker.Run(() => StartJob(printerDc, jobName, outputFile));
            if (!started)
                throw new IOException("The printer did not accept the print job.");

            for (int i = 0; i < pages.Count; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                int page = pages[i];
                await worker.Run(() =>
                {
                    if (StartPage(printerDc) <= 0) throw new IOException("The printer rejected a page.");
                    document.PrintPage(page, printerDc, PrintLayout.Place(document.GetPageSize(page), printer));
                    if (EndPage(printerDc) <= 0) throw new IOException("The printer rejected a page.");
                });
                progress.Report(i + 1);
            }

            await worker.Run(() => { EndDoc(printerDc); });
            started = false;
        }
        finally
        {
            try
            {
                if (started) await worker.Run(() => { AbortDoc(printerDc); });
                await worker.Run(() => { DeleteDC(printerDc); });
            }
            catch (InvalidOperationException) { /* worker already shut down (app closing) */ }
        }
    }

    private static PrinterPage ReadPrinterPage(IntPtr dc) => new(
        PaperWidth: GetDeviceCaps(dc, PHYSICALWIDTH), PaperHeight: GetDeviceCaps(dc, PHYSICALHEIGHT),
        PrintableX: GetDeviceCaps(dc, PHYSICALOFFSETX), PrintableY: GetDeviceCaps(dc, PHYSICALOFFSETY),
        PrintableWidth: GetDeviceCaps(dc, HORZRES), PrintableHeight: GetDeviceCaps(dc, VERTRES),
        DpiX: GetDeviceCaps(dc, LOGPIXELSX), DpiY: GetDeviceCaps(dc, LOGPIXELSY));

    private static bool StartJob(IntPtr dc, string jobName, string? outputFile)
    {
        IntPtr name = Marshal.StringToHGlobalUni(jobName);
        IntPtr output = outputFile is null ? IntPtr.Zero : Marshal.StringToHGlobalUni(outputFile);
        try
        {
            var info = new DOCINFOW { cbSize = Marshal.SizeOf<DOCINFOW>(), lpszDocName = name, lpszOutput = output };
            return Win32Print.StartDoc(dc, ref info) > 0;
        }
        finally
        {
            Marshal.FreeHGlobal(name);
            if (output != IntPtr.Zero) Marshal.FreeHGlobal(output);
        }
    }
}
