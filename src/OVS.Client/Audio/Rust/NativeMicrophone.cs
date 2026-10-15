using Microsoft.Win32.SafeHandles;
using OVS.Client.Exceptions;

namespace OVS.Client.Audio.Rust;

/// <summary>
/// Package 177: the microphone of the native library. A native capture thread (MMCSS "Pro Audio") fills a ring of one
/// second, so the capture pipeline can stand for a garbage collection without losing audio; it waits on
/// <see cref="Ready"/> (A181: nothing calls back) and takes the samples. A close from another thread waits, without a
/// lock (N30), until a read in progress has left the native code.
/// </summary>
internal sealed unsafe class NativeMicrophone : IDisposable
{
    readonly NativeAudio native;
    readonly nint microphone;
    readonly NativeEvent ready;
    int users;
    int closed;

    NativeMicrophone(NativeAudio native, nint microphone)
    {
        this.native = native;
        this.microphone = microphone;
        Channels = (int)native.MicChannels(microphone);
        ready = new NativeEvent(native.MicEvent(microphone));
    }

    /// <summary>Opens the microphone <paramref name="deviceId"/> (null: the default one) and starts it; throws when the library is unusable or the device cannot open.</summary>
    /// <param name="native">The loaded library.</param>
    /// <param name="deviceId">The MMDevice id; null opens the default device of the console role.</param>
    public static NativeMicrophone Open(NativeAudio native, string? deviceId)
    {
        if (native.Error is { } unusable) throw new InvalidOperationException(unusable);
        int code = 0;
        nint microphone;
        fixed (char* id = deviceId) microphone = native.MicOpen(id, &code);
        if (microphone == 0) throw new AudioDeviceException(native.LastError(), code);
        return new(native, microphone);
    }

    /// <summary>A microphone without a device, for tests: written by <see cref="FakeWrite"/>.</summary>
    internal static NativeMicrophone OpenFake(NativeAudio native, int channels)
    {
        if (native.Error is { } unusable) throw new InvalidOperationException(unusable);
        return new(native, native.MicOpenFake((uint)channels));
    }

    /// <summary>The samples per frame (the device's channel count), 48 kHz float interleaved.</summary>
    public int Channels { get; }

    /// <summary>Signalled after each round of the device (and when it fails).</summary>
    public WaitHandle Ready => ready;

    /// <summary>The HRESULT that ended the stream, 0 while it runs.</summary>
    public int ErrorCode
    {
        get
        {
            if (!Enter()) return 0;
            try
            {
                return native.MicError(microphone);
            }
            finally
            {
                Leave();
            }
        }
    }

    /// <summary>Takes whole frames into <paramref name="samples"/>; how many samples it took.</summary>
    public int Read(Span<float> samples)
    {
        if (!Enter()) return 0;
        try
        {
            fixed (float* data = samples) return (int)native.MicRead(microphone, data, (uint)samples.Length);
        }
        finally
        {
            Leave();
        }
    }

    /// <summary>Test seam: queues samples as the device would.</summary>
    internal void FakeWrite(ReadOnlySpan<float> samples)
    {
        if (!Enter()) return;
        try
        {
            fixed (float* data = samples) native.MicFakeWrite(microphone, data, (uint)samples.Length);
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
            native.MicFakeFail(microphone, code);
        }
        finally
        {
            Leave();
        }
    }

    /// <summary>Stops the device; waits for a read in progress on another thread, then frees the native microphone.</summary>
    public void Dispose()
    {
        if (Interlocked.Exchange(ref closed, 1) == 1) return;
        var spin = new SpinWait();
        while (Volatile.Read(ref users) != 0) spin.SpinOnce();
        native.MicClose(microphone);
        ready.Dispose();
    }

    bool Enter()
    {
        Interlocked.Increment(ref users);
        if (Volatile.Read(ref closed) == 0) return true;
        Interlocked.Decrement(ref users);
        return false;
    }

    void Leave() => Interlocked.Decrement(ref users);

    /// <summary>An event handle the native microphone owns, as a wait handle the client can wait on.</summary>
    sealed class NativeEvent : WaitHandle
    {
        public NativeEvent(nint handle) => SafeWaitHandle = new SafeWaitHandle(handle, ownsHandle: false);
    }
}
