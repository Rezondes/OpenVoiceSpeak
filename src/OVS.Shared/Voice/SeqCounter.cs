namespace OVS.Shared.Voice;

/// <summary>Per-direction sequence numbers. They are GCM nonces, so they must never wrap.</summary>
public sealed class SeqCounter
{
    long next;

    public uint Next()
    {
        long value = Interlocked.Increment(ref next) - 1;
        if (value > uint.MaxValue) throw new OverflowException("Voice sequence exhausted, reconnect required");
        return (uint)value;
    }

    public void SetNextForTest(uint value) => next = value;
}
