namespace OVS.Shared.Voice;

/// <summary>Per-direction sequence numbers. They are GCM nonces, so they must never wrap.</summary>
public sealed class SeqCounter
{
    long next;

    /// <summary>The next sequence number; throws once all 2^32 are used.</summary>
    public uint Next()
    {
        long value = Interlocked.Increment(ref next) - 1;
        if (value > uint.MaxValue) throw new OverflowException("Voice sequence exhausted, reconnect required");
        return (uint)value;
    }

    /// <summary>Lets a test start near the end of the range.</summary>
    public void SetNextForTest(uint value) => next = value;
}
