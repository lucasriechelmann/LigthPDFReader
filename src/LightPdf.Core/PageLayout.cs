namespace LightPdf.Core;

public readonly record struct LayoutRect(double X, double Y, double Width, double Height)
{
    public double Right => X + Width;
    public double Bottom => Y + Height;
}

/// <summary>
/// Positions pages in a continuous vertical strip, in device-independent units (DIP).
/// Pages are centered horizontally; lookups are O(log n), so huge documents are cheap.
/// </summary>
public sealed class PageLayout
{
    private readonly LayoutRect[] _rects;

    /// <param name="pageSizes">Page sizes in PDF points.</param>
    /// <param name="dipPerPoint">Scale: DIP per PDF point (zoom 100% = 96/72).</param>
    /// <param name="gap">Vertical space between pages, in DIP.</param>
    /// <param name="margin">Space around the whole strip, in DIP.</param>
    public PageLayout(IReadOnlyList<PageSize> pageSizes, double dipPerPoint, double gap, double margin)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(dipPerPoint);
        DipPerPoint = dipPerPoint;

        double maxWidth = 0;
        foreach (var s in pageSizes) maxWidth = Math.Max(maxWidth, s.Width * dipPerPoint);
        ExtentWidth = maxWidth + 2 * margin;

        _rects = new LayoutRect[pageSizes.Count];
        double y = margin;
        for (int i = 0; i < _rects.Length; i++)
        {
            double w = pageSizes[i].Width * dipPerPoint;
            double h = pageSizes[i].Height * dipPerPoint;
            _rects[i] = new LayoutRect((ExtentWidth - w) / 2, y, w, h);
            y += h + (i < _rects.Length - 1 ? gap : 0);
        }
        ExtentHeight = y + margin;
    }

    public double DipPerPoint { get; }
    public double ExtentWidth { get; }
    public double ExtentHeight { get; }
    public int PageCount => _rects.Length;

    public LayoutRect GetPageRect(int pageIndex) => _rects[pageIndex];

    /// <summary>
    /// Pages intersecting [top, bottom]. Returns (first, last) inclusive;
    /// first &gt; last when nothing is visible.
    /// </summary>
    public (int First, int Last) GetVisibleRange(double top, double bottom)
    {
        if (_rects.Length == 0) return (0, -1);
        int first = FirstWithBottomAtOrAfter(top);
        int last = LastWithTopAtOrBefore(bottom);
        return (first, last);
    }

    /// <summary>The page at vertical position <paramref name="y"/>, or the nearest one (gaps snap to the page above).</summary>
    public int PageAt(double y)
    {
        if (_rects.Length == 0) return -1;
        return Math.Clamp(LastWithTopAtOrBefore(y), 0, _rects.Length - 1);
    }

    private int FirstWithBottomAtOrAfter(double y)
    {
        int lo = 0, hi = _rects.Length; // answer in [0, n]
        while (lo < hi)
        {
            int mid = (lo + hi) >>> 1;
            if (_rects[mid].Bottom >= y) hi = mid; else lo = mid + 1;
        }
        return lo;
    }

    private int LastWithTopAtOrBefore(double y)
    {
        int lo = -1, hi = _rects.Length - 1; // answer in [-1, n-1]
        while (lo < hi)
        {
            int mid = (lo + hi + 1) >>> 1;
            if (_rects[mid].Y <= y) lo = mid; else hi = mid - 1;
        }
        return lo;
    }
}
