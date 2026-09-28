using OVS.Client.Localization;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.InteropServices;
using NAudio.CoreAudioApi;
using NAudio.Wave;
using OVS.Client.Input;
using OVS.Client.Settings;
using OVS.Shared.Voice;

namespace OVS.Client.Audio;

public sealed record AudioDevice(string Id, string Name);

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

    public static MMDevice? Open(string? id, DataFlow flow, List<string> warnings)
    {
        var (resolved, fellBack) = Resolve(id, List(flow));
        if (fellBack) warnings.Add(Strings.Audio_DeviceFallback);
        if (resolved is null) return null;
        using var enumerator = new MMDeviceEnumerator();
        return enumerator.GetDevice(resolved);
    }
}

/// <summary>
/// Owns microphone, speaker, mixer and the playback clock. Recreating devices on Configure keeps the connection.
/// Without devices (--no-audio) the mixer runs on its own 20 ms clock, so reception and indicators still work.
/// </summary>
public sealed class AudioEngine : IDisposable
{
    // Small buffers keep mouth-to-ear latency low; the refill loop wakes about every 15 ms.
    static readonly TimeSpan TargetBuffer = TimeSpan.FromMilliseconds(40);

    readonly object gate = new();
    readonly KeyPoller keys;
    readonly bool useDevices;
    readonly Thread playbackThread;
    readonly ConcurrentDictionary<uint, long> framesReceived = new();
    volatile bool running = true;
    volatile bool selfMuted = true, deafened, hasSpeakLinked, connected, channelMuted;
    ClientSettings settings = new();
    CapturePipeline? capture;
    WasapiOut? output;
    volatile BufferedWaveProvider? playback;
    List<ActiveSpeaker> lastActive = [];
    byte? lastTarget;
    int levelCounter, mixCount;
    long framesSent;

    public AudioEngine(KeyPoller keys, bool useDevices = true)
    {
        this.keys = keys;
        this.useDevices = useDevices;
        playbackThread = new Thread(PlaybackLoop) { IsBackground = true, Name = "Playback" };
        playbackThread.Start();
    }

    public Mixer Mixer { get; } = new();
    public TransmitMode Mode { get; set; }
    public bool SelfMuted { get => selfMuted; set => selfMuted = value; }
    public bool HasSpeakLinked { get => hasSpeakLinked; set => hasSpeakLinked = value; }
    public bool Connected { get => connected; set => connected = value; }
    /// <summary>Package 34: in a muted channel nobody would hear it, so nothing is sent.</summary>
    public bool ChannelMuted { get => channelMuted; set => channelMuted = value; }

    // ---- Sounds (Package 47) ----

    public SoundQueue Sounds { get; } = new();
    readonly Queue<SoundEvent> recentSounds = new();

    /// <summary>The last tones actually played, newest last (debug API).</summary>
    public IReadOnlyList<SoundEvent> RecentSounds
    {
        get { lock (recentSounds) return [.. recentSounds]; }
    }

    /// <summary>Plays a tone on top of the voice. With the sound off only the own microphone and sound tones play (A46).</summary>
    public void PlaySound(SoundEvent sound)
    {
        if (!settings.SoundsEnabled) return;
        if (deafened && sound is not (SoundEvent.MicOff or SoundEvent.MicOn or SoundEvent.SoundOff or SoundEvent.SoundOn)) return;
        Sounds.Play(SoundSynth.Render(sound), settings.SoundVolume);
        lock (recentSounds)
        {
            recentSounds.Enqueue(sound);
            while (recentSounds.Count > 20) recentSounds.Dequeue();
        }
    }

    public bool Deafened
    {
        get => deafened;
        set
        {
            deafened = value;
            if (value) Mixer.Clear();
        }
    }

    /// <summary>Test tone frequency replacing the microphone, or null for the real microphone.</summary>
    public double? ToneHz { get; private set; }

    public long FramesSent => Interlocked.Read(ref framesSent);
    public IReadOnlyDictionary<uint, long> FramesReceived => new Dictionary<uint, long>(framesReceived);
    public float LastOutputLevelDb { get; private set; } = -120f;

    /// <summary>Set to VoiceClient.SendVoice while connected.</summary>
    public Action<byte[], byte>? Send { get; set; }

    public event Action<List<ActiveSpeaker>>? SpeakersChanged;
    public event Action<byte?>? TransmitChanged;
    public event Action<float>? InputLevel;

    /// <returns>A warning for the user, or null.</returns>
    public string? Configure(ClientSettings newSettings)
    {
        lock (gate)
        {
            settings = newSettings;
            StopDevices();
            Mode = settings.Mode;
            Mixer.Volume = settings.OutputVolume;
            var warnings = new List<string>();
            StartCapture(warnings);
            if (useDevices) StartOutput(warnings);
            return warnings.Count == 0 ? null : string.Join(" ", warnings.Distinct());
        }
    }

