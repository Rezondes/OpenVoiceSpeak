namespace OVS.Client.Audio;

/// <summary>
/// Reorders one speaker's Opus frames and hides losses. Pull() is called once per 20 ms tick.
/// The sender's sequence only advances while it transmits, so a talk pause shows up as an
/// underrun, never as a gap: after a short concealment we rebuffer and continue at the same seq.
/// </summary>
public sealed class JitterBuffer
{
    public const int StartDelay = 2;   // 40 ms cushion before playback starts
    public const int MaxFrames = 10;   // caps the latency at 200 ms
    const int MaxUnderruns = 2;        // then assume a talk pause and rebuffer

    readonly SortedDictionary<uint, byte[]> frames = [];
    readonly VoiceDecoder decoder = new();
    bool started, playing;
    uint next;
    int waited, underruns;

    public bool ViaLink { get; private set; }
    public int Buffered => frames.Count;

    public void Push(uint seq, byte[] opus, bool viaLink)
    {
        ViaLink = viaLink;
        if (started && seq < next) return; // too late, already played or concealed
        frames[seq] = opus;
        while (frames.Count > MaxFrames)
        {
            var oldest = frames.Keys.First();
            frames.Remove(oldest);
            if (started && oldest >= next) next = oldest + 1;
        }
    }

    /// <returns>20 ms of PCM, or null while buffering or idle.</returns>
    public float[]? Pull()
    {
        if (!playing)
        {
            if (frames.Count == 0) return null;
            if (frames.Count < StartDelay && ++waited < StartDelay) return null;
            var first = frames.Keys.First();
            next = started ? Math.Max(next, first) : first;
            started = playing = true;
            waited = underruns = 0;
        }

        if (frames.Remove(next, out var opus))
        {
            underruns = 0;
            next++;
            return decoder.Decode(opus);
        }
        if (frames.Count > 0)
        {
            // lost packet, later ones already here: the very next one carries an FEC copy of it
            return frames.TryGetValue(++next, out var following) ? decoder.DecodeLost(following) : decoder.Decode([]);
        }
        if (++underruns > MaxUnderruns)
        {
            playing = false;
            return null;
        }
        return decoder.Decode([]);
    }
}
