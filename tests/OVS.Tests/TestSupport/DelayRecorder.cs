namespace OVS.Tests.TestSupport;

/// <summary>A clock that records the delay of each timer created on it (a wait by the clock), and otherwise is <c>inner</c>.</summary>
sealed class DelayRecorder(TimeProvider inner) : TimeProvider
{
    readonly List<TimeSpan> delays = [];

    /// <summary>The delays asked for so far, in order.</summary>
    public IReadOnlyList<TimeSpan> Delays
    {
        get
        {
            lock (delays) return [.. delays];
        }
    }

    public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
    {
        lock (delays) delays.Add(dueTime);
        return inner.CreateTimer(callback, state, dueTime, period);
    }

    public override DateTimeOffset GetUtcNow() => inner.GetUtcNow();
    public override long GetTimestamp() => inner.GetTimestamp();
    public override long TimestampFrequency => inner.TimestampFrequency;
}
