using OVS.Client.Audio.Capture;

namespace OVS.Client.Audio.Processing;

/// <summary>
/// Package 123, 140: the native filters each new capture gets, the noise suppressor and the echo canceller. A filter
/// whose library cannot load is off from then on, with one log line, and the voice goes out without it. The engine
/// attaches them to each new capture while setting it up, under its lock, so one capture at a time asks.
/// </summary>
public sealed class NativeCaptureFilters
{
    readonly string noiseLibrary, echoLibrary;
    bool noiseSuppressorMissing; // logged once, then no further tries
    bool echoCancellerMissing; // Package 140: the same

    /// <param name="log">A line for the client log when a filter's native library cannot load.</param>
    /// <param name="noiseLibrary">The native RNNoise library; tests name a missing one.</param>
    /// <param name="echoLibrary">The native echo canceller; tests name a missing one.</param>
    internal NativeCaptureFilters(Action<string>? log, string noiseLibrary = NoiseSuppressor.Library, string echoLibrary = EchoCanceller.Library)
    {
        Log = log;
        this.noiseLibrary = noiseLibrary;
        this.echoLibrary = echoLibrary;
    }

    /// <summary>Where a library that cannot load is told; tests check that the composition root hands in the client log.</summary>
    internal Action<string>? Log { get; }

    /// <summary>
    /// Gives a new capture its filters before it starts, created even while switched off, so the switches on the settings
    /// page act at once (AC7: a missing library leaves that filter null).
    /// </summary>
    /// <param name="echoDelayMs">Package 140 (A149): the echo canceller's delay hint, from a mixed frame to its echo in the capture.</param>
    internal void AttachTo(CapturePipeline capture, int echoDelayMs)
    {
        if (!noiseSuppressorMissing)
        {
            capture.Suppressor = NoiseSuppressor.Create(out var error, noiseLibrary);
            if (capture.Suppressor is null) noiseSuppressorMissing = LogOff("Noise suppression", noiseLibrary, error);
        }
        if (!echoCancellerMissing)
        {
            capture.Canceller = EchoCanceller.Create(out var error, echoLibrary);
            if (capture.Canceller is null) echoCancellerMissing = LogOff("Echo cancellation", echoLibrary, error);
            else capture.Canceller.DelayMs = echoDelayMs;
        }
    }

    /// <summary>The one log line of a filter whose library cannot load.</summary>
    /// <returns>True: the filter is off from now on.</returns>
    bool LogOff(string feature, string library, string? error)
    {
        Log?.Invoke($"{feature} off: {library} cannot be loaded ({error})");
        return true;
    }
}
