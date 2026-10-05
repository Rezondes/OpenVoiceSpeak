using OVS.Client.Localization;
using System.Runtime.InteropServices;
using NAudio.CoreAudioApi;

namespace OVS.Client.Audio.Devices;

public static class AudioDevices
{
    public static List<AudioDevice> List(DataFlow flow)
    {
        try
        {
            using var enumerator = new MMDeviceEnumerator();
            return enumerator.EnumerateAudioEndPoints(flow, DeviceState.Active).Select(d => new AudioDevice(d.ID, d.FriendlyName)).ToList();
        }
        catch (COMException)
        {
            return [];
        }
    }

    /// <summary>Saved device id, or null (= default device) if it is gone. FellBack tells the UI to show a hint.</summary>
    public static (string? Id, bool FellBack) Resolve(string? wanted, IReadOnlyList<AudioDevice> available) =>
        wanted is null ? (null, false)
        : available.Any(d => d.Id == wanted) ? (wanted, false)
        : (null, true);

    /// <summary>
    /// The saved device by its id, or null (= default device, with a warning if it was set but is gone). Package 52:
    /// straight by id instead of listing every device with its name, which took more than half a second.
    /// </summary>
    public static MMDevice? Open(string? id, DataFlow flow, List<string> warnings)
    {
        if (id is null) return null;
        try
        {
            using var enumerator = new MMDeviceEnumerator();
            var device = enumerator.GetDevice(id);
            if (device.State == DeviceState.Active && device.DataFlow == flow) return device;
            device.Dispose();
        }
        catch (Exception e) when (e is COMException or ArgumentException)
        {
            // unknown id: unplugged or removed
        }
        warnings.Add(Strings.Audio_DeviceFallback);
        return null;
    }
}
