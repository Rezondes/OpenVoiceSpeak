using System.Collections.Concurrent;

namespace OVS.Tests.TestSupport;

/// <summary>A single "UI thread" with its own SynchronizationContext, standing in for Avalonia's dispatcher.</summary>
public sealed class TestDispatcher : IDisposable
{
    readonly BlockingCollection<Action> queue = new();
    readonly Thread thread;

    public TestDispatcher()
    {
        thread = new Thread(() =>
        {
            SynchronizationContext.SetSynchronizationContext(new Context(this));
            foreach (var action in queue.GetConsumingEnumerable()) action();
        }) { IsBackground = true, Name = "TestUi" };
        thread.Start();
    }

    public void Post(Action action)
    {
        try
        {
            queue.Add(action);
        }
        catch (InvalidOperationException)
        {
            // disposed: late network callbacks are dropped
        }
    }

    public Task<T> InvokeAsync<T>(Func<Task<T>> work)
    {
        var done = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        Post(async () =>
        {
            try
            {
                done.SetResult(await work());
            }
            catch (Exception e)
            {
                done.SetException(e);
            }
        });
        return done.Task;
    }

    /// <summary>Reads a value on the "UI thread".</summary>
    public Task<T> ReadAsync<T>(Func<T> read) => InvokeAsync(() => Task.FromResult(read()));

    /// <summary>Runs <paramref name="work"/> on the "UI thread"; its exception reaches the caller.</summary>
    public Task RunAsync(Func<Task> work) => InvokeAsync<object?>(async () =>
    {
        await work();
        return null;
    });

    /// <summary>
    /// Reads on the "UI thread" until the value fits, at most 10 s by the clock (not by a loop count: network, audio and
    /// pool threads may come late on a busy runner). Returns the last value, so the caller's assert names it.
    /// </summary>
    public async Task<T> ReadUntilAsync<T>(Func<T> read, Func<T, bool> done)
    {
        var watch = System.Diagnostics.Stopwatch.StartNew();
        while (true)
        {
            var value = await ReadAsync(read);
            if (done(value) || watch.ElapsedMilliseconds > 10_000) return value;
            await Task.Delay(20);
        }
    }

    public void Dispose() => queue.CompleteAdding();

    sealed class Context(TestDispatcher dispatcher) : SynchronizationContext
    {
        public override void Post(SendOrPostCallback callback, object? state) => dispatcher.Post(() => callback(state));
        public override void Send(SendOrPostCallback callback, object? state) => throw new NotSupportedException();
    }
}
