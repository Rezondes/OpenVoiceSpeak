using System.Runtime.InteropServices;
using NAudio.CoreAudioApi;
using NAudio.Wave;
using OVS.Client.Audio.Capture;
using OVS.Client.Localization;

namespace OVS.Client.Audio.Devices;

/// <summary>The real microphones and speakers (WASAPI).</summary>
sealed class WasapiHardware : IAudioHardware
{
    readonly object watcherGate = new();
    Action? changed;
    DeviceWatcher? watcher;

    /// <summary>Package 137: Windows is asked for notifications only while someone listens (the engine, for its lifetime).</summary>
    public event Action? Changed
    {
        add
        {
            lock (watcherGate)
            {
                changed += value;
                watcher ??= DeviceWatcher.Start(Raise);
            }
        }
        remove
        {
            lock (watcherGate)
            {
                changed -= value;
                if (changed is not null) return;
                watcher?.Dispose();
                watcher = null;
            }
        }
    }

    void Raise() => Volatile.Read(ref changed)?.Invoke();

    public event Action<AudioDirection, Exception>? Failed;

    public List<AudioDevice> ListActive(AudioDirection direction)
    {
        try
        {
            using var enumerator = new MMDeviceEnumerator();
            return enumerator.EnumerateAudioEndPoints(ToDataFlow(direction), DeviceState.Active).Select(d => new AudioDevice(d.ID, d.FriendlyName)).ToList();
        }
        catch (COMException)
        {
            return [];
        }
    }

    public string? FindCurrentDeviceId(string? deviceId, AudioDirection direction)
    {
        var flow = ToDataFlow(direction);
        try
        {
            using var enumerator = new MMDeviceEnumerator();
            if (deviceId is not null)
            {
                using var device = FindActive(enumerator, deviceId, flow);
                if (device is not null) return deviceId;
            }
            if (!enumerator.HasDefaultAudioEndpoint(flow, Role.Console)) return null;
            using var fallback = enumerator.GetDefaultAudioEndpoint(flow, Role.Console);
            return fallback.ID;
        }
        catch (COMException)
        {
            return null;
        }
    }

    public CapturePipeline OpenInput(string? deviceId, List<string> warnings) =>
        CapturePipeline.FromDevice(Open(deviceId, AudioDirection.Input, warnings), e => Failed?.Invoke(AudioDirection.Input, e));

    public PlaybackBuffer OpenOutput(string? deviceId, List<string> warnings)
    {
        var device = Open(deviceId, AudioDirection.Output, warnings);
        var output = device is null
            ? new WasapiOut(AudioClientShareMode.Shared, 30)
            : new WasapiOut(device, AudioClientShareMode.Shared, true, 30);
        output.PlaybackStopped += (_, e) =>
        {
            if (e.Exception is { } error) Failed?.Invoke(AudioDirection.Output, error); // Package 137: a stop of our own carries none
        };
        try
        {
            var playback = new PlaybackBuffer(TimeSpan.FromSeconds(1), output); // disposing it stops the device
            output.Init(playback.Source);
            output.Play();
            return playback;
        }
        catch
        {
            output.Dispose();
            throw;
        }
    }

    /// <summary>
    /// The saved device by its id, or null (= default device, with a warning if it was set but is gone). Package 52:
    /// straight by id instead of listing every device with its name, which took more than half a second.
    /// </summary>
    static MMDevice? Open(string? id, AudioDirection direction, List<string> warnings)
    {
        if (id is null) return null;
        try
        {
            using var enumerator = new MMDeviceEnumerator();
            if (FindActive(enumerator, id, ToDataFlow(direction)) is { } device) return device;
        }
        catch (COMException)
        {
            // no audio service: the default device, with the warning
        }
        warnings.Add(Strings.Audio_DeviceFallback);
        return null;
    }

    /// <summary>The device with this id if it is active and of this flow, else null (unplugged, removed, disabled, the other flow).</summary>
    static MMDevice? FindActive(MMDeviceEnumerator enumerator, string id, DataFlow flow)
    {
        MMDevice? device = null;
        try
        {
            device = enumerator.GetDevice(id);
            if (device.State == DeviceState.Active && device.DataFlow == flow) return device;
        }
        catch (Exception e) when (e is COMException or ArgumentException)
        {
            // unknown id: unplugged or removed
        }
        device?.Dispose();
        return null;
    }

    internal static DataFlow ToDataFlow(AudioDirection direction) => direction == AudioDirection.Input ? DataFlow.Capture : DataFlow.Render;
}
