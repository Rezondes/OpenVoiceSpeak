using System.Diagnostics;
using OVS.Client.Audio.Codec;
using OVS.Client.Audio.Processing;
using OVS.Client.Audio.Rust;

namespace OVS.Client.Audio.Playback;

/// <summary>
/// One jitter buffer per speaker, summed into one 20 ms frame per tick. Thread-safe. Package 176: the mixing runs in the
/// native library (native/ovs_audio, <c>core/mixer.rs</c>), on the speaker's render thread when a native speaker plays
/// it, else through <see cref="Tick"/> on the playback loop's own clock (no device, the tests). This is its managed face:
/// the per-speaker volumes and the output volume are turned into final gains here (<see cref="VolumeCurve"/>, the voice
/// boost), packets go in from any thread, and what it reports comes from its snapshot.
/// </summary>
internal sealed unsafe class VoiceMixer
{
    /// <summary>
    /// Package 124: a speaker keeps the jitter buffer and decoder over talk pauses (they were made anew after 500 ms
    /// for every talk spurt). Dropped after a minute of mixing without a word from them, or when they leave.
    /// </summary>
    public const int IdleTicksUntilRemoved = 3000;

    /// <summary>Package 50 (A51): others arrived much too quietly, so every voice is played twice as loud (+6 dB).</summary>
    public const float DefaultVoiceBoost = 2f;

    readonly NativeAudio native = NativeAudio.Load();
    readonly NativeObjectHandle? mixer; // null without the native library (A180): nothing plays
    readonly object gate = new(); // the gains
    Dictionary<uint, float> speakerGains = [];
    float volume = 1f;
    // Package 124: reused by every tick; only the playback thread ticks. Package 142: interleaved stereo.
    readonly float[] mixed = new float[AudioFormat.StereoFrameSamples];
    readonly List<ActiveSpeaker> active = new(NativeMixerSnapshot.Pool);

    /// <summary>A mixer without speakers, at the default gains.</summary>
    public VoiceMixer()
    {
        if (native.Error is not null) return;
        mixer = NativeObjectHandle.Create(native, native.MixerCreate, native.MixerFree);
        lock (gate) SendGains();
    }

    /// <summary>Package 176: the native mixer a native speaker plays; 0 without the library.</summary>
    internal nint Handle => mixer?.Pointer ?? 0;

    /// <summary>Package 51: the volume per speaker (session id), 1 for everyone not listed; Package 128: slider values (A137).</summary>
    public void SetSpeakerGains(IReadOnlyDictionary<uint, float> gains)
    {
        lock (gate)
        {
            speakerGains = new Dictionary<uint, float>(gains);
            SendGains();
        }
    }

    /// <summary>The gain of speakerId as set by the user.</summary>
    public float SpeakerGain(uint speakerId)
    {
        lock (gate) return speakerGains.GetValueOrDefault(speakerId, 1f);
    }

    /// <summary>The output volume as the slider value; Package 128: through <see cref="VolumeCurve"/> like the per-speaker volume.</summary>
    public float Volume
    {
        get { lock (gate) return volume; }
        set
        {
            lock (gate)
            {
                volume = value;
                SendGains();
            }
        }
    }

    /// <summary>Package 176: the final gain of every listed speaker and of everyone else, as the native mixer multiplies them. Under gate.</summary>
    void SendGains()
    {
        if (mixer is null) return;
        uint* ids = stackalloc uint[NativeMixerSnapshot.Pool];
        float* gains = stackalloc float[NativeMixerSnapshot.Pool];
        int count = 0;
        foreach (var (id, gain) in speakerGains)
        {
            if (count == NativeMixerSnapshot.Pool) break;
            ids[count] = id;
            gains[count++] = VolumeCurve.Combine(gain, volume) * DefaultVoiceBoost; // Package 128
        }
        native.MixerSetGains(mixer.Pointer, ids, gains, (uint)count, VolumeCurve.Combine(1f, volume) * DefaultVoiceBoost);
    }

    /// <param name="arrivalMs">Package 133: when it arrived (tests); by default read in real time, as it comes.</param>
    /// <param name="copy">Package 136: the frame before it, if it came along.</param>
    /// <param name="speakerId">The sender's session id.</param>
    /// <param name="seq">The frame number.</param>
    /// <param name="opus">The Opus frame.</param>
    /// <param name="viaLink">It came in over a link.</param>
    public void Push(uint speakerId, uint seq, byte[] opus, bool viaLink, double? arrivalMs = null, byte[]? copy = null)
    {
        if (mixer is null) return; // Package 175 (A180): without the native library nothing plays
        double at = arrivalMs ?? ReadArrivalMs();
        fixed (byte* data = opus)
        fixed (byte* copied = copy)
            native.MixerPush(mixer.Pointer, speakerId, seq, data, (uint)opus.Length, viaLink ? (byte)1 : (byte)0, at, copied, (uint)(copy?.Length ?? 0));
    }

