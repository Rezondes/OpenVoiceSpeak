using OVS.Client.Exceptions;
using OVS.Client.Audio.Codec;
using OVS.Client.Audio.Devices;

namespace OVS.Client.Audio.Rust;

/// <summary>
/// Package 173: the speaker of the native library. The device plays from a native ring on a native render thread
/// (MMCSS "Pro Audio"), so what is buffered keeps playing while managed code stands (a garbage collection). The
/// playback thread writes frames and asks for a failure (A181); a close from another thread waits, without a lock
/// (N30), until a write in progress has left the native code.
/// </summary>
internal sealed unsafe class NativeSpeaker : IPlaybackBuffer
{
    readonly NativeAudio native;
    readonly nint speaker;
    readonly Action<Exception>? failed;
    int users; // calls inside the native speaker right now
    int closed;
    bool failureReported; // playback thread only

    NativeSpeaker(NativeAudio native, nint speaker, Action<Exception>? failed) => (this.native, this.speaker, this.failed) = (native, speaker, failed);

    /// <summary>
    /// Opens the device <paramref name="deviceId"/> (null: the default device) and starts it. Throws when the library
    /// is unusable (A180: no fallback) or the device cannot open (an <see cref="AudioDeviceException"/> with its HRESULT).
    /// </summary>
    /// <param name="native">The loaded library.</param>
    /// <param name="deviceId">The MMDevice id; null opens the default device of the console role.</param>
    /// <param name="failed">Told once when the device stops with an error, on the playback thread that asked.</param>
    public static NativeSpeaker Open(NativeAudio native, string? deviceId, Action<Exception>? failed)
    {
        if (native.Error is { } unusable) throw new InvalidOperationException(unusable);
        int code = 0;
        nint speaker;
        fixed (char* id = deviceId) speaker = native.SpeakerOpen(id, &code);
        if (speaker == 0) throw new AudioDeviceException(native.LastError(), code);
        return new(native, speaker, failed);
    }

    /// <summary>A speaker without a device, for tests: rendered by <see cref="FakeRender"/>, failed by <see cref="FakeFail"/>.</summary>
    internal static NativeSpeaker OpenFake(NativeAudio native, Action<Exception>? failed)
    {
        if (native.Error is { } unusable) throw new InvalidOperationException(unusable);
        return new(native, native.SpeakerOpenFake(), failed);
    }

    /// <inheritdoc/>
    public TimeSpan BufferedDuration
    {
        get
        {
            if (!Enter()) return TimeSpan.Zero;
            try
            {
                return TimeSpan.FromSeconds(native.SpeakerBufferedFrames(speaker) / (double)AudioFormat.SampleRate);
            }
            finally
            {
                Leave();
            }
        }
    }

    /// <summary>Silent frames played inside the audio because none were written in time.</summary>
    public long UnderrunFrames
    {
        get
        {
            if (!Enter()) return 0;
            try
            {
                return (long)native.SpeakerUnderrunFrames(speaker);
            }
            finally
            {
                Leave();
            }
        }
    }

    /// <summary>The MMCSS task index of the render thread; 0 when it is not registered.</summary>
    public uint MmcssTask
    {
        get
        {
            if (!Enter()) return 0;
            try
            {
                return native.SpeakerMmcssTask(speaker);
            }
            finally
            {
                Leave();
            }
        }
    }

    /// <inheritdoc/>
    public void AddSamples(ReadOnlySpan<float> frame)
    {
        if (!Enter()) return;
        try
        {
            fixed (float* samples = frame) native.SpeakerWrite(speaker, samples, (uint)(frame.Length / 2));
        }
        finally
        {
            Leave();
        }
    }

    /// <inheritdoc/>
    public void PollFailure()
    {
        if (failureReported || !Enter()) return;
        int code;
        try
        {
            code = native.SpeakerError(speaker);
        }
        finally
        {
            Leave();
        }
        if (code == 0) return;
        failureReported = true;
        failed?.Invoke(new AudioDeviceException($"the speaker stopped (HRESULT 0x{code:X8})", code));
    }

    /// <summary>Test seam: fills <paramref name="out"/> (interleaved stereo) as the device would.</summary>
    internal void FakeRender(Span<float> @out)
    {
        if (!Enter()) return;
        try
        {
            fixed (float* samples = @out) native.SpeakerFakeRender(speaker, samples, (uint)(@out.Length / 2));
        }
        finally
        {
            Leave();
        }
    }

    /// <summary>Test seam: ends the fake's stream with <paramref name="code"/>.</summary>
    internal void FakeFail(int code)
    {
        if (!Enter()) return;
        try
        {
            native.SpeakerFakeFail(speaker, code);
        }
        finally
        {
            Leave();
        }
    }

    /// <summary>Stops the device; waits for a call in progress on another thread, then frees the native speaker.</summary>
    public void Dispose()
    {
        if (Interlocked.Exchange(ref closed, 1) == 1) return;
        var spin = new SpinWait();
        while (Volatile.Read(ref users) != 0) spin.SpinOnce();
        native.SpeakerClose(speaker);
    }

    bool Enter()
    {
        Interlocked.Increment(ref users);
        if (Volatile.Read(ref closed) == 0) return true;
        Interlocked.Decrement(ref users);
        return false;
    }

    void Leave() => Interlocked.Decrement(ref users);
}
