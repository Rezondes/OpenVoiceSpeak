using NAudio.CoreAudioApi;
using NAudio.Wave;
using OVS.Client.Audio;
using OVS.Client.Localization;
using OVS.Shared.Voice;

namespace OVS.Tests.TestSupport;

/// <summary>
/// Package 124: counts the microphones and speakers the engine opens and closes, without hardware. Package 137: knows
/// which devices are plugged in and which are the defaults, falls back like the real one, and raises device changes.
/// </summary>
public sealed class FakeAudioDevices : IAudioDeviceFactory
{
    readonly List<(string Device, CapturePipeline Capture)> captures = [];
    readonly List<(string Device, FakeOutput Output)> outputs = [];
    readonly List<string?> captureIds = [];
    Action? changed;

    /// <summary>Package 137: the devices plugged in; a requested id that is not here opens the default with a warning.</summary>
    public readonly HashSet<string> Present = [];
    public volatile string DefaultMic = "mic-default", DefaultSpeaker = "speaker-default";

    public event Action? Changed
    {
        add { lock (captures) changed += value; }
        remove { lock (captures) changed -= value; }
    }

    public event Action<DataFlow, Exception>? Failed;

    /// <summary>Package 137: as Windows reports a device added, removed or a new default.</summary>
    public void RaiseChanged()
    {
        Action? handler;
        lock (captures) handler = changed;
        handler?.Invoke();
    }

    /// <summary>Package 137: as an open device's stream stopping with an error.</summary>
    public void RaiseFailed(DataFlow flow) => Failed?.Invoke(flow, new InvalidOperationException("device invalidated"));

    public string? Resolve(string? deviceId, DataFlow flow) =>
        Resolve(deviceId, flow == DataFlow.Capture ? DefaultMic : DefaultSpeaker, []);

    public int Subscribers
    {
        get { lock (captures) return changed?.GetInvocationList().Length ?? 0; }
    }

    string Resolve(string? id, string fallback, List<string> warnings)
    {
        lock (Present)
        {
            if (id is null) return fallback;
            if (Present.Contains(id)) return id;
        }
        warnings.Add(Strings.Audio_DeviceFallback);
        return fallback;
    }

    public CapturePipeline OpenCapture(string? deviceId, List<string> warnings)
    {
        var device = Resolve(deviceId, DefaultMic, warnings);
        var capture = CapturePipeline.Manual();
        lock (captures)
        {
            captures.Add((device, capture));
            captureIds.Add(deviceId);
        }
        return capture;
    }

    public (BufferedWaveProvider Buffer, IDisposable Device) OpenOutput(string? deviceId, List<string> warnings)
    {
        var device = Resolve(deviceId, DefaultSpeaker, warnings);
        var output = new FakeOutput();
        lock (outputs) outputs.Add((device, output));
        // Nothing plays it: one frame long, so it never holds the engine's 40 ms and the tones run through at once.
        var buffer = new BufferedWaveProvider(WaveFormat.CreateIeeeFloatWaveFormat(AudioFormat.SampleRate, 2)) // Package 142: stereo, as the real one
        {
            BufferDuration = AudioFormat.FrameDuration,
            DiscardOnBufferOverflow = true,
        };
        return (buffer, output);
    }

    /// <summary>Opened so far and open now, for microphones and speakers.</summary>
    public (int Mics, int MicsOpen, int Speakers, int SpeakersOpen) Counts
    {
        get
        {
            lock (captures)
            lock (outputs)
                return (captures.Count, captures.Count(c => !c.Capture.IsDisposed), outputs.Count, outputs.Count(o => !o.Output.Disposed));
        }
    }

    public string? LastMicId
    {
        get { lock (captures) return captureIds[^1]; }
    }

    /// <summary>Package 137: the devices open now (each at most one), or null.</summary>
    public (string? Mic, string? Speaker) Open
    {
        get
        {
            lock (captures)
            lock (outputs)
                return (captures.SingleOrDefault(c => !c.Capture.IsDisposed).Device, outputs.SingleOrDefault(o => !o.Output.Disposed).Device);
        }
    }

    sealed class FakeOutput : IDisposable
    {
        public volatile bool Disposed;
        public void Dispose() => Disposed = true;
    }
}
