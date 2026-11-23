namespace LightPdf.Core.Tests;

public class PdfWorkerTests
{
    [Fact]
    public async Task Run_ExecutesAllWorkOnOneDedicatedThread()
    {
        using var worker = new PdfWorker();
        int callerThread = Environment.CurrentManagedThreadId;

        int[] threads = await Task.WhenAll(Enumerable.Range(0, 20)
            .Select(_ => worker.Run(() => Environment.CurrentManagedThreadId)));

        Assert.Single(threads.Distinct());
        Assert.NotEqual(callerThread, threads[0]);
    }

    [Fact]
    public async Task Run_PreservesOrder()
    {
        using var worker = new PdfWorker();
        var order = new List<int>();

        await Task.WhenAll(Enumerable.Range(0, 50).Select(i => worker.Run(() => order.Add(i))));

        Assert.Equal(Enumerable.Range(0, 50), order);
    }

    [Fact]
    public async Task Run_CancelledBeforeStart_SkipsWork()
    {
        using var worker = new PdfWorker();
        using var gate = new ManualResetEventSlim();
        using var cts = new CancellationTokenSource();
        bool ran = false;

        var blocker = worker.Run(() => gate.Wait());          // occupy the thread
        var skipped = worker.Run(() => ran = true, cts.Token); // queued behind it
        cts.Cancel();
        gate.Set();

        await blocker;
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => skipped);
        Assert.False(ran);
    }

    [Fact]
    public async Task Run_PropagatesExceptions_AndKeepsWorking()
    {
        using var worker = new PdfWorker();

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => worker.Run(() => throw new InvalidOperationException("boom")));

        Assert.Equal(42, await worker.Run(() => 42)); // thread survived
    }
}
