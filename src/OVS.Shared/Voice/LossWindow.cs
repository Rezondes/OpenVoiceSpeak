namespace OVS.Shared.Voice;

/// <summary>
/// Package 133 (A142): counts the frames of one or more voice streams (one per speaker) and the gaps in their frame
/// numbers, per 5 s window. A sender's frame number only advances while it talks, so a talk pause is no gap. Not
/// thread-safe; nothing is allocated per frame once a stream is known.
/// </summary>
public sealed class LossWindow(TimeProvider time)
{
    public static readonly TimeSpan Length = TimeSpan.FromSeconds(5);

    /// <summary>
    /// A stream silent this long, or jumping ahead by more than <see cref="MaxGap"/> frames, starts afresh without counting
    /// the gap: the server only relays what routing allows (not while deafened, other channels only by link), so the
    /// frame numbers of what reaches a listener skip without anything being lost. ponytail: a real outage longer than
    /// that goes uncounted.
    /// </summary>
    public static readonly TimeSpan FreshAfter = TimeSpan.FromSeconds(1);
    public const uint MaxGap = 50;

    sealed class Stream
    {
        public uint Highest;
        public ulong Seen; // bit i: frame Highest - i arrived, or lies before the stream's start or this window
        public long LastArrival;
    }

    readonly Dictionary<uint, Stream> streams = [];
    long start = time.GetTimestamp();
    long received, missed;
    LossReport? last;

    public void Count(uint id, uint frame)
    {
        Roll();
        long now = time.GetTimestamp();
        if (!streams.TryGetValue(id, out var s)) streams[id] = s = new Stream();
        else if (time.GetElapsedTime(s.LastArrival, now) <= FreshAfter)
        {
            s.LastArrival = now;
            if (frame > s.Highest && frame - s.Highest <= MaxGap)
            {
                uint gap = frame - s.Highest;
                missed += gap - 1;
                s.Seen = (s.Seen << (int)gap) | 1; // the gap's bits are 0: missing, counted in this window
                s.Highest = frame;
                received++;
                return;
            }
            uint age = s.Highest - frame;
            if (frame <= s.Highest && age < 64)
            {
                ulong bit = 1UL << (int)age;
                if ((s.Seen & bit) != 0) return; // a duplicate, or older than what this window counted
                s.Seen |= bit;
                missed--; // late: its gap was counted in this window, so missed is at least 1
                received++;
                return;
            }
            if (frame <= s.Highest) return; // far too old to place
        }
        // first frame, after a silence, or a jump beyond MaxGap: a fresh start
        s.Highest = frame;
        s.Seen = ulong.MaxValue;
        s.LastArrival = now;
        received++;
    }

    /// <summary>The last complete window, or the current one until the first is complete.</summary>
    public LossReport Report()
    {
        Roll();
        return last ?? Current();
    }

    LossReport Current()
    {
        uint expected = (uint)Math.Min(uint.MaxValue, received + missed);
        return new LossReport(expected, (uint)Math.Min(expected, missed));
    }

    void Roll()
    {
        long now = time.GetTimestamp();
        if (time.GetElapsedTime(start, now) < Length) return;
        last = Current();
        received = missed = 0;
        start = now;
        foreach (var s in streams.Values) s.Seen = ulong.MaxValue; // a late frame does not take back a gap of the last window
    }
}
