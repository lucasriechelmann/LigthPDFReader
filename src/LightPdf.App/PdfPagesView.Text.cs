using System.Text;
using Avalonia;
using Avalonia.Input;
using Avalonia.Media;
using LightPdf.Core;

namespace LightPdf.App;

public sealed record SearchMatch(int Page, int Start, int Length, IReadOnlyList<NormRect> Rects);

/// <summary>Text features of the page view: per-page text cache, search highlights, mouse selection.</summary>
public sealed partial class PdfPagesView
{
    private const int TextCacheLimit = 48; // pages; each is a few tens of KB (text + one box per char)

    private static readonly IBrush MatchBrush = new SolidColorBrush(Color.FromArgb(90, 255, 200, 0));
    private static readonly IBrush CurrentMatchBrush = new SolidColorBrush(Color.FromArgb(150, 255, 120, 0));
    private static readonly IBrush SelectionBrush = new SolidColorBrush(Color.FromArgb(80, 0, 120, 215));
    private static readonly Cursor TextCursor = new(StandardCursorType.Ibeam);

    // Text, loaded on demand on the PDFium thread (loaded or still loading).
    private readonly Dictionary<int, Task<PageText>> _text = [];
    private readonly Queue<int> _textOrder = new();

    // Search
    private readonly List<SearchMatch> _matches = [];                        // document order
    private readonly Dictionary<int, List<SearchMatch>> _matchesByPage = []; // for drawing
    private SearchMatch? _currentMatch;
    private CancellationTokenSource? _searchCts;

    // Selection: ends are page points, resolved to characters once that page's text is loaded.
    private SelectionEnd? _anchor, _focus;
    private bool _selecting;

    private readonly record struct SelectionEnd(int Page, double X, double Y, int? Char = null);

    public string SearchQuery { get; private set; } = "";
    public bool SearchMatchCase { get; private set; }
    public bool IsSearching { get; private set; }
    public int MatchCount => _matches.Count;
    public int CurrentMatchNumber => _currentMatch is null ? 0 : _matches.IndexOf(_currentMatch) + 1;

    /// <summary>Search results or progress changed.</summary>
    public event EventHandler? SearchChanged;

    private void ResetTextState()
    {
        CancelSearch();
        _matches.Clear();
        _matchesByPage.Clear();
        _currentMatch = null;
        SearchQuery = "";
        _text.Clear();
        _textOrder.Clear();
        _anchor = _focus = null;
        _selecting = false;
    }

    // ---------------------------------------------------------------- text cache

    private Task<PageText> LoadText(int page)
    {
        if (_text.TryGetValue(page, out var existing)) return existing;
        PdfDocument document = _document!;
        var task = _worker!.Run(() => document.GetPageText(page));
        _text[page] = task;
        _textOrder.Enqueue(page);
        while (_textOrder.Count > TextCacheLimit)
            _text.Remove(_textOrder.Dequeue());
        return task;
    }

    /// <summary>Text if already loaded; otherwise starts loading it and redraws when it arrives.</summary>
    private bool TryGetLoadedText(int page, out PageText text)
    {
        if (_text.TryGetValue(page, out var task) && task.IsCompletedSuccessfully)
        {
            text = task.Result;
            return true;
        }
        if (task is null && _document is not null) _ = LoadThenRedraw(page);
        text = null!;
        return false;
    }

    private async Task LoadThenRedraw(int page)
    {
        try { await LoadText(page); InvalidateVisual(); }
        catch (Exception) { /* page without extractable text: nothing to select or highlight */ }
    }

    // ---------------------------------------------------------------- search

