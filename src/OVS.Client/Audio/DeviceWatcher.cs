using System.Runtime.InteropServices;
using NAudio.CoreAudioApi;
using NAudio.CoreAudioApi.Interfaces;

namespace OVS.Client.Audio;

/// <summary>
/// Package 137 (A146): Windows' audio device notifications (added, removed, enabled or disabled, a new default). They
/// arrive on COM threads of the audio service, so the callback must return at once (the engine only arms
/// its debounce timer). No thread of its own: it keeps nothing alive. Dispose unregisters; never from a notification.
/// </summary>
sealed class DeviceWatcher : IMMNotificationClient, IDisposable
{
    readonly MMDeviceEnumerator enumerator;
    readonly Action changed;

    DeviceWatcher(MMDeviceEnumerator enumerator, Action changed)
    {
        this.enumerator = enumerator;
        this.changed = changed;
    }

    /// <returns>The registered watcher, or null where Windows offers no notifications (no audio service).</returns>
    public static DeviceWatcher? Start(Action changed)
    {
        MMDeviceEnumerator? enumerator = null;
        try
        {
            enumerator = new MMDeviceEnumerator();
            var watcher = new DeviceWatcher(enumerator, changed);
            Marshal.ThrowExceptionForHR(enumerator.RegisterEndpointNotificationCallback(watcher));
            return watcher;
        }
        catch (Exception e) when (e is COMException or PlatformNotSupportedException)
        {
            enumerator?.Dispose();
            return null;
        }
    }

    public void OnDeviceStateChanged(string deviceId, DeviceState newState) => changed();
    public void OnDeviceAdded(string pwstrDeviceId) => changed();
    public void OnDeviceRemoved(string deviceId) => changed();

    // Windows sends one per role; the engine opens the defaults of the console role (WasapiCapture and WasapiOut do).
    public void OnDefaultDeviceChanged(DataFlow flow, Role role, string defaultDeviceId)
    {
        if (role == Role.Console) changed();
    }

    // Volume, name and format changes: nothing to reopen for, and they come often.
    public void OnPropertyValueChanged(string pwstrDeviceId, PropertyKey key) { }

    public void Dispose()
    {
        enumerator.UnregisterEndpointNotificationCallback(this);
        enumerator.Dispose();
    }
}
