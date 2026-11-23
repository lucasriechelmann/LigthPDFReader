namespace LightPdf.Core.Tests;

public class PageLayoutTests
{
    // Two portrait pages and one landscape page, at 1 DIP per point.
    private static readonly PageSize[] Sizes = [new(100, 200), new(100, 200), new(300, 100)];
    private static PageLayout Layout(double scale = 1) => new(Sizes, scale, gap: 10, margin: 5);

    [Fact]
    public void Extent_IncludesMarginsAndGaps()
    {
        var layout = Layout();
        Assert.Equal(300 + 2 * 5, layout.ExtentWidth);
        Assert.Equal(5 + 200 + 10 + 200 + 10 + 100 + 5, layout.ExtentHeight);
    }

    [Fact]
    public void Pages_AreStackedAndCentered()
    {
        var layout = Layout();
        Assert.Equal(new LayoutRect(105, 5, 100, 200), layout.GetPageRect(0));
        Assert.Equal(new LayoutRect(105, 215, 100, 200), layout.GetPageRect(1));
        Assert.Equal(new LayoutRect(5, 425, 300, 100), layout.GetPageRect(2));
    }

    [Fact]
    public void Scale_MultipliesSizes()
    {
        var rect = Layout(scale: 2).GetPageRect(1);
        Assert.Equal(200, rect.Width);
        Assert.Equal(400, rect.Height);
    }

    [Theory]
    [InlineData(0, 4, 1, 0)]       // inside the top margin (page 0 starts at 5): nothing visible
    [InlineData(0, 100, 0, 0)]
    [InlineData(150, 300, 0, 1)]   // spans the gap between pages 0 and 1
    [InlineData(206, 214, 1, 0)]   // entirely inside the gap: nothing visible
    [InlineData(400, 1000, 1, 2)]
    public void VisibleRange(double top, double bottom, int first, int last)
    {
        var (f, l) = Layout().GetVisibleRange(top, bottom);
        if (first > last) Assert.True(f > l, $"expected empty range, got {f}..{l}");
        else Assert.Equal((first, last), (f, l));
    }

    [Theory]
    [InlineData(0, 0)]     // above first page -> first page
    [InlineData(100, 0)]
    [InlineData(210, 0)]   // in the gap -> page above
    [InlineData(215, 1)]
    [InlineData(5000, 2)]  // below the end -> last page
    public void PageAt(double y, int expected) => Assert.Equal(expected, Layout().PageAt(y));

    [Fact]
    public void EmptyDocument_HasNoVisiblePages()
    {
        var layout = new PageLayout([], 1, 10, 5);
        var (f, l) = layout.GetVisibleRange(0, 1000);
        Assert.True(f > l);
        Assert.Equal(-1, layout.PageAt(0));
    }
}
