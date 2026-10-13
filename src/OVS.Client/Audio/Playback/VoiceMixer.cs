using System.Diagnostics;
using OVS.Client.Audio.Codec;
using OVS.Client.Audio.Processing;

namespace OVS.Client.Audio.Playback;

/// <summary>One jitter buffer per speaker, summed into one 20 ms frame per tick. Thread-safe.</summary>
public sealed class VoiceMixer
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

    /// <param name="arrivalMs">Package 133: when it arrived (tests); by default read in real time, before waiting for the lock.</param>
    /// <param name="copy">Package 136: the frame before it, if it came along.</param>
    public void Push(uint speakerId, uint seq, byte[] opus, bool viaLink, double? arrivalMs = null, byte[]? copy = null)
    {
        double at = arrivalMs ?? ReadArrivalMs();
        lock (gate)
        {
            if (!speakers.TryGetValue(speakerId, out var speaker)) speakers[speakerId] = speaker = new Speaker();
            speaker.Buffer.Push(seq, opus, viaLink, at, copy);
        }
    }

    /// <summary>A frame's arrival in ms: it came over the real network, so it is measured in real time; one timestamp read, nothing allocated.</summary>
    static double ReadArrivalMs() => Stopwatch.GetTimestamp() * 1000.0 / Stopwatch.Frequency; // clock: real time

    ReceiveStats retired; // Package 133: the counts of the speakers removed so far

    /// <summary>Under gate.</summary>
    void Retire(JitterBuffer jitterBuffer) =>
        retired = retired.Add(ReceiveStats.Of(jitterBuffer)) with { JitterMs = 0, DelayMs = 0 }; // a gone speaker's jitter is not shown

    /// <summary>
    /// Package 133 (A142): for the connection details, every 2 s on the UI thread. The jitter is the worst one of the speakers
    /// still kept (Package 124: up to a minute after their last word), so it shows the last one measured.
    /// </summary>
    public ReceiveStats Stats()
    {
        lock (gate)
        {
            var sum = retired;
            foreach (var speaker in speakers.Values) sum = sum.Add(ReceiveStats.Of(speaker.Buffer));
            return sum;
        }
    }

    /// <returns>The frame (Package 142: interleaved stereo) and who is in it, both reused by the next tick (Package 124:
    /// nothing allocated per frame).</returns>
    public MixedFrame Tick()
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
                for (int index = 0; index < mixed.Length; index++) mixed[index] += pcm[index] * own;
            }
        }
        limiter.Process(mixed); // only Tick uses it, and Tick runs on the playback thread alone
        return new MixedFrame(mixed, active);
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
