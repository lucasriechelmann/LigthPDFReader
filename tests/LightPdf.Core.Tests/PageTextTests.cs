namespace LightPdf.Core.Tests;

[Collection(PdfiumCollection.Name)]
public class PageTextTests
{
    private static PageText Extract(string[] lines, int rotate = 0)
    {
        using var doc = PdfDocument.Open(TestPdf.WriteToTemp(textLines: lines, rotate: rotate));
        return doc.GetPageText(0);
    }

    [Fact]
    public void GetPageText_ReturnsTextWithOneBoxPerChar()
    {
        var text = Extract(["Hello World"]);

        Assert.Equal("Hello World", text.Text.Trim());
        Assert.False(text.GetCharBox(0).IsEmpty);
    }

    [Fact]
    public void CharBoxes_AreNormalizedTopLeftCoordinates()
    {
        var text = Extract(["Hello"]);
        NormRect h = text.GetCharBox(text.Text.IndexOf('H'));

        // Text starts at x = 72pt of 612; baseline at y = 700pt from the bottom = 92pt from the top of 792.
        Assert.InRange(h.X, 72 / 612.0 - 0.005, 72 / 612.0 + 0.005);
        double baselineFromTop = 92 / 792.0;
        Assert.True(h.Y < baselineFromTop && h.Bottom > baselineFromTop, $"box {h} should straddle the baseline");
    }

    [Fact]
    public void LinesAreSeparatedByLineBreaks()
    {
        var text = Extract(["first line", "second line"]);
        Assert.Matches(@"first line\s+second line", text.Text);
    }

    [Fact]
    public void HitTest_InsideBox_ReturnsThatChar()
    {
        var text = Extract(["Hello World"]);
        int w = text.Text.IndexOf('W');
        NormRect box = text.GetCharBox(w);

        Assert.Equal(w, text.HitTest(box.X + box.Width / 2, box.Y + box.Height / 2));
    }

    [Fact]
    public void HitTest_PastEndOfLine_ReturnsLastCharOfThatLine()
    {
        var text = Extract(["short", "a much longer second line"]);
        NormRect t = text.GetCharBox(text.Text.IndexOf('t')); // last char of "short"

        Assert.Equal(text.Text.IndexOf('t'), text.HitTest(0.95, t.Y + t.Height / 2));
    }

    [Fact]
    public void HitTest_NoText_ReturnsMinusOne()
        => Assert.Equal(-1, Extract([]).HitTest(0.5, 0.5));

    [Fact]
    public void RangeRects_MergePerLine()
    {
        var text = Extract(["Hello World", "Second"]);

        Assert.Single(text.GetRangeRects(0, 5));                // "Hello"
        Assert.Equal(2, text.GetRangeRects(0, text.Length).Count); // both lines
    }

    [Fact]
    public void RotatedPage_BoxesFollowDisplayOrientation()
    {
        // On a page rotated 90°, a horizontal word is displayed vertically: its highlight is tall and thin.
        var text = Extract(["Horizontal"], rotate: 90);
        NormRect word = Assert.Single(text.GetRangeRects(0, "Horizontal".Length));

        double displayedWidth = word.Width * 792, displayedHeight = word.Height * 612; // page is landscape now
        Assert.True(displayedHeight > displayedWidth, $"expected a vertical run, got {word}");
        Assert.InRange(word.X, 0, 1);
        Assert.InRange(word.Bottom, 0, 1);
    }
}

[Collection(PdfiumCollection.Name)]
public class TextSearchTests
{
    [Fact]
    public void FindAll_IgnoresCaseByDefault()
    {
        var hits = TextSearch.FindAll("Printer printer PRINTER", "printer", matchCase: false);
        Assert.Equal([new TextRange(0, 7), new TextRange(8, 7), new TextRange(16, 7)], hits);
    }

    [Fact]
    public void FindAll_MatchCase()
    {
        var hits = TextSearch.FindAll("Printer printer PRINTER", "printer", matchCase: true);
        Assert.Equal([new TextRange(8, 7)], hits);
    }

    [Fact]
    public void FindAll_MatchesAcrossLineBreaks()
    {
        const string text = "install the printer\r\ndriver first";
        var hit = Assert.Single(TextSearch.FindAll(text, "printer driver", matchCase: false));

        Assert.Equal("printer\r\ndriver", text.Substring(hit.Start, hit.Length));
    }

    [Fact]
    public void FindAll_QueryWhitespaceIsFlexible()
    {
        Assert.Single(TextSearch.FindAll("a b", "  a    b ", matchCase: false));
    }

    [Fact]
    public void FindAll_NonOverlapping()
    {
        Assert.Equal(2, TextSearch.FindAll("aaaa", "aa", matchCase: false).Count);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void FindAll_EmptyQuery_FindsNothing(string query)
        => Assert.Empty(TextSearch.FindAll("anything", query, matchCase: false));

    [Fact]
    public void FindAll_OnExtractedPdfText()
    {
        using var doc = PdfDocument.Open(TestPdf.WriteToTemp(textLines: ["Find the needle here", "and the NEEDLE there"]));
        var text = doc.GetPageText(0);

        var hits = TextSearch.FindAll(text.Text, "needle", matchCase: false);

        Assert.Equal(2, hits.Count);
        Assert.All(hits, h => Assert.Single(text.GetRangeRects(h.Start, h.Length)));
    }
}