    /// <summary>
    /// Searches every page, starting at the current one, as one PDFium work item per page, so renders
    /// interleave and results appear progressively. The first hit becomes the current match.
    /// </summary>
    public async void StartSearch(string query, bool matchCase)
    {
        CancelSearch();
        var cts = _searchCts = new CancellationTokenSource();
        _matches.Clear();
        _matchesByPage.Clear();
        _currentMatch = null;
        SearchQuery = query.Trim();
        SearchMatchCase = matchCase;
        IsSearching = SearchQuery.Length > 0 && PageCount > 0 && _document is not null;
        InvalidateVisual();
        RaiseSearchChanged();
        if (!IsSearching) return;

        PdfDocument document = _document!;
        string q = SearchQuery;
        int start = CurrentPage;
        try
        {
            for (int k = 0; k < PageCount; k++)
            {
                int page = (start + k) % PageCount;
                List<SearchMatch> hits;
                try
                {
                    hits = await _worker!.Run(() =>
                    {
                        var text = document.GetPageText(page);
                        return TextSearch.FindAll(text.Text, q, matchCase)
                            .Select(r => new SearchMatch(page, r.Start, r.Length, text.GetRangeRects(r.Start, r.Length)))
                            .ToList();
                    }, cts.Token);
                }
                catch (PdfException) { continue; } // one broken page shouldn't stop the search

                if (cts.IsCancellationRequested) return;
                if (hits.Count > 0)
                {
                    int at = _matches.FindIndex(m => m.Page > page);
                    _matches.InsertRange(at < 0 ? _matches.Count : at, hits);
                    _matchesByPage[page] = hits;
                    if (_currentMatch is null)
                    {
                        _currentMatch = hits[0];
                        ScrollToMatch(_currentMatch);
                    }
                    InvalidateVisual();
                }
                RaiseSearchChanged();
            }
        }
        catch (OperationCanceledException) { return; }
        finally
        {
            if (ReferenceEquals(_searchCts, cts))
            {
                IsSearching = false;
                RaiseSearchChanged();
            }
        }
    }

    public void ClearSearch()
    {
        CancelSearch();
        _matches.Clear();
        _matchesByPage.Clear();
        _currentMatch = null;
        SearchQuery = "";
        IsSearching = false;
        InvalidateVisual();
        RaiseSearchChanged();
    }

    /// <summary>Moves to the next (+1) or previous (-1) match, wrapping around.</summary>
    public void GoToMatch(int direction)
    {
        if (_matches.Count == 0) return;
        int index = _currentMatch is null ? -1 : _matches.IndexOf(_currentMatch);
        index = index < 0
            ? (direction > 0 ? 0 : _matches.Count - 1)
            : ((index + direction) % _matches.Count + _matches.Count) % _matches.Count;
        _currentMatch = _matches[index];
        ScrollToMatch(_currentMatch);
        InvalidateVisual();
        RaiseSearchChanged();
    }

    private void CancelSearch()
    {
        _searchCts?.Cancel();
        _searchCts = null;
    }

    /// <summary>Scrolls just enough to show the match, placing it a third of the way down if it was off-screen.</summary>
    private void ScrollToMatch(SearchMatch match)
    {
        if (_layout is null || _scroller is null || match.Rects.Count == 0) return;
        Rect r = ToDip(match.Rects[0], ToRect(PageRect(match.Page)));
        Vector offset = _scroller.Offset;
        Size vp = _scroller.Viewport;

        double x = offset.X, y = offset.Y;
        if (r.Y < y || r.Bottom > y + vp.Height) y = r.Y - vp.Height / 3;
        if (r.X < x || r.Right > x + vp.Width) x = r.X - vp.Width / 3;
        _scroller.Offset = new Vector(Math.Max(0, x), Math.Max(0, y));
    }

    private void RaiseSearchChanged() => SearchChanged?.Invoke(this, EventArgs.Empty);

    // ---------------------------------------------------------------- selection

    public bool HasSelection => TryGetSelection(out _, out _);

    public void ClearSelection()
    {
        _anchor = _focus = null;
        InvalidateVisual();
    }

    /// <summary>Selects all text on the current page.</summary>
    public async void SelectAllOnPage()
    {
        if (_document is null || PageCount == 0) return;
        int page = CurrentPage;
        PageText text;
        try { text = await LoadText(page); }
        catch (Exception) { return; }
        if (text.Length == 0) return;
        _anchor = new SelectionEnd(page, 0, 0, 0);
        _focus = new SelectionEnd(page, 1, 1, text.Length - 1);
        InvalidateVisual();
    }

