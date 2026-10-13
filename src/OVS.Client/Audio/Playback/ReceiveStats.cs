namespace OVS.Client.Audio.Playback;

/// <summary>Package 133 (A142): the jitter buffers' counts summed over the speakers, the worst jitter and delay of those present.
/// Package 136: Recovered are the frames played in full from a copy in the next packet.</summary>
public readonly record struct ReceiveStats(int Received, int Lost, int Rebuilt, int Concealed, double JitterMs, int DelayMs, int Recovered = 0)
{
    /// <summary>The counts of one jitter buffer.</summary>
    public static ReceiveStats Of(JitterBuffer jitterBuffer) =>
        new(jitterBuffer.Received, jitterBuffer.Lost, jitterBuffer.Rebuilt, jitterBuffer.Concealed, jitterBuffer.JitterMs, jitterBuffer.DelayMs, jitterBuffer.Recovered);

    /// <summary>The counts added, the worst jitter and delay of the two.</summary>
    public ReceiveStats Add(ReceiveStats other) =>
        new(Received + other.Received, Lost + other.Lost, Rebuilt + other.Rebuilt, Concealed + other.Concealed,
            Math.Max(JitterMs, other.JitterMs), Math.Max(DelayMs, other.DelayMs), Recovered + other.Recovered);
}
