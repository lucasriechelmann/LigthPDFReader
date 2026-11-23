namespace LightPdf.Core.Tests;

public class ZoomTests
{
    [Theory]
    [InlineData(1.0, 1.1)]
    [InlineData(1.05, 1.1)]   // between steps -> next step up
    [InlineData(0.05, 0.1)]
    [InlineData(8.0, 8.0)]    // capped
    public void StepIn(double from, double expected) => Assert.Equal(expected, Zoom.StepIn(from));

    [Theory]
    [InlineData(1.0, 0.9)]
    [InlineData(1.05, 1.0)]
    [InlineData(0.1, 0.1)]    // capped
    public void StepOut(double from, double expected) => Assert.Equal(expected, Zoom.StepOut(from));

    [Fact]
    public void FitWidth_FillsAvailableWidth()
    {
        // 612 pt * 96/72 = 816 DIP at 100%.
        Assert.Equal(1.0, Zoom.FitWidth(816, 612), precision: 6);
        Assert.Equal(2.0, Zoom.FitWidth(1632, 612), precision: 6);
    }

    [Fact]
    public void FitPage_UsesTighterDimension()
    {
        // Letter page at 100% = 816 x 1056 DIP. Height is the constraint here.
        Assert.Equal(0.5, Zoom.FitPage(2000, 528, new PageSize(612, 792)), precision: 6);
    }

    [Fact]
    public void Fit_IsClampedToLimits()
    {
        Assert.Equal(Zoom.Max, Zoom.FitWidth(1_000_000, 612));
        Assert.Equal(Zoom.Min, Zoom.FitWidth(1, 612));
    }
}
