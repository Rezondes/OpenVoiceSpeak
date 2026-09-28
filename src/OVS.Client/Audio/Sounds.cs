namespace OVS.Client.Audio;

/// <summary>Package 47 (A46): the moments the client gives an acoustic cue for.</summary>
public enum SoundEvent
{
    MicOff, MicOn, SoundOff, SoundOn,
    Connected, Disconnected,
    ChannelEntered, UserJoined, UserLeft,
    ServerMuted, Moved, PrivateMessage,
    ServerMessage, ChannelMessage, // Package 56
    LinkVoice, // Package 57
    OwnLinkVoice, // Package 60
}

/// <summary>
/// The default tones, made in code: no files and no licences. Each is a short sequence of soft sine notes
/// (0 Hz is a pause); rising means "on" or "arrived", falling means "off" or "gone".
/// </summary>
public static class SoundSynth
{
    const float Amplitude = 0.28f;
    const int FadeSamples = AudioFormat.SampleRate / 200; // 5 ms, so no note clicks

    static readonly (float Hz, int Ms)[] ChatTone = [(1047, 60), (0, 40), (1319, 90)];

    /// <summary>Package 57: a short, soft double blip over the voice.</summary>
    static readonly (float Hz, int Ms)[] LinkTone = [(1397, 30), (0, 20), (1760, 40)];

    static readonly Dictionary<SoundEvent, (float Hz, int Ms)[]> Patterns = new()
    {
        [SoundEvent.MicOff] = [(660, 70), (440, 90)],
        [SoundEvent.MicOn] = [(440, 70), (660, 90)],
        [SoundEvent.SoundOff] = [(523, 70), (392, 70), (262, 110)],
        [SoundEvent.SoundOn] = [(262, 70), (392, 70), (523, 110)],
        [SoundEvent.Connected] = [(523, 90), (659, 90), (784, 150)],
        [SoundEvent.Disconnected] = [(784, 90), (659, 90), (523, 150)],
        [SoundEvent.ChannelEntered] = [(587, 80), (880, 130)],
        [SoundEvent.UserJoined] = [(740, 130)],
        [SoundEvent.UserLeft] = [(494, 130)],
        [SoundEvent.ServerMuted] = [(330, 120), (0, 60), (330, 120)],
        [SoundEvent.Moved] = [(880, 70), (587, 110)],
        [SoundEvent.PrivateMessage] = ChatTone,
        [SoundEvent.ServerMessage] = ChatTone, // Package 56: the same tone by default, each can get its own file
        [SoundEvent.ChannelMessage] = ChatTone,
        [SoundEvent.LinkVoice] = LinkTone,
        [SoundEvent.OwnLinkVoice] = LinkTone, // Package 60: the same by default, each can get its own file
    };

    /// <summary>Tones that play over speech are softer.</summary>
    static float Level(SoundEvent sound) => sound is SoundEvent.LinkVoice or SoundEvent.OwnLinkVoice ? 0.5f : 1f;

    static readonly Dictionary<SoundEvent, float[]> Cache = [];

    /// <summary>48 kHz mono samples, rendered once per event.</summary>
    public static float[] Render(SoundEvent sound)
    {
        lock (Cache)
        {
            if (Cache.TryGetValue(sound, out var done)) return done;
            var samples = new List<float>();
            foreach (var (hz, ms) in Patterns[sound])
            {
                int count = AudioFormat.SampleRate * ms / 1000;
                for (int i = 0; i < count; i++)
                {
                    float envelope = Math.Min(1f, Math.Min(i, count - 1 - i) / (float)FadeSamples);
                    samples.Add(hz == 0 ? 0 : Amplitude * Level(sound) * envelope * MathF.Sin(2 * MathF.PI * hz * i / AudioFormat.SampleRate));
                }
            }
            return Cache[sound] = [.. samples];
        }
    }
}

/// <summary>Tones currently playing, mixed on top of the voice in the playback loop. Several may overlap.</summary>
public sealed class SoundQueue
{
    sealed class Playing(float[] samples, float gain)
    {
        public readonly float[] Samples = samples;
        public readonly float Gain = gain;
        public int Position;
    }

    readonly List<Playing> playing = [];

    public int Count
    {
        get { lock (playing) return playing.Count; }
    }

    public void Play(float[] samples, float gain)
    {
        if (gain <= 0 || samples.Length == 0) return;
        lock (playing) playing.Add(new Playing(samples, gain));
    }

    /// <summary>Adds the next piece of every tone to the frame and keeps the sum within -1..1.</summary>
    public void MixInto(Span<float> frame)
    {
        lock (playing)
        {
            if (playing.Count == 0) return;
            foreach (var tone in playing)
            {
                int n = Math.Min(frame.Length, tone.Samples.Length - tone.Position);
                for (int i = 0; i < n; i++) frame[i] += tone.Samples[tone.Position + i] * tone.Gain;
                tone.Position += n;
            }
            playing.RemoveAll(t => t.Position >= t.Samples.Length);
        }
        for (int i = 0; i < frame.Length; i++) frame[i] = Math.Clamp(frame[i], -1f, 1f);
    }

    public void Clear()
    {
        lock (playing) playing.Clear();
    }
}
