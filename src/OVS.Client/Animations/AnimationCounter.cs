namespace OVS.Client.Animations;

/// <summary>
/// Package 109: a test hook, every animation started from code passes here (<see cref="Motion.PlayAsync"/>,
/// <see cref="FlyGhost.FlyAsync"/>).
/// </summary>
internal static class AnimationCounter
{
    static int started, running;

    /// <summary>Animations started from code since the last <see cref="ResetStarted"/>.</summary>
    internal static int Started => Volatile.Read(ref started);

    /// <summary>Animations started from code that have not ended yet (finished or cancelled).</summary>
    internal static int Running => Volatile.Read(ref running);

    internal static void ResetStarted() => Interlocked.Exchange(ref started, 0);

    /// <summary>Counts an animation that starts and, once its task ends, no longer counts it as running.</summary>
    internal static Task CountAsync(Task run)
    {
        Interlocked.Increment(ref started);
        Interlocked.Increment(ref running);
        run.ContinueWith(_ => Interlocked.Decrement(ref running), CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
        return run;
    }
}
