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
        if (fellBack) warnings.Add("Ein gespeichertes Audiogerät fehlt, das Standardgerät wird verwendet.");
        if (resolved is null) return null;
        using var enumerator = new MMDeviceEnumerator();
        return enumerator.GetDevice(resolved);
    }
}

/// <summary>Owns microphone, speaker, mixer and the playback clock. Recreating devices on Configure keeps the connection.</summary>
public sealed class AudioEngine : IDisposable
{
    static readonly TimeSpan TargetBuffer = TimeSpan.FromMilliseconds(60);

    readonly object gate = new();
    readonly KeyPoller keys;
    readonly Thread playbackThread;
    volatile bool running = true;
    volatile bool selfMuted = true, deafened, hasSpeakLinked, connected;
    CapturePipeline? capture;
    WasapiOut? output;
    volatile BufferedWaveProvider? playback;
    List<ActiveSpeaker> lastActive = [];
    byte? lastTarget;
    int levelCounter, mixCount;

    public AudioEngine(KeyPoller keys)
    {
        this.keys = keys;
        playbackThread = new Thread(PlaybackLoop) { IsBackground = true, Name = "Playback" };
        playbackThread.Start();
    }

    public Mixer Mixer { get; } = new();
    public TransmitMode Mode { get; set; }
    public bool SelfMuted { get => selfMuted; set => selfMuted = value; }
    public bool HasSpeakLinked { get => hasSpeakLinked; set => hasSpeakLinked = value; }
    public bool Connected { get => connected; set => connected = value; }

    public bool Deafened
    {
        get => deafened;
        set
        {
            deafened = value;
            if (value) Mixer.Clear();
        }
    }

    /// <summary>Set to VoiceClient.SendVoice while connected.</summary>
    public Action<byte[], byte>? Send { get; set; }

    public event Action<List<ActiveSpeaker>>? SpeakersChanged;
    public event Action<byte?>? TransmitChanged;
    public event Action<float>? InputLevel;

    /// <returns>A warning for the user, or null.</returns>
    public string? Configure(ClientSettings settings)
    {
        lock (gate)
        {
            StopDevices();
            Mode = settings.Mode;
            Mixer.Volume = settings.OutputVolume;
            var warnings = new List<string>();

            try
            {
                capture = CapturePipeline.FromDevice(AudioDevices.Open(settings.InputDeviceId, DataFlow.Capture, warnings));
                capture.Gain = settings.InputGain;
                capture.DecideTarget = Decide;
                capture.Vad.ThresholdDb = settings.VadThresholdDb;
                capture.FrameEncoded += (opus, target) => Send?.Invoke(opus, target);
                capture.Level += OnLevel;
                capture.Start();
            }
            catch (Exception e) when (e is COMException or InvalidOperationException or ArgumentException or NotSupportedException)
            {
                capture?.Dispose();
                capture = null;
                warnings.Add($"Mikrofon konnte nicht geöffnet werden: {e.Message}");
            }

            try
            {
                var device = AudioDevices.Open(settings.OutputDeviceId, DataFlow.Render, warnings);
                output = device is null
                    ? new WasapiOut(AudioClientShareMode.Shared, 50)
                    : new WasapiOut(device, AudioClientShareMode.Shared, true, 50);
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
                warnings.Add($"Lautsprecher konnte nicht geöffnet werden: {e.Message}");
            }

            return warnings.Count == 0 ? null : string.Join(" ", warnings.Distinct());
        }
    }

    byte? Decide(bool voiceActive)
    {
        var target = TransmitController.Decide(Mode, keys.PttDown, keys.LinkPttDown, voiceActive, selfMuted || !connected, hasSpeakLinked);
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
        if (!deafened) Mixer.Push(speaker, seq, opus, target == VoiceHeader.TargetLinked);
    }

    void PlaybackLoop()
    {
        var bytes = new byte[AudioFormat.FrameSamples * sizeof(float)];
        while (running)
        {
            var buffer = playback;
            if (buffer is null || buffer.BufferedDuration >= TargetBuffer)
            {
                Thread.Sleep(5);
                continue;
            }
            var (frame, active) = Mixer.Tick();
            if (deafened) Array.Clear(frame);
            Buffer.BlockCopy(frame, 0, bytes, 0, bytes.Length);
            buffer.AddSamples(bytes, 0, bytes.Length);
            // On change, and every 100 ms while someone talks: the UI holds an indicator for 300 ms after the last report.
            if (!active.SequenceEqual(lastActive) || (++mixCount % 5 == 0 && active.Count > 0))
            {
                lastActive = active;
                SpeakersChanged?.Invoke(active);
            }
        }
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
