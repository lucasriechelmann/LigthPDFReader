using System.Collections.Concurrent;

namespace LightPdf.Core;

/// <summary>
/// The single thread that owns every PDFium call (PDFium is not thread-safe).
/// Work runs in FIFO order; work whose token is cancelled before it starts is skipped,
/// so stale render requests (pages scrolled past) cost nothing.
/// </summary>
public sealed class PdfWorker : IDisposable
{
    private readonly BlockingCollection<Action> _queue = new();
    private readonly Thread _thread;

    public PdfWorker()
    {
        _thread = new Thread(Loop) { IsBackground = true, Name = "PDFium worker" };
        _thread.Start();
    }

    public bool IsWorkerThread => Thread.CurrentThread == _thread;

    public Task<T> Run<T>(Func<T> work, CancellationToken cancellationToken = default)
    {
        var tcs = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        if (cancellationToken.IsCancellationRequested)
        {
            tcs.SetCanceled(cancellationToken);
            return tcs.Task;
        }

        _queue.Add(() =>
        {
            if (cancellationToken.IsCancellationRequested)
            {
                tcs.TrySetCanceled(cancellationToken);
                return;
            }
            try { tcs.TrySetResult(work()); }
            catch (Exception ex) { tcs.TrySetException(ex); }
        });
        return tcs.Task;
    }

    public Task Run(Action work, CancellationToken cancellationToken = default)
        => Run(() => { work(); return true; }, cancellationToken);

    private void Loop()
    {
        foreach (var item in _queue.GetConsumingEnumerable())
            item();
    }

    /// <summary>Stops accepting work; queued items still run, then the thread exits.</summary>
    public void Dispose() => _queue.CompleteAdding();
}
