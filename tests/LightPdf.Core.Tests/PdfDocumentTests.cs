using System.Runtime.InteropServices;
using LightPdf.Pdfium;

namespace LightPdf.Core.Tests;

public class PdfDocumentTests
{
    [Fact]
    public void Open_ReadsPageCountAndSize()
    {
        using var doc = PdfDocument.Open(TestPdf.WriteToTemp(pageCount: 3));

        Assert.Equal(3, doc.PageCount);
        Assert.Equal(new PageSize(612, 792), doc.GetPageSize(0));
    }

    [Fact]
    public void Open_UnicodePath_Works()
    {
        // FPDF_LoadDocument takes UTF-8; this guards against ANSI-codepage path bugs on Windows.
        using var doc = PdfDocument.Open(TestPdf.WriteToTemp(fileName: "ünïcødé 日本語.pdf"));

        Assert.Equal(1, doc.PageCount);
    }

    [Fact]
    public void Open_MissingFile_ThrowsFileError()
    {
        var ex = Assert.Throws<PdfException>(() => PdfDocument.Open(@"C:\does\not\exist.pdf"));
        Assert.Equal(PdfiumError.File, ex.Error);
    }

    [Fact]
    public void Open_NotAPdf_ThrowsFormatError()
    {
        string path = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.pdf");
        File.WriteAllText(path, "this is not a pdf");

        var ex = Assert.Throws<PdfException>(() => PdfDocument.Open(path));
        Assert.Equal(PdfiumError.Format, ex.Error);
    }

    [Fact]
    public void RenderPage_DrawsBlackSquareOnWhitePaper()
    {
        using var doc = PdfDocument.Open(TestPdf.WriteToTemp());
        const int w = 612, h = 792, stride = w * 4; // 1 px per point
        IntPtr buffer = Marshal.AllocHGlobal(stride * h);
        try
        {
            doc.RenderPage(0, buffer, w, h, stride);

            // PDF square (100..300) in bottom-left coords -> rows 492..692 from the top.
            Assert.Equal(0xFF000000u, Pixel(buffer, stride, x: 200, y: 592)); // inside square: black
            Assert.Equal(0xFFFFFFFFu, Pixel(buffer, stride, x: 10, y: 10));   // corner: white paper
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    [Fact]
    public void GetPageSize_OutOfRange_Throws()
    {
        using var doc = PdfDocument.Open(TestPdf.WriteToTemp());
        Assert.Throws<ArgumentOutOfRangeException>(() => doc.GetPageSize(1));
    }

    private static uint Pixel(IntPtr buffer, int stride, int x, int y)
        => (uint)Marshal.ReadInt32(buffer, y * stride + x * 4); // BGRA little-endian -> 0xAARRGGBB
}
