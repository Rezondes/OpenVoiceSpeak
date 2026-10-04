using System.Collections.Concurrent;
using System.Runtime.InteropServices;

namespace OVS.Client.Audio;

/// <summary>
/// Package 140 (A149): WebRTC's echo canceller (AEC3, from the freedesktop webrtc-audio-processing, BSD-3) behind the
/// own C wrapper <c>ovs_aec</c> (see native/ovs_aec). Loaded by hand like <see cref="NoiseSuppressor"/>: a missing
/// library shows up as null. The far end (<see cref="Render"/>) comes from the playback thread, the near end
/// (<see cref="Process"/>) from the capture thread under the pipeline's lock; the library locks the two sides itself.
/// </summary>
/// <remarks>No finalizer: the capture pipeline owns it and disposes it under its lock.</remarks>
public sealed unsafe class EchoCanceller : IDisposable
{
    public const string Library = "ovs_aec";

    /// <summary>The library works on 10 ms at 48 kHz, so one 20 ms frame is two halves.</summary>
    public const int HalfFrame = 480;

    sealed class Native
    {
        public delegate* unmanaged[Cdecl]<nint> Create;
        public delegate* unmanaged[Cdecl]<nint, int, void> SetDelay;
        public delegate* unmanaged[Cdecl]<nint, float*, int> Render;
        public delegate* unmanaged[Cdecl]<nint, float*, int> Capture;
        public delegate* unmanaged[Cdecl]<nint, void> Destroy;
        public string? Error;
    }

    static readonly ConcurrentDictionary<string, Lazy<Native>> Loaded = new();

    readonly Native native;
    readonly object renderGate = new(); // the playback thread's Render against Dispose
    nint state;
    long rendered;

    /// <summary>Tests see the far end arrive.</summary>
    internal long RenderedFrames => Interlocked.Read(ref rendered);

    EchoCanceller(Native native, nint state) => (this.native, this.state) = (native, state);

    static Native Load(string library)
    {
        try
        {
            var handle = NativeLibrary.Load(library, typeof(EchoCanceller).Assembly, null);
            return new Native
            {
                Create = (delegate* unmanaged[Cdecl]<nint>)NativeLibrary.GetExport(handle, "ovs_aec_create"),
                SetDelay = (delegate* unmanaged[Cdecl]<nint, int, void>)NativeLibrary.GetExport(handle, "ovs_aec_set_delay"),
                Render = (delegate* unmanaged[Cdecl]<nint, float*, int>)NativeLibrary.GetExport(handle, "ovs_aec_render"),
                Capture = (delegate* unmanaged[Cdecl]<nint, float*, int>)NativeLibrary.GetExport(handle, "ovs_aec_capture"),
                Destroy = (delegate* unmanaged[Cdecl]<nint, void>)NativeLibrary.GetExport(handle, "ovs_aec_destroy"),
            };
        }
        catch (Exception e) // missing, wrong architecture, a missing export: echo cancellation is off
        {
            return new Native { Error = e.Message };
        }
    }

    /// <returns>Null with the reason in <paramref name="error"/> when the library is missing or unusable.</returns>
    public static EchoCanceller? Create(out string? error, string library = Library)
    {
        var native = Loaded.GetOrAdd(library, name => new Lazy<Native>(() => Load(name))).Value;
        error = native.Error;
        if (error is not null) return null;
        var state = native.Create();
        if (state != 0) return new EchoCanceller(native, state);
        error = "ovs_aec_create gab nichts zurück";
        return null;
    }

    /// <summary>
    /// How long a played frame takes to come back in the capture (speaker and capture buffers). AEC3's own delay
    /// estimator finds the actual delay; the hint is mainly the alignment it starts from, at first and whenever the
    /// estimator resets. Read on the capture thread.
    /// </summary>
    public int DelayMs { get; set; }

    /// <summary>The far end: a frame as it goes to the speaker (playback thread); its length is a multiple of <see cref="HalfFrame"/>.</summary>
    public void Render(ReadOnlySpan<float> frame)
    {
        lock (renderGate)
        {
            if (state == 0) return;
            fixed (float* samples = frame)
            {
                for (int offset = 0; offset + HalfFrame <= frame.Length; offset += HalfFrame)
                    native.Render(state, samples + offset); // read only, despite the pointer
            }
            rendered++;
        }
    }

    /// <summary>The near end: removes the echo from a captured frame in place (capture thread). Disposed: unchanged.</summary>
    public void Process(Span<float> frame)
    {
        if (state == 0) return;
        fixed (float* samples = frame)
        {
            for (int offset = 0; offset + HalfFrame <= frame.Length; offset += HalfFrame)
            {
                native.SetDelay(state, DelayMs); // a stream setter: before each capture call, on its thread
                native.Capture(state, samples + offset);
            }
        }
    }

    public void Dispose()
    {
        lock (renderGate)
        {
            if (state == 0) return;
            native.Destroy(state);
            state = 0;
        }
    }
}