    /// <summary>A frame's arrival in ms: it came over the real network, so it is measured in real time; one timestamp read, nothing allocated.</summary>
    static double ReadArrivalMs() => Stopwatch.GetTimestamp() * 1000.0 / Stopwatch.Frequency; // clock: real time

    NativeMixerSnapshot Snapshot(bool settle)
    {
        NativeMixerSnapshot snapshot = default;
        if (mixer is null) return snapshot;
        if (settle) native.MixerSettle(mixer.Pointer);
        native.MixerSnapshot(mixer.Pointer, &snapshot);
        return snapshot;
    }

    /// <summary>
    /// Package 133 (A142): for the connection details, every 2 s on the UI thread. The jitter is the worst one of the speakers
    /// still kept (Package 124: up to a minute after their last word), so it shows the last one measured.
    /// </summary>
    public ReceiveStats Stats()
    {
        var stats = Snapshot(settle: true).Stats;
        return new ReceiveStats(stats.Received, stats.Lost, stats.Rebuilt, stats.Concealed, stats.JitterMs, stats.DelayMs, stats.Recovered);
    }

    /// <returns>The frame (Package 142: interleaved stereo) and who is in it, both reused by the next tick (Package 124:
    /// nothing allocated per frame).</returns>
    public MixedFrame Tick()
    {
        if (mixer is null)
        {
            Array.Clear(mixed);
            active.Clear();
            return new MixedFrame(mixed, active);
        }
        fixed (float* frame = mixed) native.MixerMix(mixer.Pointer, frame);
        return new MixedFrame(mixed, ReadActive());
    }

    /// <summary>Package 176: who was heard in the last frame the native mixer mixed (also on the render thread), into the reused list.</summary>
    public List<ActiveSpeaker> ReadActive()
    {
        var snapshot = Snapshot(settle: false);
        active.Clear();
        for (int index = 0; index < snapshot.ActiveCount; index++) active.Add(new ActiveSpeaker(snapshot.ActiveIds[index], snapshot.ActiveLinks[index] != 0));
        return active;
    }

    /// <summary>Package 176: the voices' level in the last frame mixed, in dB.</summary>
    public float LastLevelDb => mixer is null ? -120f : Snapshot(settle: false).LevelDb;

    /// <summary>Package 124: something to play or to conceal; without it the playback loop sleeps.</summary>
    public bool HasWork => Snapshot(settle: false).Busy != 0;

    /// <summary>A number for the buffer speakerId has now, new for every buffer made; null when there is none.</summary>
    internal ulong? BufferIdOf(uint speakerId)
    {
        var snapshot = Snapshot(settle: true);
        for (int index = 0; index < snapshot.SpeakerCount; index++)
            if (snapshot.SpeakerIds[index] == speakerId) return snapshot.SpeakerSerials[index];
        return null;
    }

    /// <summary>Package 124: drops the speakers who left, with their decoders.</summary>
    public void RemoveLeft(Func<uint, bool> isPresent)
    {
        if (mixer is null) return;
        var snapshot = Snapshot(settle: true);
        for (int index = 0; index < snapshot.SpeakerCount; index++)
            if (!isPresent(snapshot.SpeakerIds[index])) native.MixerRemove(mixer.Pointer, snapshot.SpeakerIds[index]);
    }

    /// <summary>The speakers with a buffer.</summary>
    public int SpeakerCount => (int)Snapshot(settle: true).SpeakerCount;

    /// <summary>Drops every speaker's buffer.</summary>
    public void Clear()
    {
        if (mixer is not null) native.MixerClear(mixer.Pointer);
    }

    /// <summary>Package 176: deafened silences the voices, except the own voice in the self test.</summary>
    public void SetFlags(bool deafened, bool selfTest)
    {
        if (mixer is not null) native.MixerSetFlags(mixer.Pointer, deafened ? (byte)1 : (byte)0, selfTest ? (byte)1 : (byte)0);
    }

    /// <summary>Package 176: takes the mono far end a native speaker played (voices and sounds) into <paramref name="samples"/>; how many there were.</summary>
    public int ReadFarEnd(Span<float> samples)
    {
        if (mixer is null) return 0;
        fixed (float* data = samples) return (int)native.MixerReadFarEnd(mixer.Pointer, data, (uint)samples.Length);
    }
}
