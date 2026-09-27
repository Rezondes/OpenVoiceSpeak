namespace OVS.Client.Audio;

public readonly record struct ActiveSpeaker(uint SessionId, bool ViaLink);

/// <summary>One jitter buffer per speaker, summed into one 20 ms frame per tick. Thread-safe.</summary>
public sealed class Mixer
{
    public const int IdleTicksUntilRemoved = 25; // 500 ms

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
        float volume = Volume;
        for (int i = 0; i < mixed.Length; i++) mixed[i] = Math.Clamp(mixed[i] * volume, -1f, 1f);
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
