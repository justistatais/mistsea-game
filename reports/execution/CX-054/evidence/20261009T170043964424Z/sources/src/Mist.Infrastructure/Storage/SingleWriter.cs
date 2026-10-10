using System.Collections.Concurrent;

namespace Mist.Infrastructure.Storage;

public sealed class SingleWriter : IDisposable
{
    private readonly BlockingCollection<Action> _queue = new(256);
    private readonly Thread _thread;
    internal Action? BeforeEach {get;set;}
    internal Func<IDisposable>? Synchronize {get;set;}
    public SingleWriter()
    {
        _thread = new Thread(() => { foreach (var work in _queue.GetConsumingEnumerable()) work(); }) { IsBackground = true, Name = "MistSea single writer" };
        _thread.Start();
    }
    public ValueTask<T> Enqueue<T>(Func<T> operation, CancellationToken cancellationToken = default)
    {
        var completion = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        Action work = () =>
        {
            if (cancellationToken.IsCancellationRequested) { completion.TrySetCanceled(cancellationToken); return; }
            try { using var lease=Synchronize?.Invoke();BeforeEach?.Invoke();completion.TrySetResult(operation()); }
            catch (Exception error) { completion.TrySetException(error); }
        };
        if (!_queue.TryAdd(work)) completion.TrySetException(new InvalidOperationException("Write queue full; request was not committed."));
        return new(completion.Task);
    }
    public void Dispose()
    {
        _queue.CompleteAdding();
        if (!_thread.Join(TimeSpan.FromSeconds(10))) throw new TimeoutException("Writer did not finish safely.");
        _queue.Dispose();
    }
}
