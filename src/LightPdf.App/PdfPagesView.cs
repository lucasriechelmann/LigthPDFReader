using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using Avalonia.VisualTree;
using LightPdf.Core;

namespace LightPdf.App;

public sealed class PageRenderedEventArgs(int pageIndex, WriteableBitmap bitmap) : EventArgs
{
    public int PageIndex { get; } = pageIndex;
    public WriteableBitmap Bitmap { get; } = bitmap;
}

/// <summary>
/// Continuous, virtualized page strip. Lives inside a <see cref="ScrollViewer"/>: it reports the
/// whole document as its size (so scrollbars and wheel scrolling work normally) but only draws
/// and renders the pages near the viewport. All PDFium work goes through the <see cref="PdfWorker"/>.
/// </summary>
public sealed partial class PdfPagesView : Control
{
    private const double Gap = 12;
    private const double StripMargin = 16;
    private const long MaxPixelsPerPage = 16_000_000;           // ~64 MB; beyond this, upscale (blurry) until tiling lands
    // Re-rendering a page takes ~10 ms, so a big cache buys little. 96 MB is ~11 fit-width pages on a
    // 1000-DIP window at 125% scaling; visible pages and their neighbours are never evicted regardless.
    private const long CacheBudgetBytes = 96L * 1024 * 1024;
    private static readonly TimeSpan SettleDelay = TimeSpan.FromMilliseconds(150);

    private static readonly BoxShadows PageShadow =
        new(new BoxShadow { OffsetY = 1, Blur = 8, Color = Color.FromArgb(110, 0, 0, 0) });

    private readonly PageCache _cache = new();
    private readonly Dictionary<int, PendingRender> _pending = [];
    private readonly HashSet<int> _failed = [];
    private readonly DispatcherTimer _settleTimer;

    private PdfWorker? _worker;
    private PdfDocument? _document;
    private PageSize[] _sizes = [];
    private double _maxPageWidth;
    private PageLayout? _layout;
    private ScrollViewer? _scroller;
    private bool _settling;
    private ViewState? _pendingView;
    private int? _restoringPage; // saved page we will scroll to once layout has settled

    private sealed record PendingRender(int Width, int Height, CancellationTokenSource Cts);

    public PdfPagesView()
    {
        _settleTimer = new DispatcherTimer { Interval = SettleDelay };
        _settleTimer.Tick += (_, _) =>
        {
            _settleTimer.Stop();
            _settling = false;
            RequestRenders();
        };
        RenderOptions.SetBitmapInterpolationMode(this, BitmapInterpolationMode.HighQuality);
    }

    /// <summary>Zoom factor; 1.0 = 100% (physical size).</summary>
    public double Zoom { get; private set; } = 1;
    public ZoomMode ZoomMode { get; private set; } = ZoomMode.FitWidth;
    public int PageCount => _sizes.Length;
    public int CurrentPage { get; private set; }

    /// <summary>Zoom, zoom mode, or current page changed.</summary>
    public event EventHandler? ViewChanged;
    public event EventHandler<PageRenderedEventArgs>? PageRendered;

    // ---------------------------------------------------------------- document

    /// <summary>
    /// Shows a document (already opened on <paramref name="worker"/>). The caller keeps ownership.
    /// <paramref name="initialView"/> (page, zoom) is applied as soon as the viewport has a real size,
    /// which for a brand-new tab is only after its first layout pass.
    /// </summary>
    public void SetDocument(PdfWorker worker, PdfDocument document, PageSize[] pageSizes, ViewState? initialView = null)
    {
        ResetRenderState();
        ResetTextState();
        _worker = worker;
        _document = document;
        _sizes = pageSizes;
        _maxPageWidth = pageSizes.Length == 0 ? 0 : pageSizes.Max(s => s.Width);
        _layout = null;
        _pendingView = initialView ?? ViewState.Default;
        TryApplyPendingView();
    }

    /// <summary>Where the reader is now, for saving and restoring.</summary>
    public ViewState GetViewState() =>
        _pendingView ?? new ViewState(_restoringPage ?? CurrentPage, Zoom, ZoomMode);

