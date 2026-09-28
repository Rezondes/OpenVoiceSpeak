namespace OVS.Client.Audio;

public readonly record struct ActiveSpeaker(uint SessionId, bool ViaLink);

/// <summary>One jitter buffer per speaker, summed into one 20 ms frame per tick. Thread-safe.</summary>
public sealed class Mixer
{
    public const int IdleTicksUntilRemoved = 25; // 500 ms

    /// <summary>Package 50 (A51): others arrived much too quietly, so every voice is played twice as loud (+6 dB).</summary>
    public const float DefaultVoiceBoost = 2f;

    readonly SoftLimiter limiter = new();

    sealed class Speaker
    {
        public readonly JitterBuffer Buffer = new();
        public int IdleTicks;
    }

    readonly Dictionary<uint, Speaker> speakers = [];
    readonly object gate = new();

    public float Volume { get; set; } = 1f;

    public void Push(uint speakerId, uint seq, byte[] opus, bool viaLink)
    {
        lock (gate)
        {
            if (!speakers.TryGetValue(speakerId, out var speaker)) speakers[speakerId] = speaker = new Speaker();
            speaker.Buffer.Push(seq, opus, viaLink);
        }
    }

    public (float[] Frame, List<ActiveSpeaker> Active) Tick()
    {
        var mixed = new float[AudioFormat.FrameSamples];
        var active = new List<ActiveSpeaker>();
        lock (gate)
        {
            foreach (var (id, speaker) in speakers.ToList())
            {
                var pcm = speaker.Buffer.Pull();
                if (pcm is null)
                {
                    if (++speaker.IdleTicks >= IdleTicksUntilRemoved && speaker.Buffer.Buffered == 0) speakers.Remove(id);
                    continue;
                }
                speaker.IdleTicks = 0;
                active.Add(new ActiveSpeaker(id, speaker.Buffer.ViaLink));
                for (int i = 0; i < mixed.Length; i++) mixed[i] += pcm[i];
            }
        }
        float gain = DefaultVoiceBoost * Volume;
        for (int i = 0; i < mixed.Length; i++) mixed[i] *= gain;
        limiter.Process(mixed); // only Tick uses it, and Tick runs on the playback thread alone
        return (mixed, active);
    }

    public int SpeakerCount
    {
        get { lock (gate) return speakers.Count; }
    }

    public void Clear()
    {
        lock (gate) speakers.Clear();
    }
}

/// <summary>
/// Package 50 (A52): keeps the boosted mix below <see cref="Threshold"/> without cutting the waveform flat. A loud frame
/// is scaled down as a whole at once; afterwards the gain climbs back by <see cref="ReleasePerFrame"/> per frame,
/// ramped sample by sample so it never clicks. Below the threshold the signal is left exactly as it is.
/// </summary>
public sealed class SoftLimiter
{
    public const float Threshold = 0.95f;
    public const float ReleasePerFrame = 0.05f; // from half gain back to full in 200 ms

    public float Gain { get; private set; } = 1f;

    public void Process(Span<float> frame)
    {
        float peak = 0;
        foreach (float s in frame) peak = Math.Max(peak, Math.Abs(s));
        float allowed = peak > Threshold ? Threshold / peak : 1f;
        if (allowed <= Gain)
        {
            Gain = allowed; // attack at once, the frame's peak lands exactly on the threshold
            if (Gain < 1f) for (int i = 0; i < frame.Length; i++) frame[i] *= Gain;
            return;
        }
        float start = Gain, end = Math.Min(allowed, Gain + ReleasePerFrame);
        Gain = end;
        if (start >= 1f) return;
        for (int i = 0; i < frame.Length; i++) frame[i] *= start + (end - start) * (i + 1) / frame.Length;
    }
}
