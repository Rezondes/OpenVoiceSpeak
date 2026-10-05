using System.Runtime.InteropServices;
using NAudio.CoreAudioApi;
using NAudio.Wave;
using OVS.Client.Audio.Capture;
using OVS.Client.Audio.Codec;

namespace OVS.Client.Audio.Devices;

/// <summary>The real microphone and speaker (WASAPI).</summary>
sealed class WasapiDevices : IAudioDeviceFactory
{
    public static readonly WasapiDevices Instance = new();

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

    public event Action<DataFlow, Exception>? Failed;

    public string? Resolve(string? deviceId, DataFlow flow)
    {
        try
        {
            using var enumerator = new MMDeviceEnumerator();
            if (deviceId is not null)
            {
                try
                {
                    using var device = enumerator.GetDevice(deviceId);
                    if (device.State == DeviceState.Active && device.DataFlow == flow) return deviceId;
                }
                catch (Exception e) when (e is COMException or ArgumentException)
                {
                    // unknown id: unplugged or removed
                }
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

    public CapturePipeline OpenCapture(string? deviceId, List<string> warnings) =>
        CapturePipeline.FromDevice(AudioDevices.Open(deviceId, DataFlow.Capture, warnings), e => Failed?.Invoke(DataFlow.Capture, e));

    public (BufferedWaveProvider Buffer, IDisposable Device) OpenOutput(string? deviceId, List<string> warnings)
    {
        var device = AudioDevices.Open(deviceId, DataFlow.Render, warnings);
        var output = device is null
            ? new WasapiOut(AudioClientShareMode.Shared, 30)
            : new WasapiOut(device, AudioClientShareMode.Shared, true, 30);
        output.PlaybackStopped += (_, e) =>
        {
            if (e.Exception is { } error) Failed?.Invoke(DataFlow.Render, error); // Package 137: a stop of our own carries none
        };
        try
        {
            var buffer = new BufferedWaveProvider(WaveFormat.CreateIeeeFloatWaveFormat(AudioFormat.SampleRate, 2)) // Package 142: stereo
            {
                DiscardOnBufferOverflow = true,
                BufferDuration = TimeSpan.FromSeconds(1),
            };
            output.Init(buffer);
            output.Play();
            return (buffer, output); // disposing stops it
        }
        catch
        {
            output.Dispose();
            throw;
        }
    }
}