    private void TryApplyPendingView()
    {
        if (_pendingView is not { } view || _scroller is null || PageCount == 0) return;
        Size vp = _scroller.Viewport;
        if (vp.Width <= 0 || vp.Height <= 0) return; // not laid out yet; retried on the next ScrollChanged
        _pendingView = null;

        CurrentPage = Math.Clamp(view.Page, 0, PageCount - 1);
        ZoomMode = view.Mode;
        Zoom = view.Mode == ZoomMode.Custom ? Core.Zoom.Clamp(view.Zoom) : ComputeFitZoom(view.Mode) ?? 1;
        RebuildLayout();
        RaiseViewChanged();

        if (CurrentPage == 0)
        {
            RequestRenders();
            return;
        }

        // We are usually called from inside a layout pass (ScrollChanged), where UpdateLayout() can't
        // grow the scroll extent yet, so an offset set now would be clamped to 0. Jump once layout is done.
        _restoringPage = CurrentPage;
        PostCompleteRestore();
    }

    /// <summary>
    /// Scrolls to the saved page after the current layout pass. If the view gets hidden first
    /// (another tab selected), the jump stays pending and runs when it is shown again.
    /// </summary>
    private void PostCompleteRestore()
    {
        PdfDocument? document = _document;
        Dispatcher.UIThread.Post(() =>
        {
            if (_restoringPage is not { } page || !ReferenceEquals(document, _document) || _scroller is null) return;
            _restoringPage = null;
            GoToPage(page);
            RequestRenders();
        }, DispatcherPriority.Background);
    }

    public void ClearDocument()
    {
        ResetRenderState();
        ResetTextState();
        _document = null;
        _sizes = [];
        _layout = null;
        InvalidateMeasure();
        InvalidateVisual();
    }

    private void ResetRenderState()
    {
        CancelPending(_ => true);
        _cache.Clear();
        _failed.Clear();
        _settleTimer.Stop();
        _settling = false;
    }

    // ---------------------------------------------------------------- navigation

    public void GoToPage(int pageIndex)
    {
        if (_layout is null || _scroller is null || PageCount == 0) return;
        pageIndex = Math.Clamp(pageIndex, 0, PageCount - 1);
        double y = Math.Max(0, _layout.GetPageRect(pageIndex).Y - Gap);
        _scroller.Offset = new Vector(_scroller.Offset.X, y);
        CurrentPage = pageIndex; // set eagerly: the last page may not reach the viewport center
        RaiseViewChanged();
    }

    // ---------------------------------------------------------------- zoom

    public void ZoomIn() => SetZoom(Core.Zoom.StepIn(Zoom));
    public void ZoomOut() => SetZoom(Core.Zoom.StepOut(Zoom));

    /// <summary>Custom zoom, keeping the document point under <paramref name="anchor"/> (viewport coordinates) in place.</summary>
    public void SetZoom(double zoom, Point? anchor = null) => ApplyZoom(zoom, ZoomMode.Custom, anchor);

    public void SetZoomMode(ZoomMode mode)
    {
        if (mode == ZoomMode.Custom) { ZoomMode = mode; RaiseViewChanged(); return; }
        int page = CurrentPage;
        ApplyZoom(ComputeFitZoom(mode) ?? Zoom, mode, anchor: null);
        if (mode == ZoomMode.FitPage) GoToPage(page);
    }

    private double? ComputeFitZoom(ZoomMode mode)
    {
        if (_scroller is null || PageCount == 0) return null;
        Size vp = _scroller.Viewport;
        if (vp.Width <= 0 || vp.Height <= 0) return null;
        double width = vp.Width - 2 * StripMargin;
        double height = vp.Height - 2 * Gap;
        return mode switch
        {
            ZoomMode.FitWidth => Core.Zoom.FitWidth(width, _maxPageWidth),
            ZoomMode.FitPage => Core.Zoom.FitPage(width, height, _sizes[Math.Clamp(CurrentPage, 0, PageCount - 1)]),
            _ => null,
        };
    }

