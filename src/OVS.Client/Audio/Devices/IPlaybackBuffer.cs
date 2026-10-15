namespace OVS.Client.Audio.Devices;

/// <summary>
/// An open speaker: the buffer the playback loop fills, in the one playback format (interleaved stereo float at
/// <see cref="Codec.AudioFormat.SampleRate"/>), and the device that plays it. Disposing it stops the device; a frame
/// added after that is simply never played. Package 173: the real one is the native library's
/// (<see cref="Rust.NativeSpeaker"/>), the tests' one holds what it is given.
/// </summary>
internal interface IPlaybackBuffer : IDisposable
{
    /// <summary>The audio waiting to be played.</summary>
    TimeSpan BufferedDuration { get; }

    /// <summary>Queues one frame of interleaved stereo samples; what does not fit is discarded.</summary>
    void AddSamples(ReadOnlySpan<float> frame);

    /// <summary>
    /// Package 173 (A181): reports once, through the hardware's <see cref="IAudioHardware.Failed"/>, that the device
    /// stopped with an error (unplugged, invalidated); the playback thread asks on every turn, nothing calls back.
    /// </summary>
    void PollFailure();

    /// <summary>
    /// Package 174: the speaker mixes the sounds itself (the native one); the playback loop then hands each tone over
    /// once and leaves it out of the frames it writes.
    /// </summary>
    bool PlaysSounds { get; }

    /// <summary>Package 174: starts <paramref name="samples"/> (mono) at <paramref name="gain"/> with the next frame written, <paramref name="offset"/> samples in; <paramref name="restart"/> stops the same samples first.</summary>
    void PlaySound(float[] samples, float gain, int offset, bool restart);

    /// <summary>
    /// Package 176: the speaker plays the voices of <paramref name="mixer"/> itself, mixed where the device asks for them;
    /// false when it cannot (the tests' fake), and the playback loop then mixes and writes the frames as before.
    /// </summary>
    bool AttachMixer(Playback.VoiceMixer mixer);
}

