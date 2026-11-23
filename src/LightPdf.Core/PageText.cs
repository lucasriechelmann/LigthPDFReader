namespace LightPdf.Core;

/// <summary>
/// Rectangle in normalized page coordinates: (0,0) is the page's top-left corner as displayed,
/// (1,1) its bottom-right. Independent of zoom, so the UI just multiplies by the page's on-screen size.
/// </summary>
public readonly record struct NormRect(double X, double Y, double Width, double Height)
{
    public double Right => X + Width;
    public double Bottom => Y + Height;
    public bool IsEmpty => Width <= 0 || Height <= 0;

    public static NormRect FromPoints(double x1, double y1, double x2, double y2)
        => new(Math.Min(x1, x2), Math.Min(y1, y2), Math.Abs(x2 - x1), Math.Abs(y2 - y1));

    public NormRect Union(NormRect other)
    {
        double left = Math.Min(X, other.X), top = Math.Min(Y, other.Y);
        return new NormRect(left, top, Math.Max(Right, other.Right) - left, Math.Max(Bottom, other.Bottom) - top);
    }

    /// <summary>Swaps the axes (used to treat vertical lines like horizontal ones).</summary>
    public NormRect Transpose() => new(Y, X, Height, Width); // a method, not a property: record ToString() would recurse
}

/// <summary>A page's text with one box per character (empty for characters PDFium synthesized, like line breaks).</summary>
public sealed class PageText
{
    private readonly NormRect[] _boxes;

    /// <param name="verticalLines">
    /// True when lines run top-to-bottom on screen (page rotated 90° or 270°). Line-based logic
    /// (hit-testing, highlight merging) then works on transposed boxes.
    /// </param>
    public PageText(int pageIndex, string text, NormRect[] boxes, bool verticalLines = false)
    {
        if (text.Length != boxes.Length)
            throw new ArgumentException("Need exactly one box per character.", nameof(boxes));
        PageIndex = pageIndex;
        Text = text;
        _boxes = boxes;
        VerticalLines = verticalLines;
    }

    public int PageIndex { get; }
    public string Text { get; }
    public int Length => Text.Length;
    public bool VerticalLines { get; }

    public NormRect GetCharBox(int index) => _boxes[index];

    // Boxes in "line space": lines always run along X, so one implementation serves both orientations.
    private NormRect LineBox(int i) => VerticalLines ? _boxes[i].Transpose() : _boxes[i];

    /// <summary>
    /// The character nearest to a point, in two steps: the nearest line, then the nearest character along
    /// it. So pointing past the end of a line picks that line's last character, never a longer line nearby.
    /// -1 if the page has no text.
    /// </summary>
    public int HitTest(double x, double y)
    {
        if (VerticalLines) (x, y) = (y, x);

        double minAcross = double.MaxValue;
        for (int i = 0; i < _boxes.Length; i++)
            if (!_boxes[i].IsEmpty) minAcross = Math.Min(minAcross, Distance(y, LineBox(i).Y, LineBox(i).Bottom));
        if (minAcross == double.MaxValue) return -1;

        const double lineTolerance = 1e-4; // same line = (almost) the same distance across lines
        int best = -1;
        double bestAlong = double.MaxValue;
        for (int i = 0; i < _boxes.Length; i++)
        {
            if (_boxes[i].IsEmpty) continue;
            NormRect b = LineBox(i);
            if (Distance(y, b.Y, b.Bottom) > minAcross + lineTolerance) continue;
            double along = Distance(x, b.X, b.Right);
            if (along < bestAlong)
            {
                bestAlong = along;
                best = i;
            }
        }
        return best;
    }

    /// <summary>Highlight rectangles for a character range: one per line, merged from the character boxes.</summary>
    public List<NormRect> GetRangeRects(int start, int length)
    {
        var rects = new List<NormRect>(); // in line space
        int end = Math.Min(Length, start + length);
        for (int i = Math.Max(0, start); i < end; i++)
        {
            if (_boxes[i].IsEmpty) continue;
            NormRect b = LineBox(i);
            if (rects.Count > 0 && SameLine(rects[^1], b))
                rects[^1] = rects[^1].Union(b);
            else
                rects.Add(b);
        }
        if (VerticalLines)
            for (int i = 0; i < rects.Count; i++) rects[i] = rects[i].Transpose();
        return rects;
    }

    public string GetText(int start, int length)
    {
        start = Math.Clamp(start, 0, Length);
        return Text.Substring(start, Math.Clamp(length, 0, Length - start));
    }

    private static double Distance(double v, double from, double to) => v < from ? from - v : v > to ? v - to : 0;

    /// <summary>Same line when the boxes overlap across the line direction by at least half the thinner one.</summary>
    private static bool SameLine(NormRect a, NormRect b)
    {
        double overlap = Math.Min(a.Bottom, b.Bottom) - Math.Max(a.Y, b.Y);
        return overlap >= 0.5 * Math.Min(a.Height, b.Height);
    }
}