    private void ApplyZoom(double zoom, ZoomMode mode, Point? anchor)
    {
        zoom = Core.Zoom.Clamp(zoom);
        ZoomMode = mode;
        PageLayout? old = _layout;
        if (_scroller is null || old is null || PageCount == 0)
        {
            Zoom = zoom;
            RebuildLayout();
            RaiseViewChanged();
            return;
        }
        if (Math.Abs(zoom - Zoom) < 1e-9) { RaiseViewChanged(); return; }

        // Remember which point of which page sits under the anchor...
        Size vp = _scroller.Viewport;
        Vector offset = _scroller.Offset;
        Point a = anchor ?? new Point(vp.Width / 2, vp.Height / 2);
        double ax = offset.X + a.X - CenterShift(old, vp);
        double ay = offset.Y + a.Y;
        int page = old.PageAt(ay);
        LayoutRect r = old.GetPageRect(page);
        double fx = (ax - r.X) / r.Width, fy = (ay - r.Y) / r.Height;

        Zoom = zoom;
        RebuildLayout();
        UpdateLayout(); // grow/shrink the scroll extent now, or the new offset gets clamped to the old one

        // ...and scroll so the same point is under the anchor again.
        LayoutRect n = _layout!.GetPageRect(page);
        double nx = n.X + fx * n.Width + CenterShift(_layout, vp) - a.X;
        double ny = n.Y + fy * n.Height - a.Y;
        _scroller.Offset = new Vector(Math.Max(0, nx), Math.Max(0, ny));

        BeginSettle();
        InvalidateVisual();
        RaiseViewChanged();
    }

    /// <summary>Horizontal offset of this control inside the viewport when the document is narrower (centered).</summary>
    private static double CenterShift(PageLayout layout, Size viewport) => Math.Max(0, (viewport.Width - layout.ExtentWidth) / 2);

    /// <summary>During a zoom gesture, stretch cached bitmaps; re-render once the user pauses.</summary>
    private void BeginSettle()
    {
        _settling = true;
        CancelPending(_ => true); // renders at the old scale are now wasted work
        _settleTimer.Stop();
        _settleTimer.Start();
    }

    private void RebuildLayout()
    {
        _layout = PageCount == 0 ? null : new PageLayout(_sizes, Zoom * Core.Zoom.DipPerPointAt100, Gap, StripMargin);
        InvalidateMeasure();
        InvalidateVisual();
    }

    // ---------------------------------------------------------------- scroll viewer hookup

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _scroller = this.FindAncestorOfType<ScrollViewer>();
        if (_scroller is null) return;
        _scroller.ScrollChanged += OnScrollChanged;

