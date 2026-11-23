namespace LightPdf.Core.Tests;

public class PrintLayoutTests
{
    // A4 at 600 dpi: 4960 x 7016 px; typical laser margins of 0.17" (100 px).
    private static readonly PrinterPage A4Portrait600 = new(
        PaperWidth: 4960, PaperHeight: 7016,
        PrintableX: 100, PrintableY: 100, PrintableWidth: 4760, PrintableHeight: 6816,
        DpiX: 600, DpiY: 600);

    private static readonly PageSize A5 = new(420, 595);   // smaller than A4
    private static readonly PageSize A4 = new(595, 842);   // same as the paper: larger than the printable area
    private static readonly PageSize A4Landscape = new(842, 595);

    [Fact]
    public void SmallerPage_PrintsAtActualSize_CenteredOnPaper()
    {
        var p = PrintLayout.Place(A5, A4Portrait600);

        Assert.Equal(1.0, p.Scale);
        Assert.Equal(0, p.Rotate);
        Assert.Equal(3500, p.Width);                         // 420pt = 5.833in * 600
        // Centered on the physical sheet, expressed in printable-area coordinates.
        Assert.Equal((4960 - 3500) / 2 - 100, p.X);
        Assert.Equal((int)Math.Round((7016 - 4958.33) / 2 - 100), p.Y);
    }

    [Fact]
    public void PageLargerThanPrintableArea_IsShrunkToFitAndCentered()
    {
        var p = PrintLayout.Place(A4, A4Portrait600);

        Assert.True(p.Scale < 1);
        Assert.True(p.Width <= A4Portrait600.PrintableWidth && p.Height <= A4Portrait600.PrintableHeight);
        // Tight dimension fills the printable area; the other is centered.
        Assert.True(p.Height == A4Portrait600.PrintableHeight || p.Width == A4Portrait600.PrintableWidth);
        Assert.Equal((A4Portrait600.PrintableWidth - p.Width) / 2.0, p.X, precision: 0);
    }

    [Fact]
    public void LandscapePageOnPortraitPaper_IsRotated()
    {
        var p = PrintLayout.Place(A4Landscape, A4Portrait600);

        Assert.Equal(1, p.Rotate);
        Assert.True(p.Height > p.Width, "rotated page should be tall on the sheet");
    }

    [Fact]
    public void LandscapePageOnLandscapePaper_IsNotRotated()
    {
        var landscapePaper = A4Portrait600 with
        {
            PaperWidth = 7016, PaperHeight = 4960, PrintableWidth = 6816, PrintableHeight = 4760,
        };
        Assert.Equal(0, PrintLayout.Place(A4Landscape, landscapePaper).Rotate);
    }

    [Fact]
    public void NeverEnlarges()
    {
        var tiny = new PageSize(72, 72); // 1 inch square
        var p = PrintLayout.Place(tiny, A4Portrait600);
        Assert.Equal(1.0, p.Scale);
        Assert.Equal(600, p.Width);
    }

    [Fact]
    public void UnevenMargins_KeepActualSizePageInsidePrintableArea()
    {
        // Big top margin: centering on the paper would put the top edge above the printable area.
        var printer = A4Portrait600 with { PrintableY = 1200, PrintableHeight = 5716 };
        var p = PrintLayout.Place(new PageSize(420, 595), printer); // A5: 4958 px tall, fits in 5716

        Assert.InRange(p.Y, 0, printer.PrintableHeight - p.Height);
    }

    [Fact]
    public void ExpandRanges_ConvertsToZeroBasedInOrder()
    {
        Assert.Equal([0, 1, 2, 6, 9, 10], PrintLayout.ExpandRanges([(1, 3), (7, 7), (10, 11)], pageCount: 20));
    }

    [Fact]
    public void ExpandRanges_ClampsAndHandlesReversedRanges()
    {
        Assert.Equal([4, 3, 2], PrintLayout.ExpandRanges([(5, 3)], pageCount: 20));
        Assert.Equal([8, 9], PrintLayout.ExpandRanges([(9, 99)], pageCount: 10));
    }
}
