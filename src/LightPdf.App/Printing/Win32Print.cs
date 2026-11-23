using System.Runtime.InteropServices;

namespace LightPdf.App.Printing;

/// <summary>
/// The Win32 printing APIs we need: the PrintDlgEx dialog (comdlg32) and the GDI print-job calls (gdi32).
/// All structs are blittable so source-generated P/Invoke works under Native AOT.
/// </summary>
internal static partial class Win32Print
{
    // PrintDlgEx flags
    public const uint PD_NOSELECTION = 0x4;
    public const uint PD_PAGENUMS = 0x2;
    public const uint PD_RETURNDC = 0x100;
    public const uint PD_USEDEVMODECOPIESANDCOLLATE = 0x40000;
    public const uint PD_CURRENTPAGE = 0x400000;
    public const uint START_PAGE_GENERAL = 0xFFFFFFFF;
    public const uint PD_RESULT_PRINT = 1;

    // GetDeviceCaps indexes
    public const int HORZRES = 8, VERTRES = 10, LOGPIXELSX = 88, LOGPIXELSY = 90;
    public const int PHYSICALWIDTH = 110, PHYSICALHEIGHT = 111, PHYSICALOFFSETX = 112, PHYSICALOFFSETY = 113;

    [StructLayout(LayoutKind.Sequential)]
    public struct PRINTDLGEX
    {
        public uint lStructSize;
        public IntPtr hwndOwner;
        public IntPtr hDevMode;
        public IntPtr hDevNames;
        public IntPtr hDC;
        public uint Flags;
        public uint Flags2;
        public uint ExclusionFlags;
        public uint nPageRanges;
        public uint nMaxPageRanges;
        public IntPtr lpPageRanges;   // PRINTPAGERANGE[nMaxPageRanges]
        public uint nMinPage;
        public uint nMaxPage;
        public uint nCopies;
        public IntPtr hInstance;
        public IntPtr lpPrintTemplateName;
        public IntPtr lpCallback;
        public uint nPropertyPages;
        public IntPtr lphPropertyPages;
        public uint nStartPage;
        public uint dwResultAction;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct PRINTPAGERANGE
    {
        public uint nFromPage;
        public uint nToPage;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct DOCINFOW
    {
        public int cbSize;
        public IntPtr lpszDocName;   // LPCWSTR
        public IntPtr lpszOutput;    // LPCWSTR: print to this file instead of the port (null = normal)
        public IntPtr lpszDatatype;
        public uint fwType;
    }

    /// <summary>Returns an HRESULT; on S_OK check <see cref="PRINTDLGEX.dwResultAction"/>.</summary>
    [LibraryImport("comdlg32.dll", EntryPoint = "PrintDlgExW")]
    public static partial int PrintDlgEx(ref PRINTDLGEX pd);

    [LibraryImport("gdi32.dll", EntryPoint = "CreateDCW", StringMarshalling = StringMarshalling.Utf16)]
    public static partial IntPtr CreateDC(string? driver, string device, string? output, IntPtr initData);

    [LibraryImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool DeleteDC(IntPtr hdc);

    [LibraryImport("gdi32.dll", EntryPoint = "StartDocW")]
    public static partial int StartDoc(IntPtr hdc, ref DOCINFOW docInfo);

    [LibraryImport("gdi32.dll")]
    public static partial int StartPage(IntPtr hdc);

    [LibraryImport("gdi32.dll")]
    public static partial int EndPage(IntPtr hdc);

    [LibraryImport("gdi32.dll")]
    public static partial int EndDoc(IntPtr hdc);

    [LibraryImport("gdi32.dll")]
    public static partial int AbortDoc(IntPtr hdc);

    [LibraryImport("gdi32.dll")]
    public static partial int GetDeviceCaps(IntPtr hdc, int index);

    [LibraryImport("kernel32.dll")]
    public static partial IntPtr GlobalFree(IntPtr hMem);
}
