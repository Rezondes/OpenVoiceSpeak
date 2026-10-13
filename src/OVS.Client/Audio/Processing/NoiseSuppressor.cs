using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using OVS.Client.Audio.Codec;

namespace OVS.Client.Audio.Processing;

/// <summary>
/// Package 123 (A131): RNNoise (xiph, BSD-3) on the sent signal. Own minimal binding instead of the package's wrapper,
/// so the native library stays swappable. Loaded by hand rather than with DllImport: a missing library shows up here
/// as null, not as an exception somewhere on the capture thread.
/// </summary>
/// <remarks>No finalizer: the capture pipeline owns it and disposes it under its lock, after the last frame.</remarks>
internal sealed unsafe class NoiseSuppressor : IDisposable
{
    /// <summary>The native library's name.</summary>
    public const string Library = "rnnoise";

    // RNNoise expects samples in the 16-bit range, not -1..1
    const float Scale = 32768f;

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
        catch (Exception exception) // whatever keeps it from loading (missing, wrong architecture, missing runtime) turns it off
        {
            return new Native { Error = exception.Message };
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
        error = "rnnoise_create returned nothing";
        return null;
    }

    /// <summary>
    /// Package 127: a frame RNNoise turned down by more than this is mostly noise, whatever its rating. Measured on
    /// synthetic signals: a voice's speech-rated frames lose a median of 0.1 dB (90 % at most 3.8 dB), while steady
    /// pink noise at -40 and -35 dBFS that RNNoise rates as speech loses at least 4.1 dB (median 9 to 37 dB).
    /// </summary>
    public const float NoiseCutDb = 4f;

    // energy ratio for NoiseCutDb
    static readonly double NoiseCutRatio = Math.Pow(10, -NoiseCutDb / 10);

    /// <summary>Denoises a frame in place; its length is a multiple of <see cref="AudioFormat.HalfFrameSamples"/>.</summary>
    /// <returns>
    /// Package 127 (A136): how likely the frame is speech (0 to 1), the lower of its halves' ratings, so a frame counts
    /// as speech only when all of it does; 0 when RNNoise cut the frame by more than <see cref="NoiseCutDb"/> (it
    /// removed mostly noise: steady loud noise can be rated as speech). Null when disposed or shorter than
    /// <see cref="AudioFormat.HalfFrameSamples"/> (nothing rated).
    /// </returns>
    public float? Process(Span<float> frame)
    {
        if (state == 0 || frame.Length < AudioFormat.HalfFrameSamples) return null; // disposed or too short: passes unchanged
        float probability = 1f;
        double before = 0, after = 0;
        for (int index = 0; index < frame.Length; index++)
        {
            before += frame[index] * frame[index];
            frame[index] *= Scale;
        }
        fixed (float* samples = frame)
        {
            // in place is fine: RNNoise filters the input into its own buffer before it writes the output
            for (int offset = 0; offset + AudioFormat.HalfFrameSamples <= frame.Length; offset += AudioFormat.HalfFrameSamples)
                probability = Math.Min(probability, native.ProcessFrame(state, samples + offset, samples + offset));
        }
        for (int index = 0; index < frame.Length; index++)
        {
            frame[index] = Math.Clamp(frame[index] / Scale, -1f, 1f);
            after += frame[index] * frame[index];
        }
        return after < before * NoiseCutRatio ? 0f : probability;
    }

    /// <summary>Releases the native state.</summary>
    public void Dispose()
    {
        if (state == 0) return;
        native.Destroy(state);
        state = 0;
    }

    /// <summary>The library's three entry points, or why they are not there.</summary>
    sealed class Native
    {
        public delegate* unmanaged[Cdecl]<nint, nint> Create;
        public delegate* unmanaged[Cdecl]<nint, float*, float*, float> ProcessFrame;
        public delegate* unmanaged[Cdecl]<nint, void> Destroy;
        public string? Error;
    }
}
