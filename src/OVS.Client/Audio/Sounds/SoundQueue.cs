namespace OVS.Client.Audio.Sounds;

/// <summary>Tones currently playing, mixed on top of the voice in the playback loop. Several may overlap.</summary>
public sealed class SoundQueue
{
    readonly List<Playing> playing = [];

    public int Count
    {
        get { lock (playing) return playing.Count; }
    }

    public void Add(float[] samples, float gain)
    {
        if (gain <= 0 || samples.Length == 0) return;
        lock (playing) playing.Add(new Playing(samples, gain));
    }

    /// <summary>
    /// Adds the next piece of every tone to the frame and keeps the sum within -1..1. Package 142: the frame is interleaved
    /// stereo, the tones (mono) play on both sides.
    /// </summary>
    public void MixInto(Span<float> frame)
    {
        lock (playing)
        {
            if (playing.Count == 0) return;
            foreach (var tone in playing)
            {
                int count = Math.Min(frame.Length / 2, tone.Samples.Length - tone.Position);
                for (int index = 0; index < count; index++)
                {
                    float sample = tone.Samples[tone.Position + index] * tone.Gain;
                    frame[2 * index] += sample;
                    frame[2 * index + 1] += sample;
                }
                tone.Position += count;
            }
            playing.RemoveAll(playing => playing.Position >= playing.Samples.Length);
        }
        for (int index = 0; index < frame.Length; index++) frame[index] = Math.Clamp(frame[index], -1f, 1f);
    }

    public void Clear()
    {
        lock (playing) playing.Clear();
    }

    sealed class Playing(float[] samples, float gain)
    {
        public readonly float[] Samples = samples;
        public readonly float Gain = gain;
        public int Position;
    }
}
