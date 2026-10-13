namespace OVS.Tests.TestSupport;

/// <summary>
/// The "post to the UI thread" of a test: collects what is posted instead of running it, so a test decides when the UI
/// thread has its turn. <see cref="RunAsync"/> lets a manual clock pass and runs what was posted.
/// </summary>
sealed class PostQueue
{
    readonly List<Action> posted = [];
    TaskCompletionSource signal = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public void Post(Action action)
    {
        TaskCompletionSource current;
        lock (posted)
        {
            posted.Add(action);
            current = signal;
        }
        current.TrySetResult();
    }

    /// <summary>Forgets what was posted so far and waits for the next post only (what came before is not what the test waits for).</summary>
    public void Clear()
    {
        lock (posted)
        {
            posted.Clear();
            signal = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        }
    }

    /// <summary>
    /// Moves <paramref name="clock"/> on by <paramref name="delay"/>, waits (bounded) for <paramref name="posts"/> posts and
    /// runs everything posted.
    /// </summary>
    public async Task RunAsync(ManualTimeProvider clock, TimeSpan delay, int posts = 1)
    {
        Task wait;
        lock (posted) wait = signal.Task;
        clock.Advance(delay);
        await HangGuard.Bound(wait);
        if (posts > 1) await AsyncWait.Until(() => { lock (posted) return posted.Count >= posts; });
        Action[] due;
        lock (posted) due = [.. posted];
        foreach (var action in due) action();
        lock (posted)
        {
            posted.RemoveRange(0, due.Length); // run once: a second run does not repeat them
            signal = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        }
    }
}
