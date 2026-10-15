using OVS.Client.Audio.Devices;

namespace OVS.Client.Audio.Sounds;

/// <summary>
/// Tones currently playing, mixed on top of the voice in the playback loop. Several may overlap. Package 174: a speaker
/// that plays the sounds itself (the native one) gets each tone handed over once, from where it has got to; the queue
/// still mixes every tone for the echo canceller's far end and to know when it ends.
/// </summary>
internal sealed class SoundQueue
{
    readonly List<Playing> playing = [];

    /// <summary>The sounds playing.</summary>
    public int Count
    {
        get { lock (playing) return playing.Count; }
    }

    /// <summary>Starts samples at gain, on top of whatever plays.</summary>
    public void Add(float[] samples, float gain) => Add(samples, gain, restart: false);

    void Add(float[] samples, float gain, bool restart)
    {
        if (gain <= 0 || samples.Length == 0) return;
        lock (playing) playing.Add(new Playing(samples, gain, restart));
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

    /// <summary>
    /// Package 174: hands every tone <paramref name="speaker"/> has not got yet over to it, from where the tone has got to,
    /// so it starts with the next frame written; called by the playback loop before it writes that frame. A speaker opened
    /// anew (a device change) gets the tones still playing.
    /// </summary>
    public void HandOver(IPlaybackBuffer speaker)
    {
        lock (playing)
        {
            foreach (var tone in playing)
            {
                if (ReferenceEquals(tone.HandedTo, speaker)) continue;
                speaker.PlaySound(tone.Samples, tone.Gain, tone.Position, tone.Restarts && tone.HandedTo is null);
                tone.HandedTo = speaker;
            }
        }
    }

    /// <summary>Package 159: starts samples anew, stopping the same samples if they still play, so a sound never overlaps itself.</summary>
    public void Restart(float[] samples, float gain)
    {
        lock (playing) playing.RemoveAll(tone => ReferenceEquals(tone.Samples, samples));
        Add(samples, gain, restart: true);
    }

    /// <summary>Stops every sound.</summary>
    public void Clear()
    {
        lock (playing) playing.Clear();
    }

    sealed class Playing(float[] samples, float gain, bool restarts)
    {
        public readonly float[] Samples = samples;
        public readonly float Gain = gain;
        public readonly bool Restarts = restarts; // Package 174: the speaker stops the same sound before it starts this one
        public int Position;
        public IPlaybackBuffer? HandedTo; // Package 174: the speaker that plays it
    }
}
