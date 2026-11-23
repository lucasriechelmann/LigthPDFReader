using System.Diagnostics.CodeAnalysis;
using Avalonia.Media.Imaging;

namespace LightPdf.App;

/// <summary>
/// Rendered page bitmaps with least-recently-used eviction, bounded by memory (not count),
/// since one page at 400% can weigh as much as dozens of pages at fit-width.
/// </summary>
internal sealed class PageCache
{
    private sealed record Entry(int Page, WriteableBitmap Bitmap, long Bytes);

    private readonly Dictionary<int, LinkedListNode<Entry>> _map = [];
    private readonly LinkedList<Entry> _lru = new(); // most recently used first

    public long TotalBytes { get; private set; }

    public bool TryGet(int page, [NotNullWhen(true)] out WriteableBitmap? bitmap)
    {
        if (_map.TryGetValue(page, out var node))
        {
            _lru.Remove(node);
            _lru.AddFirst(node);
            bitmap = node.Value.Bitmap;
            return true;
        }
        bitmap = null;
        return false;
    }

    /// <summary>Adds or replaces a page's bitmap. The cache takes ownership.</summary>
    public void Put(int page, WriteableBitmap bitmap)
    {
        Remove(page);
        long bytes = (long)bitmap.PixelSize.Width * bitmap.PixelSize.Height * 4;
        _map[page] = _lru.AddFirst(new Entry(page, bitmap, bytes));
        TotalBytes += bytes;
    }

    /// <summary>Evicts least-recently-used pages until under budget, never touching [keepFirst, keepLast].</summary>
    public void Trim(long budgetBytes, int keepFirst, int keepLast)
    {
        var node = _lru.Last;
        while (node is not null && TotalBytes > budgetBytes)
        {
            var previous = node.Previous;
            if (node.Value.Page < keepFirst || node.Value.Page > keepLast)
                Remove(node.Value.Page);
            node = previous;
        }
    }

    public void Clear()
    {
        foreach (var entry in _lru) entry.Bitmap.Dispose();
        _lru.Clear();
        _map.Clear();
        TotalBytes = 0;
    }

    private void Remove(int page)
    {
        if (!_map.Remove(page, out var node)) return;
        _lru.Remove(node);
        TotalBytes -= node.Value.Bytes;
        node.Value.Bitmap.Dispose();
    }
}
