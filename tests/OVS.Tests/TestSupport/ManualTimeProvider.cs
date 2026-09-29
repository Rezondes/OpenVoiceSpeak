namespace OVS.Tests.TestSupport;

/// <summary>Time moves only on Advance; timers (Task.Delay with this provider) fire during Advance.</summary>
public sealed class ManualTimeProvider(DateTimeOffset start) : TimeProvider
{
    readonly object gate = new();
    readonly List<Timer> timers = [];
    DateTimeOffset now = start;

    public ManualTimeProvider() : this(new DateTimeOffset(2026, 1, 1, 12, 0, 0, TimeSpan.Zero)) { }

    public override DateTimeOffset GetUtcNow()
    {
        lock (gate) return now;
    }

    public override long GetTimestamp() => GetUtcNow().UtcTicks;
    public override long TimestampFrequency => TimeSpan.TicksPerSecond;

    public void Advance(TimeSpan by)
    {
        List<Timer> due;
        lock (gate)
        {
            now += by;
            due = timers.Where(t => t.Due <= now).ToList();
            foreach (var t in due)
            {
                if (t.Period > TimeSpan.Zero) t.Due = now + t.Period;
                else timers.Remove(t);
            }
        }
        foreach (var t in due) t.Callback(t.State);
    }

    public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
    {
        var timer = new Timer(this, callback, state);
        timer.Change(dueTime, period);
        return timer;
    }

    sealed class Timer(ManualTimeProvider owner, TimerCallback callback, object? state) : ITimer
    {
        public TimerCallback Callback => callback;
        public object? State => state;
        public DateTimeOffset Due;
        public TimeSpan Period;

        public bool Change(TimeSpan dueTime, TimeSpan period)
        {
            lock (owner.gate)
            {
                owner.timers.Remove(this);
                if (dueTime == Timeout.InfiniteTimeSpan) return true;
                Due = owner.now + dueTime;
                Period = period == Timeout.InfiniteTimeSpan ? TimeSpan.Zero : period;
                owner.timers.Add(this);
            }
            return true;
        }

        public void Dispose()
        {
            lock (owner.gate) owner.timers.Remove(this);
        }

        public ValueTask DisposeAsync()
        {
            Dispose();
            return ValueTask.CompletedTask;
        }
    }
}
