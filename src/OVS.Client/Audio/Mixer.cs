namespace OVS.Client.Audio;

public readonly record struct ActiveSpeaker(uint SessionId, bool ViaLink);

/// <summary>Package 133 (A142): the jitter buffers' counts summed over the speakers, the worst jitter and delay of those present.
/// Package 136: Recovered are the frames played in full from a copy in the next packet.</summary>
public readonly record struct ReceiveStats(int Received, int Lost, int Rebuilt, int Concealed, double JitterMs, int DelayMs, int Recovered = 0);

/// <summary>One jitter buffer per speaker, summed into one 20 ms frame per tick. Thread-safe.</summary>
public sealed class Mixer
{
    /// <summary>
    /// Package 124: a speaker keeps the jitter buffer and decoder over talk pauses (they were made anew after 500 ms
    /// for every talk spurt). Dropped after a minute of mixing without a word from them, or when they leave.
    /// </summary>
    public const int IdleTicksUntilRemoved = 3000;

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
    Dictionary<uint, float> speakerGains = [];
    // Package 124: reused by every tick; only the playback thread ticks. Package 142: interleaved stereo.
    readonly float[] mixed = new float[AudioFormat.StereoFrameSamples];
    readonly List<ActiveSpeaker> active = [];

    /// <summary>Package 51: the volume per speaker (session id), 1 for everyone not listed; Package 128: slider values (A137).</summary>
    public void SetSpeakerGains(IReadOnlyDictionary<uint, float> gains)
    {
        lock (gate) speakerGains = new Dictionary<uint, float>(gains);
    }

    public float SpeakerGain(uint speakerId)
    {
        lock (gate) return speakerGains.GetValueOrDefault(speakerId, 1f);
    }

    /// <summary>The output volume as the slider value; Package 128: through <see cref="VolumeCurve"/> like the per-speaker volume.</summary>
    public float Volume { get; set; } = 1f;

    /// <param name="arrivalMs">Package 133: when it arrived (tests); now by default, taken before waiting for the lock.</param>
    /// <param name="copy">Package 136: the frame before it, if it came along.</param>
    public void Push(uint speakerId, uint seq, byte[] opus, bool viaLink, double? arrivalMs = null, byte[]? copy = null)
    {
        double at = arrivalMs ?? JitterBuffer.NowMs();
        lock (gate)
        {
            if (!speakers.TryGetValue(speakerId, out var speaker)) speakers[speakerId] = speaker = new Speaker();
            speaker.Buffer.Push(seq, opus, viaLink, at, copy);
        }
    }

    ReceiveStats retired; // Package 133: the counts of the speakers removed so far

    /// <summary>Under gate.</summary>
    void Retire(JitterBuffer b) =>
        retired = new ReceiveStats(retired.Received + b.Received, retired.Lost + b.Lost, retired.Rebuilt + b.Rebuilt, retired.Concealed + b.Concealed, 0, 0, retired.Recovered + b.Recovered);

    /// <summary>
    /// Package 133 (A142): for the connection details, every 2 s on the UI thread. The jitter is the worst one of the speakers
    /// still kept (Package 124: up to a minute after their last word), so it shows the last one measured.
    /// </summary>
    public ReceiveStats Stats()
    {
        lock (gate)
        {
            var sum = retired;
            foreach (var speaker in speakers.Values)
            {
                var b = speaker.Buffer;
                sum = new ReceiveStats(sum.Received + b.Received, sum.Lost + b.Lost, sum.Rebuilt + b.Rebuilt, sum.Concealed + b.Concealed,
                    Math.Max(sum.JitterMs, b.JitterMs), Math.Max(sum.DelayMs, b.DelayMs), sum.Recovered + b.Recovered);
            }
            return sum;
        }
    }

    /// <returns>The frame (Package 142: interleaved stereo) and who is in it, both reused by the next tick (Package 124:
    /// nothing allocated per frame).</returns>
    public (float[] Frame, List<ActiveSpeaker> Active) Tick()
    {
        Array.Clear(mixed);
        active.Clear();
        float output = Volume;
        lock (gate)
        {
            foreach (var (id, speaker) in speakers) // removing the current entry while enumerating is allowed
            {
                var pcm = speaker.Buffer.Pull();
                if (pcm is null)
                {
                    if (++speaker.IdleTicks >= IdleTicksUntilRemoved && speaker.Buffer.Buffered == 0)
                    {
                        Retire(speaker.Buffer);
                        speakers.Remove(id);
                    }
                    continue;
                }
                speaker.IdleTicks = 0;
                active.Add(new ActiveSpeaker(id, speaker.Buffer.ViaLink));
                float own = VolumeCurve.Combine(speakerGains.GetValueOrDefault(id, 1f), output) * DefaultVoiceBoost; // Package 128
                for (int i = 0; i < mixed.Length; i++) mixed[i] += pcm[i] * own;
            }
        }
        limiter.Process(mixed); // only Tick uses it, and Tick runs on the playback thread alone
        return (mixed, active);
    }

    /// <summary>Package 124: something to play or to conceal; without it the playback loop sleeps.</summary>
    public bool HasWork
    {
        get
        {
            lock (gate)
            {
                foreach (var speaker in speakers.Values)
                    if (speaker.Buffer.Busy) return true;
                return false;
            }
        }
    }

    internal JitterBuffer? BufferOf(uint speakerId)
    {
        lock (gate) return speakers.GetValueOrDefault(speakerId)?.Buffer;
    }

    /// <summary>Package 124: drops the speakers who left, with their decoders.</summary>
    public void RemoveLeft(Func<uint, bool> isPresent)
    {
        lock (gate)
            foreach (var (id, speaker) in speakers)
                if (!isPresent(id))
                {
                    Retire(speaker.Buffer);
                    speakers.Remove(id);
                }
    }

    public int SpeakerCount
    {
        get { lock (gate) return speakers.Count; }
    }

    public void Clear()
    {
        lock (gate)
        {
            foreach (var speaker in speakers.Values) Retire(speaker.Buffer);
            speakers.Clear();
        }
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