        // Back in view (e.g. its tab was selected again): re-render once layout has settled.
        Dispatcher.UIThread.Post(() =>
        {
            TryApplyPendingView();
            InvalidateVisual();
            RequestRenders();
        }, DispatcherPriority.Loaded);
        if (_restoringPage is not null)
            PostCompleteRestore(); // hidden before its saved page was restored
    }

    /// <summary>
    /// Hidden (e.g. its tab is not selected): keep the layout and scroll position, but drop the
    /// bitmaps and pending renders, so background tabs cost almost no memory or PDFium time.
    /// </summary>
    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        ResetRenderState();
        if (_scroller is not null)
        {
            _scroller.ScrollChanged -= OnScrollChanged;
            _scroller = null;
        }
        base.OnDetachedFromVisualTree(e);
    }

    private void OnScrollChanged(object? sender, ScrollChangedEventArgs e)
    {
        TryApplyPendingView();

        if (e.ViewportDelta != default)
        {
            InvalidateMeasure(); // we always cover at least the viewport
            if (ZoomMode != ZoomMode.Custom && ComputeFitZoom(ZoomMode) is { } fit)
                ApplyZoom(fit, ZoomMode, anchor: new Point(_scroller!.Viewport.Width / 2, 0)); // window resized
        }

        InvalidateVisual();
        UpdateCurrentPage();
        if (!_settling) RequestRenders();
    }

    // PointerWheelChanged only bubbles. We sit below the ScrollViewer's content presenter, so we see
    // Ctrl+wheel first and can mark it handled before it becomes a scroll.
    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        if (e.KeyModifiers.HasFlag(KeyModifiers.Control) && _scroller is not null)
        {
            e.Handled = true;
            SetZoom(Zoom * Math.Pow(1.15, e.Delta.Y), e.GetPosition(_scroller));
            return;
        }
        base.OnPointerWheelChanged(e);
    }

    private void UpdateCurrentPage()
    {
        if (_layout is null || _scroller is null || _restoringPage is not null) return;
        Vector offset = _scroller.Offset;
        Size vp = _scroller.Viewport;
        bool atEnd = offset.Y + vp.Height >= _scroller.Extent.Height - 1;
        int page = atEnd ? PageCount - 1 : _layout.PageAt(offset.Y + vp.Height / 2);
        if (page != CurrentPage && page >= 0)
        {
            CurrentPage = page;
            RaiseViewChanged();
        }
    }

    private void RaiseViewChanged() => ViewChanged?.Invoke(this, EventArgs.Empty);

    // ---------------------------------------------------------------- layout & drawing

    /// <summary>
    /// At least the viewport size, so the whole viewport is hit-testable (Ctrl+wheel works over the gray
    /// area too). Pages are centered inside by <see cref="XShift"/>.
    /// </summary>
    protected override Size MeasureOverride(Size availableSize)
    {
        if (_layout is null) return default;
        Size vp = _scroller?.Viewport ?? default;
        return new Size(Math.Max(_layout.ExtentWidth, vp.Width), Math.Max(_layout.ExtentHeight, vp.Height));
    }

    /// <summary>Horizontal offset that centers the page strip when we are wider than it.</summary>
    private double XShift => _layout is null ? 0 : Math.Max(0, (Bounds.Width - _layout.ExtentWidth) / 2);

    private LayoutRect PageRect(int page)
    {
        LayoutRect r = _layout!.GetPageRect(page);
        return r with { X = r.X + XShift };
    }

    private double Scaling => TopLevel.GetTopLevel(this)?.RenderScaling ?? 1;

    private (int First, int Last) VisibleRange()
    {
        if (_layout is null || _scroller is null) return (0, -1);
        double top = _scroller.Offset.Y;
        return _layout.GetVisibleRange(top, top + _scroller.Viewport.Height);
    }

    public override void Render(DrawingContext context)
    {
        if (_layout is null) return;
        context.FillRectangle(Brushes.Transparent, new Rect(Bounds.Size)); // hit-testable everywhere
        double s = Scaling;
        var (first, last) = VisibleRange();
        for (int i = first; i <= last; i++)
        {
            Rect dest = SnapToDevicePixels(PageRect(i), s);
            context.DrawRectangle(Brushes.White, null, new RoundedRect(dest), PageShadow);

            if (_cache.TryGet(i, out var bitmap))
                context.DrawImage(bitmap, dest);
            else if (_failed.Contains(i))
                DrawMessage(context, dest, "This page could not be rendered.");

            DrawTextOverlays(context, i, dest); // search matches and selection
        }
    }

    private static void DrawMessage(DrawingContext context, Rect page, string message)
    {
        var text = new FormattedText(message, CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
            Typeface.Default, 14, Brushes.DimGray);
        context.DrawText(text, new Point(page.X + (page.Width - text.Width) / 2, page.Y + 24));
    }

    /// <summary>Aligns page edges to whole device pixels so a 1:1 bitmap is drawn without resampling blur.</summary>
    private static Rect SnapToDevicePixels(LayoutRect r, double s)
    {
        double left = Math.Round(r.X * s), top = Math.Round(r.Y * s);
        double right = Math.Round(r.Right * s), bottom = Math.Round(r.Bottom * s);
        return new Rect(left / s, top / s, (right - left) / s, (bottom - top) / s);
    }

    private (int Width, int Height) TargetPixels(int page, double s)
    {
        Rect snapped = SnapToDevicePixels(PageRect(page), s); // same rect as Render, so sizes match exactly
        double w = Math.Max(1, Math.Round(snapped.Width * s)), h = Math.Max(1, Math.Round(snapped.Height * s));
        if (w * h > MaxPixelsPerPage)
        {
            double k = Math.Sqrt(MaxPixelsPerPage / (w * h));
            w = Math.Floor(w * k);
            h = Math.Floor(h * k);
        }
        return ((int)w, (int)h);
    }

    // ---------------------------------------------------------------- rendering

    /// <summary>Queues renders for visible pages (nearest the current page first) plus one page of prefetch each way.</summary>
    private void RequestRenders()
    {
        if (_layout is null || _worker is null || _document is null || _restoringPage is not null) return;
        var (first, last) = VisibleRange();
        if (first > last) { CancelPending(_ => true); return; }

        int keepFirst = Math.Max(0, first - 1), keepLast = Math.Min(PageCount - 1, last + 1);
        CancelPending(p => p < keepFirst || p > keepLast);

        double s = Scaling;
        var order = Enumerable.Range(keepFirst, keepLast - keepFirst + 1)
            .OrderBy(p => p < first || p > last ? 1 : 0)          // visible before prefetch
            .ThenBy(p => Math.Abs(p - CurrentPage));
        foreach (int page in order)
        {
            if (_failed.Contains(page)) continue;
            var (w, h) = TargetPixels(page, s);
            if (_cache.TryGet(page, out var cached) && cached.PixelSize.Width == w && cached.PixelSize.Height == h)
                continue;
            if (_pending.TryGetValue(page, out var pending))
            {
                if (pending.Width == w && pending.Height == h) continue;
                pending.Cts.Cancel();
                _pending.Remove(page);
            }
            StartRender(page, w, h, s);
        }
    }

    private async void StartRender(int page, int width, int height, double scaling)
    {
        var cts = new CancellationTokenSource();
        _pending[page] = new PendingRender(width, height, cts);
        PdfDocument document = _document!;
        try
        {
            // Avalonia bitmaps may be created off the UI thread; PDFium writes straight into its memory.
            WriteableBitmap bitmap = await _worker!.Run(() =>
            {
                // 96 DPI on purpose: Size == PixelSize. With DPI = 96*scaling, Avalonia mixes DIP and
                // pixel units in the source rect and draws the page magnified by the scaling factor.
                // The destination rect alone decides the on-screen size.
                var wb = new WriteableBitmap(new PixelSize(width, height), new Vector(96, 96),
                    PixelFormat.Bgra8888, AlphaFormat.Premul);
                using (var fb = wb.Lock())
                    document.RenderPage(page, fb.Address, fb.Size.Width, fb.Size.Height, fb.RowBytes);
                return wb;
            }, cts.Token);

            if (cts.IsCancellationRequested || !ReferenceEquals(document, _document))
            {
                bitmap.Dispose();
                return;
            }
            _cache.Put(page, bitmap);
            var (first, last) = VisibleRange();
            _cache.Trim(CacheBudgetBytes, first - 1, last + 1);
            InvalidateVisual();
            PageRendered?.Invoke(this, new PageRenderedEventArgs(page, bitmap));
        }
        catch (OperationCanceledException) { }
        catch (Exception) when (ReferenceEquals(document, _document))
        {
            _failed.Add(page);
            InvalidateVisual();
        }
        catch (Exception) { /* document was replaced while rendering; ignore */ }
        finally
        {
            if (_pending.TryGetValue(page, out var p) && p.Cts == cts)
                _pending.Remove(page);
            cts.Dispose();
        }
    }

    private void CancelPending(Func<int, bool> predicate)
    {
        foreach (var (page, pending) in _pending.Where(kv => predicate(kv.Key)).ToList())
        {
            pending.Cts.Cancel();
            _pending.Remove(page);
        }
    }
}
