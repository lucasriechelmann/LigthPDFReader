using System.Runtime.InteropServices;
using LightPdf.Pdfium;

namespace LightPdf.Core.Tests;

[Collection(PdfiumCollection.Name)]
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
    public void Open_Encrypted_WithoutPassword_ThrowsPasswordError()
    {
        string path = TestPdf.WriteToTemp(userPassword: "secret");

        var ex = Assert.Throws<PdfException>(() => PdfDocument.Open(path));
        Assert.Equal(PdfiumError.Password, ex.Error);

        var wrong = Assert.Throws<PdfException>(() => PdfDocument.Open(path, "nope"));
        Assert.Equal(PdfiumError.Password, wrong.Error);
    }

    [Fact]
    public void Open_Encrypted_WithPassword_RendersDecryptedContent()
    {
        using var doc = PdfDocument.Open(TestPdf.WriteToTemp(userPassword: "secret"), "secret");
        const int w = 612, h = 792, stride = w * 4;
        IntPtr buffer = Marshal.AllocHGlobal(stride * h);
        try
        {
            doc.RenderPage(0, buffer, w, h, stride);
            Assert.Equal(0xFF000000u, Pixel(buffer, stride, x: 200, y: 592)); // square decrypted and drawn
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    [Fact]
    public void GetPageSizes_ReturnsAllPages()
    {
        using var doc = PdfDocument.Open(TestPdf.WriteToTemp(pageCount: 4));
        Assert.Equal(Enumerable.Repeat(new PageSize(612, 792), 4), doc.GetPageSizes());
    }

    private static string WriteBytes(byte[] bytes)
    {
        string path = Path.Combine(Path.GetTempPath(), "lightpdf-tests", $"{Guid.NewGuid():N}.pdf");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, bytes);
        return path;
    }

    [Fact]
    public void Open_EmptyFile_ThrowsFormatError()
    {
        var ex = Assert.Throws<PdfException>(() => PdfDocument.Open(WriteBytes([])));
        Assert.Equal(PdfiumError.Format, ex.Error);
    }

    [Fact]
    public void Open_RandomBytes_ThrowsFormatError()
    {
        var bytes = new byte[64 * 1024];
        new Random(42).NextBytes(bytes);
        var ex = Assert.Throws<PdfException>(() => PdfDocument.Open(WriteBytes(bytes)));
        Assert.Equal(PdfiumError.Format, ex.Error);
    }

    [Theory]
    [InlineData(0.9)]
    [InlineData(0.6)]
    [InlineData(0.3)]
    public void Open_TruncatedFile_EitherRepairsOrFailsCleanly(double keep)
    {
        byte[] full = TestPdf.Create(pageCount: 5, textLines: ["Some text on every page"]);
        string path = WriteBytes(full[..(int)(full.Length * keep)]);

        try
        {
            using var doc = PdfDocument.Open(path); // PDFium rebuilds a missing xref when it can
            var sizes = doc.GetPageSizes();
            IntPtr buffer = Marshal.AllocHGlobal(100 * 100 * 4);
            try
            {
                for (int i = 0; i < doc.PageCount; i++)
                {
                    try { doc.RenderPage(i, buffer, 100, 100, 400); }
                    catch (PdfException) { /* a page that didn't survive: reported, not fatal */ }
                }
            }
            finally { Marshal.FreeHGlobal(buffer); }
            Assert.All(sizes, s => Assert.True(s.Width > 0 && s.Height > 0));
        }
        catch (PdfException ex)
        {
            Assert.Equal(PdfiumError.Format, ex.Error); // unrecoverable: a clean, specific error
        }
    }

    [Fact]
    public void RenderPage_GarbageContentStream_DoesNotThrow()
    {
        // Valid structure, nonsense drawing operators: PDFium skips what it can't parse.
        byte[] pdf = TestPdf.Create();
        string text = System.Text.Encoding.Latin1.GetString(pdf)
            .Replace("0 0 0 rg 100 100 200 200 re f", "q Q ]] << >> 12 Tf BT ET ((( rg zz Do"[..29]);
        using var doc = PdfDocument.Open(WriteBytes(System.Text.Encoding.Latin1.GetBytes(text)));
        IntPtr buffer = Marshal.AllocHGlobal(100 * 100 * 4);
        try { doc.RenderPage(0, buffer, 100, 100, 400); }
        finally { Marshal.FreeHGlobal(buffer); }
        Assert.Equal(1, doc.PageCount);
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
