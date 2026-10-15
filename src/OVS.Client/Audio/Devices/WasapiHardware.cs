using System.Runtime.InteropServices;
using NAudio.CoreAudioApi;
using OVS.Client.Audio.Capture;
using OVS.Client.Audio.Rust;
using OVS.Client.Localization;

namespace OVS.Client.Audio.Devices;

/// <summary>The real microphones and speakers (WASAPI).</summary>
/// <param name="audioLibrary">Package 173: the native audio library's name (tests give a missing one).</param>
sealed class WasapiHardware(string audioLibrary = NativeAudio.Library) : IAudioHardware
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
            return enumerator.EnumerateAudioEndPoints(ToDataFlow(direction), DeviceState.Active).Select(mmDevice => new AudioDevice(mmDevice.ID, mmDevice.FriendlyName)).ToList();
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
        CapturePipeline.FromDevice(Open(deviceId, AudioDirection.Input, warnings), exception => Failed?.Invoke(AudioDirection.Input, exception));

    /// <summary>The speaker's own buffer in WASAPI (its latency); part of the echo canceller's delay hint.</summary>
    internal const int SpeakerBufferMs = 30;

    /// <summary>
    /// Package 173: the native library's speaker (<see cref="NativeSpeaker"/>) on the chosen device, or on the default
    /// one with the fallback warning; without the library it cannot open (A180: no managed fallback).
    /// </summary>
    public IPlaybackBuffer OpenOutput(string? deviceId, List<string> warnings)
    {
        using var device = Open(deviceId, AudioDirection.Output, warnings);
        return NativeSpeaker.Open(NativeAudio.Load(audioLibrary), device?.ID, error => Failed?.Invoke(AudioDirection.Output, error));
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
        catch (Exception exception) when (exception is COMException or ArgumentException)
        {
            // unknown id: unplugged or removed
        }
        device?.Dispose();
        return null;
    }

    internal static DataFlow ToDataFlow(AudioDirection direction) => direction == AudioDirection.Input ? DataFlow.Capture : DataFlow.Render;
}
