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

    public void Dispose() => queue.CompleteAdding();

    sealed class Context(TestDispatcher dispatcher) : SynchronizationContext
    {
        public override void Post(SendOrPostCallback callback, object? state) => dispatcher.Post(() => callback(state));
        public override void Send(SendOrPostCallback callback, object? state) => throw new NotSupportedException();
    }
}
