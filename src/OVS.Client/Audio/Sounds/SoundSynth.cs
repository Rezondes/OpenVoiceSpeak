using OVS.Client.Audio.Codec;

namespace OVS.Client.Audio.Sounds;

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

    /// <summary>Package 73: three quick steps up, for a group given or taken away.</summary>
    static readonly (float Hz, int Ms)[] GroupTone = [(698, 60), (0, 30), (880, 60), (1175, 110)];

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
        [SoundEvent.GroupChanged] = GroupTone,
        [SoundEvent.GroupChangedByMe] = GroupTone, // Package 73: the same by default, each can get its own file
        [SoundEvent.TalkKeyDown] = [(988, 25), (0, 15), (1319, 35)], // Package 131: a short soft click up and down
        [SoundEvent.TalkKeyUp] = [(1319, 25), (0, 15), (988, 35)],
    };

    /// <summary>Tones that play over speech are softer.</summary>
    static float Level(SoundEvent sound) =>
        sound is SoundEvent.LinkVoice or SoundEvent.OwnLinkVoice or SoundEvent.TalkKeyDown or SoundEvent.TalkKeyUp ? 0.5f : 1f;

    static readonly Dictionary<SoundEvent, float[]> Cache = [];

    /// <summary>48 kHz mono samples, rendered once per event.</summary>
    public static float[] Render(SoundEvent sound)
    {
        lock (Cache)
        {
            if (Cache.TryGetValue(sound, out var done)) return done;
            var samples = new List<float>();
            foreach (var (hz, milliseconds) in Patterns[sound])
            {
                int count = AudioFormat.SampleRate * milliseconds / 1000;
                for (int index = 0; index < count; index++)
                {
                    float envelope = Math.Min(1f, Math.Min(index, count - 1 - index) / (float)FadeSamples);
                    samples.Add(hz == 0 ? 0 : Amplitude * Level(sound) * envelope * MathF.Sin(2 * MathF.PI * hz * index / AudioFormat.SampleRate));
                }
            }
            return Cache[sound] = [.. samples];
        }
    }
}
