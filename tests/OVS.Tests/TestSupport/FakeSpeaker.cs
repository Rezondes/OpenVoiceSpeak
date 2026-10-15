using OVS.Client.Audio.Codec;
using OVS.Client.Audio.Devices;

namespace OVS.Tests.TestSupport;

/// <summary>
/// Package 173: a speaker nothing plays: it holds what it is given up to <paramref name="capacity"/> and discards the
/// rest, like the native ring did with nobody reading. <see cref="Fail"/> stops it as an unplugged device, reported once on the next poll.
/// </summary>
internal sealed class FakeSpeaker(TimeSpan capacity, Action<Exception>? failed = null) : IPlaybackBuffer
{
    readonly long room = (long)(capacity.TotalSeconds * AudioFormat.SampleRate);
    long frames;
    volatile Exception? failure;
    int reported;

    /// <summary>The speaker was closed.</summary>
    public volatile bool Disposed;

    public TimeSpan BufferedDuration => TimeSpan.FromSeconds(Interlocked.Read(ref frames) / (double)AudioFormat.SampleRate);

    public void AddSamples(ReadOnlySpan<float> frame) => Interlocked.Add(ref frames, Math.Min(room - Interlocked.Read(ref frames), frame.Length / 2));

    public void PollFailure()
    {
        if (failure is { } error && Interlocked.Exchange(ref reported, 1) == 0) failed?.Invoke(error);
    }

    /// <summary>The device stops with an error, as unplugged.</summary>
    public void Fail() => failure = new InvalidOperationException("device invalidated");

    /// <summary>Package 174: whether it plays the sounds itself, as the native speaker; then the tones handed over are recorded.</summary>
    public bool PlaysSounds { get; init; }

    /// <summary>Package 174: the tones handed over, in order.</summary>
    public List<PlayedSound> Played { get; } = [];

    public void PlaySound(float[] samples, float gain, int offset, bool restart)
    {
        lock (Played) Played.Add(new PlayedSound(samples, gain, offset, restart));
    }

    public void Dispose() => Disposed = true;
}