    /// <summary>The selected text (pages joined by line breaks), or null when nothing is selected.</summary>
    public async Task<string?> GetSelectedTextAsync()
    {
        if (!TryGetSelection(out var start, out var end)) return null;
        var sb = new StringBuilder();
        for (int page = start.Page; page <= end.Page; page++)
        {
            PageText text;
            try { text = await LoadText(page); }
            catch (Exception) { continue; }
            if (text.Length == 0) continue;
            int from = page == start.Page ? start.Char : 0;
            int to = page == end.Page ? end.Char : text.Length - 1;
            if (sb.Length > 0) sb.AppendLine();
            sb.Append(text.GetText(from, to - from + 1).TrimEnd('\r', '\n'));
        }
        return sb.ToString();
    }

    /// <summary>Selection endpoints in document order, as (page, char). False if nothing (or nothing yet) is selected.</summary>
    private bool TryGetSelection(out (int Page, int Char) start, out (int Page, int Char) end)
    {
        start = end = default;
        if (_anchor is not { } a || _focus is not { } f) return false;
        if (Resolve(a) is not { } ac || Resolve(f) is not { } fc) return false;
        var x = (a.Page, Char: ac);
        var y = (f.Page, Char: fc);
        if (x == y && a.Char is null) return false; // a click, not a drag
        (start, end) = (x.Page, x.Char).CompareTo((y.Page, y.Char)) <= 0 ? (x, y) : (y, x);
        return true;
    }

    private int? Resolve(SelectionEnd end)
    {
        if (end.Char is { } c) return c;
        if (!TryGetLoadedText(end.Page, out var text)) return null;
        int hit = text.HitTest(end.X, end.Y);
        return hit < 0 ? null : hit;
    }

    // ---------------------------------------------------------------- pointer

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        var point = e.GetCurrentPoint(this);
        if (!point.Properties.IsLeftButtonPressed || _layout is null) return;

        _scroller?.Focus(); // keep keyboard scrolling/shortcuts working after a click
        if (HitPage(point.Position, clamp: false) is { } hit)
        {
            _anchor = _focus = hit;
            _selecting = true;
            e.Pointer.Capture(this);
            e.Handled = true;
        }
        else
        {
            _anchor = _focus = null;
        }
        InvalidateVisual();
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (_layout is null) return;
        Point p = e.GetPosition(this);
        if (_selecting)
        {
            _focus = HitPage(p, clamp: true);
            InvalidateVisual();
        }
        Cursor = _selecting || HitPage(p, clamp: false) is not null ? TextCursor : Cursor.Default;
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (!_selecting) return;
        _selecting = false;
        e.Pointer.Capture(null);
    }

    /// <summary>The page point under <paramref name="p"/>; with <paramref name="clamp"/>, gaps and margins snap to the nearest page edge.</summary>
    private SelectionEnd? HitPage(Point p, bool clamp)
    {
        if (_layout is null || PageCount == 0) return null;
        int page = _layout.PageAt(p.Y);
        LayoutRect r = PageRect(page);
        double x = (p.X - r.X) / r.Width, y = (p.Y - r.Y) / r.Height;
        bool inside = x is >= 0 and <= 1 && y is >= 0 and <= 1;
        if (!inside && !clamp) return null;
        return new SelectionEnd(page, Math.Clamp(x, 0, 1), Math.Clamp(y, 0, 1));
    }

    // ---------------------------------------------------------------- drawing

    private void DrawTextOverlays(DrawingContext context, int page, Rect dest)
    {
        if (_matchesByPage.TryGetValue(page, out var matches))
            foreach (var m in matches)
                foreach (var r in m.Rects)
                    context.FillRectangle(ReferenceEquals(m, _currentMatch) ? CurrentMatchBrush : MatchBrush, ToDip(r, dest));

        if (TryGetSelection(out var start, out var end) && page >= start.Page && page <= end.Page
            && TryGetLoadedText(page, out var text) && text.Length > 0)
        {
            int from = page == start.Page ? start.Char : 0;
            int to = page == end.Page ? end.Char : text.Length - 1;
            foreach (var r in text.GetRangeRects(from, to - from + 1))
                context.FillRectangle(SelectionBrush, ToDip(r, dest));
        }
    }

    private static Rect ToDip(NormRect r, Rect page)
        => new(page.X + r.X * page.Width, page.Y + r.Y * page.Height, r.Width * page.Width, r.Height * page.Height);

    private static Rect ToRect(LayoutRect r) => new(r.X, r.Y, r.Width, r.Height);
}
