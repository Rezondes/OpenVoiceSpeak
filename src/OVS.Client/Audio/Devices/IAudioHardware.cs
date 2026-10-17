using OVS.Client.Audio.Capture;

namespace OVS.Client.Audio.Devices;

/// <summary>
/// Package 124: the microphones and speakers: what the engine opens and the settings page lists. Tests use a fake that
/// counts the opens and closes without hardware.
/// </summary>
internal interface IAudioHardware
{
    /// <summary>Package 52: the devices of one direction that are active now, with their names (reading a name is slow).</summary>
    List<AudioDevice> ListActive(AudioDirection direction);

    /// <summary>A device that cannot open (or start) throws; the engine reports it to the user.</summary>
    /// <returns>A capture, not started yet.</returns>
    CapturePipeline OpenInput(string? deviceId, List<string> warnings);

    /// <summary>A device that cannot open throws; the engine reports it to the user.</summary>
    /// <returns>The buffer the playback loop fills, playing already; disposing it stops the device.</returns>
    IPlaybackBuffer OpenOutput(string? deviceId, List<string> warnings);

    /// <summary>
    /// Package 137 (A146): the id of the device that would open now for this choice: the chosen device if active, else
    /// the console default; null when there is none.
    /// </summary>
    string? FindCurrentDeviceId(string? deviceId, AudioDirection direction);

    /// <summary>
    /// Package 185 (A198): the name of the device with this id in any state (also unplugged or disabled), null for an id
    /// Windows does not know; another app's stored id may point to an endpoint Windows has set up again with a new id.
    /// </summary>
    string? NameOf(string deviceId);

    /// <summary>
    /// Package 137: a device was added, removed, enabled or disabled, or the default changed. Raised on any thread (COM
    /// threads too); a handler must return at once.
    /// </summary>
    event Action? Changed;

    /// <summary>Package 137: an open microphone or speaker stopped with an error (unplugged, invalidated); any thread.</summary>
    event Action<AudioDirection, Exception>? Failed;
}
