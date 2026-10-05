using NAudio.CoreAudioApi;
using NAudio.Wave;
using OVS.Client.Audio.Capture;

namespace OVS.Client.Audio.Devices;

/// <summary>Package 124: where the engine gets its microphone and speaker; tests count the opens and closes without hardware.</summary>
public interface IAudioDeviceFactory
{
    /// <returns>A capture, not started yet.</returns>
    CapturePipeline OpenCapture(string? deviceId, List<string> warnings);

    /// <returns>The buffer the playback loop fills, and the playing device, disposed when done.</returns>
    (BufferedWaveProvider Buffer, IDisposable Device) OpenOutput(string? deviceId, List<string> warnings);

    /// <summary>
    /// Package 137 (A146): the endpoint id that would open now for this choice: the chosen device if active, else the
    /// console default; null when there is none.
    /// </summary>
    string? Resolve(string? deviceId, DataFlow flow);

    /// <summary>
    /// Package 137: a device was added, removed, enabled or disabled, or the default changed. Raised on any thread (COM
    /// threads too); a handler must return at once.
    /// </summary>
    event Action? Changed;

    /// <summary>Package 137: an open microphone or speaker stopped with an error (unplugged, invalidated); any thread.</summary>
    event Action<DataFlow, Exception>? Failed;
}
