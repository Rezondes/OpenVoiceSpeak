using System.Collections.Concurrent;
using System.Runtime.InteropServices;

namespace OVS.Client.Audio;

/// <summary>
/// Package 123 (A131): RNNoise (xiph, BSD-3) on the sent signal. Own minimal binding instead of the package's wrapper,
/// so the native library stays swappable. Loaded by hand rather than with DllImport: a missing library shows up here
/// as null, not as an exception somewhere on the capture thread.
/// </summary>
/// <remarks>No finalizer: the capture pipeline owns it and disposes it under its lock, after the last frame.</remarks>
public sealed unsafe class NoiseSuppressor : IDisposable
{
    public const string Library = "rnnoise";

    /// <summary>RNNoise works on 10 ms at 48 kHz, so one 20 ms frame is two halves.</summary>
    public const int HalfFrame = 480;

    // RNNoise expects samples in the 16-bit range, not -1..1
    const float Scale = 32768f;

    /// <summary>The library's three entry points, or why they are not there.</summary>
    sealed class Native
    {
        public delegate* unmanaged[Cdecl]<nint, nint> Create;
        public delegate* unmanaged[Cdecl]<nint, float*, float*, float> ProcessFrame;
        public delegate* unmanaged[Cdecl]<nint, void> Destroy;
        public string? Error;
    }

    // loaded and resolved once per process (per name: tests name a missing one)
    static readonly ConcurrentDictionary<string, Lazy<Native>> Loaded = new();

    readonly Native native;
    nint state;

    NoiseSuppressor(Native native, nint state) => (this.native, this.state) = (native, state);

    static Native Load(string library)
    {
        try
        {
            // every export before the first rnnoise_create, so a missing one cannot leave a state behind
            var handle = NativeLibrary.Load(library, typeof(NoiseSuppressor).Assembly, null);
            return new Native
            {
                Create = (delegate* unmanaged[Cdecl]<nint, nint>)NativeLibrary.GetExport(handle, "rnnoise_create"),
                ProcessFrame = (delegate* unmanaged[Cdecl]<nint, float*, float*, float>)NativeLibrary.GetExport(handle, "rnnoise_process_frame"),
                Destroy = (delegate* unmanaged[Cdecl]<nint, void>)NativeLibrary.GetExport(handle, "rnnoise_destroy"),
            };
        }
        catch (Exception e) // whatever keeps it from loading (missing, wrong architecture, missing runtime) turns it off
        {
            return new Native { Error = e.Message };
        }
    }

    /// <returns>Null with the reason in <paramref name="error"/> when the library is missing or unusable.</returns>
    public static NoiseSuppressor? Create(out string? error, string library = Library)
    {
        var native = Loaded.GetOrAdd(library, name => new Lazy<Native>(() => Load(name))).Value;
        error = native.Error;
        if (error is not null) return null;
        var state = native.Create(0); // 0: the built-in model
        if (state != 0) return new NoiseSuppressor(native, state);
        error = "rnnoise_create gab nichts zurück";
        return null;
    }

    /// <summary>Denoises a frame in place; its length is a multiple of <see cref="HalfFrame"/>.</summary>
    public void Process(Span<float> frame)
    {
        if (state == 0) return; // disposed: passes unchanged
        for (int i = 0; i < frame.Length; i++) frame[i] *= Scale;
        fixed (float* samples = frame)
        {
            // in place is fine: RNNoise filters the input into its own buffer before it writes the output
            for (int offset = 0; offset + HalfFrame <= frame.Length; offset += HalfFrame)
                native.ProcessFrame(state, samples + offset, samples + offset);
        }
        for (int i = 0; i < frame.Length; i++) frame[i] = Math.Clamp(frame[i] / Scale, -1f, 1f);
    }

    public void Dispose()
    {
        if (state == 0) return;
        native.Destroy(state);
        state = 0;
    }
}
