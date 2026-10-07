using NAudio.Wave;
using OVS.Client.Audio.Codec;

namespace OVS.Client.Audio.Devices;

/// <summary>
/// An open speaker: the buffer the playback loop fills, in the one playback format, and the device that plays it.
/// Disposing it stops the device; the buffer holds no handle, so a frame added after that is simply never played.
/// </summary>
/// <param name="duration">How much audio the buffer holds; what does not fit is discarded.</param>
/// <param name="device">Plays <see cref="Source"/> once started.</param>
public sealed class PlaybackBuffer(TimeSpan duration, IDisposable device) : IDisposable
{
    readonly BufferedWaveProvider buffer = new(WaveFormat.CreateIeeeFloatWaveFormat(AudioFormat.SampleRate, 2)) // Package 142: stereo
    {
        DiscardOnBufferOverflow = true,
        BufferDuration = duration,
    };

    /// <summary>What the device plays.</summary>
    public IWaveProvider Source => buffer;

    public TimeSpan BufferedDuration => buffer.BufferedDuration;

    public void AddSamples(byte[] samples, int offset, int count) => buffer.AddSamples(samples, offset, count);

    public void Dispose() => device.Dispose();
}