    public void SetTone(double? frequency)
    {
        lock (gate)
        {
            ToneHz = frequency;
            capture?.Dispose();
            capture = null;
            StartCapture([]);
        }
    }

    void StartCapture(List<string> warnings)
    {
        try
        {
            capture = ToneHz is { } hz ? CapturePipeline.FromTone(hz)
                : useDevices ? CapturePipeline.FromDevice(AudioDevices.Open(settings.InputDeviceId, DataFlow.Capture, warnings))
                : null;
            if (capture is null) return;
            capture.Gain = settings.InputGain;
            capture.DecideTarget = Decide;
            capture.Vad.ThresholdDb = settings.VadThresholdDb;
            capture.FrameEncoded += OnFrameEncoded;
            capture.Level += OnLevel;
            capture.Start();
        }
        catch (Exception e) when (e is COMException or InvalidOperationException or ArgumentException or NotSupportedException)
        {
            capture?.Dispose();
            capture = null;
            warnings.Add(string.Format(Strings.Audio_MicFailed, e.Message));
        }
    }

    void StartOutput(List<string> warnings)
    {
        try
        {
            var device = AudioDevices.Open(settings.OutputDeviceId, DataFlow.Render, warnings);
            output = device is null
                ? new WasapiOut(AudioClientShareMode.Shared, 30)
                : new WasapiOut(device, AudioClientShareMode.Shared, true, 30);
            var buffer = new BufferedWaveProvider(WaveFormat.CreateIeeeFloatWaveFormat(AudioFormat.SampleRate, 1))
            {
                DiscardOnBufferOverflow = true,
                BufferDuration = TimeSpan.FromSeconds(1),
            };
            output.Init(buffer);
            output.Play();
            playback = buffer;
        }
        catch (Exception e) when (e is COMException or InvalidOperationException or ArgumentException)
        {
            output?.Dispose();
            output = null;
            warnings.Add(string.Format(Strings.Audio_SpeakerFailed, e.Message));
        }
    }

    void OnFrameEncoded(byte[] opus, byte target)
    {
        if (Send is not { } send) return;
        send(opus, target);
        Interlocked.Increment(ref framesSent);
    }

    byte? Decide(bool voiceActive)
    {
        // Push-to-mute counts like the own mute: it beats every way of sending (Package 29).
        var target = TransmitController.Decide(Mode, keys.PttDown, keys.LinkPttDown, voiceActive, selfMuted || !connected || keys.MuteHeld || channelMuted, hasSpeakLinked);
        if (target != lastTarget)
        {
            lastTarget = target;
            TransmitChanged?.Invoke(target);
        }
        return target;
    }

    void OnLevel(float db)
    {
        if (++levelCounter % 5 == 0) InputLevel?.Invoke(db);
    }

    /// <summary>VoiceClient callback, network thread.</summary>
    public void OnVoice(uint speaker, uint seq, byte target, byte[] opus)
    {
        if (deafened) return;
        framesReceived.AddOrUpdate(speaker, 1, (_, n) => n + 1);
        Mixer.Push(speaker, seq, opus, target == VoiceHeader.TargetLinked);
    }

    void PlaybackLoop()
    {
        var bytes = new byte[AudioFormat.FrameSamples * sizeof(float)];
        var clock = Stopwatch.StartNew();
        long ticks = 0;
        while (running)
        {
            var buffer = playback;
            if (buffer is null)
            {
                // No speaker: keep mixing on our own clock and throw the audio away.
                if (clock.Elapsed < AudioFormat.FrameDuration * ticks)
                {
                    Thread.Sleep(5);
                    continue;
                }
                ticks++;
                Mix();
                continue;
            }
            if (buffer.BufferedDuration >= TargetBuffer)
            {
                Thread.Sleep(5);
                continue;
            }
            var frame = Mix();
            Buffer.BlockCopy(frame, 0, bytes, 0, bytes.Length);
            buffer.AddSamples(bytes, 0, bytes.Length);
            ticks = (long)(clock.Elapsed / AudioFormat.FrameDuration);
        }
    }

    float[] Mix()
    {
        var (frame, active) = Mixer.Tick();
        if (deafened) Array.Clear(frame);
        LastOutputLevelDb = VoiceActivityDetector.LevelDb(frame); // voice only, before the tones
        Sounds.MixInto(frame);
        // On change, and every 100 ms while someone talks: the UI holds an indicator for 300 ms after the last report.
        if (!active.SequenceEqual(lastActive) || (++mixCount % 5 == 0 && active.Count > 0))
        {
            lastActive = active;
            SpeakersChanged?.Invoke(active);
        }
        return frame;
    }

    void StopDevices()
    {
        capture?.Dispose();
        capture = null;
        playback = null;
        output?.Stop();
        output?.Dispose();
        output = null;
    }

    public void Dispose()
    {
        running = false;
        playbackThread.Join(500);
        lock (gate) StopDevices();
    }
}
