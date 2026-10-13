using OVS.Client.Audio.Codec;

namespace OVS.Client.Audio.Capture;

/// <summary>Turns arbitrary sized capture buffers into exact 960 sample frames (Package 142: 2 x 960 interleaved for stereo).</summary>
internal sealed class FrameChunker(int channels = 1)
{
    int filled;

    /// <summary>Package 124: the one frame buffer, reused; valid until the next <see cref="Take"/>.</summary>
    public float[] Frame { get; } = new float[AudioFormat.FrameSamples * channels];

    /// <summary>Package 142: drops a part frame, when the capture switches between mono and stereo.</summary>
    public void Reset() => filled = 0;

    /// <summary>Takes samples until <see cref="Frame"/> is full; <paramref name="samples"/> keeps the rest.</summary>
    /// <returns>True when a full frame is ready.</returns>
    public bool Take(ref ReadOnlySpan<float> samples)
    {
        if (filled == Frame.Length) filled = 0; // the last full frame has been used
        int take = Math.Min(samples.Length, Frame.Length - filled);
        samples[..take].CopyTo(Frame.AsSpan(filled));
        filled += take;
        samples = samples[take..];
        return filled == Frame.Length;
    }
}
