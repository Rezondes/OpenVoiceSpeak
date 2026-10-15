using OVS.Client.Audio.Capture;
using OVS.Client.Audio.Codec;
using OVS.Client.Audio.Devices;
using OVS.Client.Localization;

namespace OVS.Tests.TestSupport;

/// <summary>
/// Package 124: counts the microphones and speakers the engine opens and closes, without hardware. Package 137: knows
/// which devices are plugged in and which are the defaults, falls back like the real one, and raises device changes.
/// </summary>
#pragma warning disable CA1001 // a test fake; its gate only blocks briefly and needs no wait handle
internal sealed class FakeAudioHardware : IAudioHardware
#pragma warning restore CA1001
{
    readonly List<(string Device, CapturePipeline Capture)> captures = [];
    readonly List<(string Device, FakeSpeaker Output)> outputs = [];
    readonly List<string?> captureIds = [];
    Action? changed;
    Action<AudioDirection, Exception>? failed;

    /// <summary>
    /// Package 137: the devices plugged in, each with its direction; a requested id that is not here, or of the other
    /// direction, opens the default with a warning. Changed only through <see cref="Plug"/> and <see cref="Unplug"/>, under
    /// its lock: the opens read it on device threads (Step 30).
    /// </summary>
    readonly Dictionary<string, AudioDirection> present = [];

    /// <summary>The defaults; null when none is left (every device unplugged): then an open of the default throws, like the real one.</summary>
    public volatile string? DefaultMic = "mic-default", DefaultSpeaker = "speaker-default";

    /// <summary>Opens wait while it is reset (at most 10 s), so a test can tell on which thread they run.</summary>
    public readonly ManualResetEventSlim OpenGate = new(true);

    /// <summary>While set, a device that resolves still cannot start: its open throws (as an exclusive or broken device).</summary>
    public volatile bool StartFails;

    /// <summary>What <see cref="ListActive"/> returns, on the caller's thread (a test may block it like a slow device list); none by default.</summary>
    public Func<AudioDirection, List<AudioDevice>> Listing { get; init; } = _ => [];

    /// <summary>A <see cref="Listing"/> with one microphone ("mic-1", "Headset") and one speaker ("out-1", "Speakers").</summary>
    public static List<AudioDevice> ListOneEach(AudioDirection direction) =>
        direction == AudioDirection.Input ? [Headset] : [Speakers];

    /// <summary>The devices the tests list: two microphones and two speakers.</summary>
    public static readonly AudioDevice Headset = new("mic-1", "Headset"), Speakers = new("out-1", "Speakers"),
        Webcam = new("mic-2", "Webcam"), MonitorOutput = new("out-2", "Monitor");

    public event Action? Changed
    {
        add { lock (captures) changed += value; }
        remove { lock (captures) changed -= value; }
    }

    public event Action<AudioDirection, Exception>? Failed
    {
        add { lock (captures) failed += value; }
        remove { lock (captures) failed -= value; }
    }

    /// <summary>Package 137: as Windows reports a device added, removed or a new default.</summary>
    public void RaiseChanged()
    {
        Action? handler;
        lock (captures) handler = changed;
        handler?.Invoke();
    }

    /// <summary>Package 173: the open speaker's device stops with an error; the playback loop reports it on its next poll.</summary>
    public void FailOpenSpeaker()
    {
        lock (outputs) outputs.Single(output => !output.Output.Disposed).Output.Fail();
    }

    void RaiseFailed(AudioDirection direction, Exception error)
    {
        Action<AudioDirection, Exception>? handler;
        lock (captures) handler = failed;
        handler?.Invoke(direction, error);
    }

    /// <summary>Package 137: as an open device's stream stopping with an error; only an open stream can break.</summary>
    public void RaiseFailed(AudioDirection direction)
    {
        var (mic, speaker) = Open;
        if ((direction == AudioDirection.Input ? mic : speaker) is null) throw new InvalidOperationException($"no open {direction} stream to fail");
        Action<AudioDirection, Exception>? handler;
        lock (captures) handler = failed;
        handler?.Invoke(direction, new InvalidOperationException("device invalidated"));
    }

    /// <summary>Plugs the device <paramref name="id"/> in as a device of <paramref name="direction"/>.</summary>
    public void Plug(string id, AudioDirection direction)
    {
        lock (present) present.Add(id, direction);
    }

    /// <summary>Unplugs the device <paramref name="id"/>.</summary>
    public void Unplug(string id)
    {
        lock (present) present.Remove(id);
    }

    public List<AudioDevice> ListActive(AudioDirection direction) => Listing(direction);

    public string? FindCurrentDeviceId(string? deviceId, AudioDirection direction) => Resolve(deviceId, direction, []);

    public int Subscribers
    {
        get { lock (captures) return changed?.GetInvocationList().Length ?? 0; }
    }

    public int FailedSubscribers
    {
        get { lock (captures) return failed?.GetInvocationList().Length ?? 0; }
    }

    string? Resolve(string? id, AudioDirection direction, List<string> warnings)
    {
        var fallback = direction == AudioDirection.Input ? DefaultMic : DefaultSpeaker;
        lock (present)
        {
            if (id is null) return fallback;
            if (present.TryGetValue(id, out var plugged) && plugged == direction) return id;
        }
        warnings.Add(Strings.Audio_DeviceFallback);
        return fallback;
    }

    public CapturePipeline OpenInput(string? deviceId, List<string> warnings)
    {
        OpenGate.Wait(TimeSpan.FromSeconds(10));
        var device = Resolve(deviceId, AudioDirection.Input, warnings) ?? throw new InvalidOperationException("no microphone");
        if (StartFails) throw new InvalidOperationException("device cannot start");
        var capture = CapturePipeline.Manual();
        lock (captures)
        {
            captures.Add((device, capture));
            captureIds.Add(deviceId);
        }
        return capture;
    }

    public IPlaybackBuffer OpenOutput(string? deviceId, List<string> warnings)
    {
        OpenGate.Wait(TimeSpan.FromSeconds(10));
        var device = Resolve(deviceId, AudioDirection.Output, warnings) ?? throw new InvalidOperationException("no speaker");
        if (StartFails) throw new InvalidOperationException("device cannot start");
        var output = new FakeSpeaker(SpeakerBuffer, error => RaiseFailed(AudioDirection.Output, error));
        lock (outputs) outputs.Add((device, output));
        return output;
    }

    /// <summary>
    /// How much an opened speaker's buffer holds. Nothing plays it: one frame by default, so it never holds the playback's
    /// target (<see cref="OVS.Client.Audio.Playback.PlaybackLoop.TargetBufferMs"/>) and the tones run through at once.
    /// </summary>
    public TimeSpan SpeakerBuffer { get; set; } = AudioFormat.FrameDuration;

    /// <summary>Opened so far and open now, for microphones and speakers.</summary>
    public DeviceCounts Counts
    {
        get
        {
            lock (captures)
            lock (outputs)
                return new(captures.Count, captures.Count(capture => !capture.Capture.IsDisposed), outputs.Count, outputs.Count(output => !output.Output.Disposed));
        }
    }

    public string? LastMicId
    {
        get { lock (captures) return captureIds[^1]; }
    }

    /// <summary>Package 137: the devices open now (each at most one), or null.</summary>
    public OpenDevices Open
    {
        get
        {
            lock (captures)
            lock (outputs)
                return new(captures.SingleOrDefault(capture => !capture.Capture.IsDisposed).Device, outputs.SingleOrDefault(output => !output.Output.Disposed).Device);
        }
    }

}
