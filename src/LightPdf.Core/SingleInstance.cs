using System.IO.Pipes;
using System.Text;

namespace LightPdf.Core;

/// <summary>
/// One running reader per user session. The first process owns a named mutex and listens on a named
/// pipe; later processes send it their file paths and exit. The pipe is restricted to the current user.
/// </summary>
public sealed class SingleInstance : IDisposable
{
    private const int MaxMessageBytes = 1 << 20; // plenty for file paths; refuses anything silly

    private readonly Mutex _mutex;
    private readonly string _pipeName;
    private readonly CancellationTokenSource _cts = new();

    private SingleInstance(Mutex mutex, string pipeName)
    {
        _mutex = mutex;
        _pipeName = pipeName;
    }

    /// <summary>An id unique to this user's session, e.g. "LightPdf-1".</summary>
    public static string DefaultId
    {
        get
        {
            using var self = System.Diagnostics.Process.GetCurrentProcess();
            return $"LightPdf-{self.SessionId}";
        }
    }

    /// <summary>Becomes the primary instance, or returns null if another process already is.</summary>
    public static SingleInstance? TryAcquire(string id)
    {
        var mutex = new Mutex(initiallyOwned: true, @"Local\" + id, out bool createdNew);
        if (!createdNew)
        {
            mutex.Dispose();
            return null;
        }
        return new SingleInstance(mutex, id);
    }

    /// <summary>
    /// Starts accepting messages on a background thread. <paramref name="onMessage"/> runs on that thread
    /// with the paths another instance sent (possibly none: "just show yourself").
    /// </summary>
    public void Listen(Action<IReadOnlyList<string>> onMessage)
    {
        CancellationToken token = _cts.Token;
        _ = Task.Run(async () =>
        {
            while (!token.IsCancellationRequested)
            {
                try
                {
                    await using var server = new NamedPipeServerStream(_pipeName, PipeDirection.In, 1,
                        PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                    await server.WaitForConnectionAsync(token).ConfigureAwait(false);
                    var message = await ReadMessageAsync(server, token).ConfigureAwait(false);
                    if (message is not null) onMessage(message);
                }
                catch (OperationCanceledException) { return; }
                catch (IOException) { /* client went away mid-message: wait for the next one */ }
            }
        }, token);
    }

    /// <summary>Sends paths to the primary instance. False if none answered within <paramref name="timeout"/>.</summary>
    public static bool TrySend(string id, IReadOnlyList<string> paths, TimeSpan timeout)
    {
        try
        {
            using var client = new NamedPipeClientStream(".", id, PipeDirection.Out, PipeOptions.CurrentUserOnly);
            client.Connect((int)timeout.TotalMilliseconds); // waits for the pipe to appear, too
            byte[] data = Encoding.UTF8.GetBytes(string.Join('\n', paths));
            client.Write(data);
            client.Flush();
            return true;
        }
        catch (Exception ex) when (ex is TimeoutException or IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static async Task<IReadOnlyList<string>?> ReadMessageAsync(Stream stream, CancellationToken token)
    {
        using var buffer = new MemoryStream();
        var chunk = new byte[4096];
        int read;
        while ((read = await stream.ReadAsync(chunk, token).ConfigureAwait(false)) > 0)
        {
            if (buffer.Length + read > MaxMessageBytes) return null;
            buffer.Write(chunk, 0, read);
        }
        string text = Encoding.UTF8.GetString(buffer.GetBuffer(), 0, (int)buffer.Length);
        return text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }

    public void Dispose()
    {
        _cts.Cancel();
        try { _mutex.ReleaseMutex(); }
        catch (ApplicationException) { /* not owned by this thread: disposing still frees it */ }
        _mutex.Dispose();
        _cts.Dispose();
    }
}
