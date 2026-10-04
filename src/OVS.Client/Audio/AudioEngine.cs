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

/// <summary>Package 124: where the engine gets its microphone and speaker; tests count the opens and closes without hardware.</summary>
public interface IAudioDeviceFactory
{
    /// <returns>A capture, not started yet.</returns>
    CapturePipeline OpenCapture(string? deviceId, List<string> warnings);

    /// <returns>The buffer the playback loop fills, and the playing device, disposed when done.</returns>
    (BufferedWaveProvider Buffer, IDisposable Device) OpenOutput(string? deviceId, List<string> warnings);
}

/// <summary>The real microphone and speaker (WASAPI).</summary>
sealed class WasapiDevices : IAudioDeviceFactory
{
    public static readonly WasapiDevices Instance = new();

    public CapturePipeline OpenCapture(string? deviceId, List<string> warnings) =>
        CapturePipeline.FromDevice(AudioDevices.Open(deviceId, DataFlow.Capture, warnings));

    public (BufferedWaveProvider Buffer, IDisposable Device) OpenOutput(string? deviceId, List<string> warnings)
    {
        var device = AudioDevices.Open(deviceId, DataFlow.Render, warnings);
        var output = device is null
            ? new WasapiOut(AudioClientShareMode.Shared, 30)
            : new WasapiOut(device, AudioClientShareMode.Shared, true, 30);
        try
        {
            var buffer = new BufferedWaveProvider(WaveFormat.CreateIeeeFloatWaveFormat(AudioFormat.SampleRate, 1))
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

/// <summary>
/// Owns microphone, speaker, mixer and the playback clock. Recreating devices on Configure keeps the connection.
/// Without devices (--no-audio) the mixer runs on its own 20 ms clock, so reception and indicators still work.
/// Package 124 (A132): the devices are only open while needed, and the playback loop sleeps while there is nothing to play.
/// </summary>
public sealed class AudioEngine : IDisposable
{
    // Small buffers keep mouth-to-ear latency low; the refill loop wakes about every 15 ms.
    static readonly TimeSpan TargetBuffer = TimeSpan.FromMilliseconds(40);
    static readonly TimeSpan DeviceLatency = TimeSpan.FromMilliseconds(40);

    readonly object gate = new(); // the devices and settings
    readonly object threadGate = new(); // the playback thread's start and end; never held while taking gate
    readonly KeyPoller keys;
    readonly IAudioDeviceFactory? devices;
    readonly Action<string>? log;
    bool noiseSuppressorMissing; // logged once, then no further tries
    Thread? playbackThread;
    readonly AutoResetEvent wake = new(false);
    readonly ConcurrentDictionary<uint, long> framesReceived = new();
    volatile bool running = true;
    volatile bool selfMuted = true, deafened, hasSpeakLinked, connected, channelMuted, selfTest, levelMeter, micWanted, voiceDetected;
    bool talkKeyHeld; // key thread only (Package 131)
    bool loopbackFrame; // capture thread only: the frame just decided goes to the own ears
    uint loopbackSeq;
    ClientSettings settings = new();
    ClientSettings? live; // the settings page's values until the next Configure
    CapturePipeline? capture;
    IDisposable? output;
    volatile BufferedWaveProvider? playback;
    readonly SpeakerReports speakerReports = new();
    byte? lastTarget;
    int levelCounter;
    long framesSent, mixes;

    /// <param name="log">A line for the client log (Package 123: when noise suppression cannot load).</param>
    /// <param name="devices">Package 124: where microphone and speaker come from; the real ones by default (tests pass fakes).</param>
    public AudioEngine(KeyPoller keys, bool useDevices = true, Action<string>? log = null, IAudioDeviceFactory? devices = null)
    {
        this.keys = keys;
        this.devices = devices ?? (useDevices ? WasapiDevices.Instance : null);
        this.log = log;
        keys.Changed += OnKeysChanged;
    }

    /// <summary>
    /// Package 131 (A140): a talk key that acts in this mode is down, the bare key without the release delay: link
    /// push-to-talk always, push-to-talk in its own mode.
    /// </summary>
    public bool TalkKeyHeld => keys.IsDown(KeyAction.LinkPushToTalk) || (Mode == TransmitMode.PushToTalk && keys.IsDown(KeyAction.PushToTalk));

    /// <summary>Package 131: the voice gate heard voice in the last frame, also while muted (it runs anyway, nothing is sent).</summary>
    public bool VoiceDetected => voiceDetected;

    /// <summary>Package 131: the talk key tones on the key's own edges, while the keys can talk (connected or self test).</summary>
    void OnKeysChanged()
    {
        bool held = TalkKeyHeld;
        if (held == talkKeyHeld) return;
        talkKeyHeld = held;
        if (connected || selfTest) PlaySound(held ? SoundEvent.TalkKeyDown : SoundEvent.TalkKeyUp);
    }

    public Mixer Mixer { get; } = new();
    public TransmitMode Mode { get; set; }
    public bool SelfMuted { get => selfMuted; set => selfMuted = value; }
    public bool HasSpeakLinked { get => hasSpeakLinked; set => hasSpeakLinked = value; }

    public bool Connected
    {
        get => connected;
        set
        {
            connected = value;
            DevicesChanged();
        }
    }

    /// <summary>Package 34: in a muted channel nobody would hear it, so nothing is sent.</summary>
    public bool ChannelMuted { get => channelMuted; set => channelMuted = value; }

    /// <summary>The mixer id of the own voice during the self test; the server never hands out session id 0.</summary>
    public const uint SelfTestSpeaker = 0;

    /// <summary>
    /// Package 53: the own voice goes through Opus, the voice boost and the volume straight back to the own ears,
    /// gated like real sending (voice activation or PTT), but never to the server.
    /// </summary>
    public bool SelfTest
    {
        get => selfTest;
        set
        {
            selfTest = value;
            DevicesChanged();
        }
    }

    /// <summary>Package 124: the settings page shows the microphone's level, so the microphone is open while it shows.</summary>
    public bool LevelMeter
    {
        get => levelMeter;
        set
        {
            levelMeter = value;
            DevicesChanged();
        }
    }

    /// <summary>Package 124 (A132): the microphone only while connected, in the self test or under the level meter.</summary>
    bool MicNeeded => connected || selfTest || levelMeter;

    /// <summary>Package 124: the playback thread and speaker while connected and in the self test, else until the tones played.</summary>
    bool PlaybackNeeded => connected || selfTest;

    /// <summary>Package 124: a device opened in the background could not be used (the text for a notice).</summary>
    public event Action<string>? Warning;

    /// <summary>Package 53: sliders on the settings page act at once, without restarting the devices.</summary>
    public void ApplyLive(ClientSettings live)
    {
        lock (gate)
        {
            this.live = live; // Package 124: a microphone still opening in the background takes these too
            Mode = live.Mode;
            Mixer.Volume = live.OutputVolume;
            keys.ReleaseDelay = TimeSpan.FromMilliseconds(live.PttReleaseDelayMs); // Package 130
            if (capture is null) return;
            capture.Gain = live.InputGain;
            capture.Vad.ThresholdDb = live.VadThresholdDb;
            capture.Vad.Automatic = live.VadAutomatic;
            capture.NoiseSuppression = live.NoiseSuppression;
            capture.AutoGain = live.AutoGain;
        }
    }

    /// <summary>Package 123: the native RNNoise library; tests name a missing one.</summary>
    internal string NoiseLibrary { get; init; } = NoiseSuppressor.Library;

    internal CapturePipeline? Capture => capture;

    /// <summary>Package 124: tests count the frames mixed and see whether the playback thread runs.</summary>
    internal long MixCount => Interlocked.Read(ref mixes);

    internal bool PlaybackRunning
    {
        get { lock (threadGate) return playbackThread is not null; }
    }

    // ---- Sounds (Package 47, 48) ----

    /// <summary>Where a tone's samples come from: the default tone, or the user's own file (set by the main view model).</summary>
    public Func<SoundEvent, float[]>? SoundSource { get; set; }

    /// <summary>"Alle Sounds aus" and "stumm" silence it, otherwise the sound's own volume times the overall one.</summary>
    public static float GainFor(ClientSettings settings, SoundEvent sound) =>
        !settings.SoundsEnabled || settings.SoundFor(sound) is { Muted: true } ? 0f : settings.SoundVolume * settings.SoundFor(sound).Volume;

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
        if (deafened && sound is not (SoundEvent.MicOff or SoundEvent.MicOn or SoundEvent.SoundOff or SoundEvent.SoundOn)) return;
        float gain = GainFor(settings, sound);
        if (gain <= 0) return;
        PlaySamples((SoundSource ?? SoundSynth.Render)(sound), gain);
        lock (recentSounds)
        {
            recentSounds.Enqueue(sound);
            while (recentSounds.Count > 20) recentSounds.Dequeue();
        }
    }

    /// <summary>Package 124: any tone, also while not connected (a preview): the speaker opens for it and closes afterwards.</summary>
    public void PlaySamples(float[] samples, float gain)
    {
        Sounds.Play(samples, gain);
        Kick();
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

    /// <summary>Package 129 (A138): the voice gate's threshold in use, for the level meter; the manual one without a microphone.</summary>
    public float InputThresholdDb => capture?.Vad.ThresholdInUseDb ?? (live ?? settings).VadThresholdDb;

    /// <summary>Takes the settings; the devices open now only if they are needed (Package 124), and then with these.</summary>
    /// <returns>A warning for the user, or null.</returns>
    public string? Configure(ClientSettings newSettings)
    {
        lock (gate)
        {
            settings = newSettings;
            live = null;
            bool speaker = output is not null || PlaybackRunning;
            StopDevices();
            Mode = settings.Mode;
            Mixer.Volume = settings.OutputVolume;
            keys.ReleaseDelay = TimeSpan.FromMilliseconds(settings.PttReleaseDelayMs); // Package 130
            var warnings = new List<string>();
            SyncCapture(warnings);
            if (speaker) StartOutput(warnings); // a device changed while connected takes effect at once
            return Text(warnings);
        }
    }

    static string? Text(List<string> warnings) => warnings.Count == 0 ? null : string.Join(" ", warnings.Distinct());

    public void SetTone(double? frequency)
    {
        lock (gate)
        {
            ToneHz = frequency;
            capture?.Dispose();
            capture = null;
            SyncCapture([]);
        }
    }

    /// <summary>
    /// Package 124: called whenever a flag that decides about the devices is set (the UI sets them again and again).
    /// The microphone opens or closes in the background, off the UI thread; the playback thread ends by itself.
    /// </summary>
    void DevicesChanged()
    {
        if (PlaybackNeeded) StartPlayback();
        wake.Set(); // the idle loop sees that the self test began, or that it may end
        lock (threadGate)
        {
            if (micWanted == MicNeeded) return;
            micWanted = MicNeeded;
        }
        if (devices is null) return; // the test tone needs no device and runs as long as it is set
        ThreadPool.QueueUserWorkItem(_ =>
        {
            var warnings = new List<string>();
            try
            {
                lock (gate) SyncCapture(warnings);
            }
            catch (Exception e) // a background thread must not take the client down: the user gets a notice instead
            {
                warnings.Add(string.Format(Strings.Audio_MicFailed, e.Message));
            }
            if (Text(warnings) is { } text) Warning?.Invoke(text);
        });
    }

    /// <summary>Opens or closes the capture as needed: the tone always, the microphone while wanted. Under gate.</summary>
    void SyncCapture(List<string> warnings)
    {
        if (!running) return; // an open queued before Dispose comes too late
        bool needed = ToneHz is not null || (micWanted && devices is not null);
        if (needed && capture is null) StartCapture(warnings);
        else if (!needed && capture is not null)
        {
            capture.Dispose();
            capture = null;
        }
    }

    void StartCapture(List<string> warnings)
    {
        try
        {
            capture = ToneHz is { } hz ? CapturePipeline.FromTone(hz) : devices?.OpenCapture(settings.InputDeviceId, warnings);
            if (capture is null) return;
            var values = live ?? settings;
            capture.Gain = values.InputGain;
            capture.DecideTarget = Decide;
            capture.Vad.ThresholdDb = values.VadThresholdDb;
            capture.Vad.Automatic = values.VadAutomatic;
            capture.Suppressor = CreateSuppressor();
            capture.NoiseSuppression = values.NoiseSuppression;
            capture.AutoGain = values.AutoGain;
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

    /// <summary>Created even while switched off, so the switch on the settings page acts at once.</summary>
    NoiseSuppressor? CreateSuppressor()
    {
        if (noiseSuppressorMissing) return null;
        var suppressor = NoiseSuppressor.Create(out var error, NoiseLibrary);
        if (suppressor is null)
        {
            noiseSuppressorMissing = true; // AC7: the voice goes out unfiltered instead of not at all
            log?.Invoke($"Rauschunterdrückung aus: {NoiseLibrary} nicht ladbar ({error})");
        }
        return suppressor;
    }

    /// <summary>Under gate.</summary>
    void StartOutput(List<string> warnings)
    {
        if (devices is null || output is not null || !running) return;
        try
        {
            (playback, output) = devices.OpenOutput(settings.OutputDeviceId, warnings);
        }
        catch (Exception e) when (e is COMException or InvalidOperationException or ArgumentException)
        {
            warnings.Add(string.Format(Strings.Audio_SpeakerFailed, e.Message));
        }
    }

    void OnFrameEncoded(byte[] opus, byte target)
    {
        if (loopbackFrame)
        {
            Mixer.Push(SelfTestSpeaker, loopbackSeq++, opus, false);
            Kick();
            return;
        }
        if (Send is not { } send) return;
        send(opus, target);
        Interlocked.Increment(ref framesSent);
    }

    /// <summary>The capture's decision per frame; internal so tests feed frames by hand (Package 130).</summary>
    internal byte? Decide(bool voiceActive)
    {
        loopbackFrame = false;
        voiceDetected = voiceActive;
        if (selfTest)
        {
            // as if unmuted, but only for the own ears; nothing counts as sending
            if (lastTarget is not null)
            {
                lastTarget = null;
                TransmitChanged?.Invoke(null);
            }
            loopbackFrame = TransmitController.Decide(Mode, keys.PttDown, keys.LinkPttDown, voiceActive, keys.MuteHeld, hasSpeakLinked) is not null;
            return loopbackFrame ? VoiceHeader.TargetChannel : null;
        }
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
        Kick();
    }

    // ---- Playback (Package 124: only while needed, asleep while there is nothing to play) ----

    /// <summary>Something to play arrived: the playback loop runs (started if needed) and is awake.</summary>
    void Kick()
    {
        StartPlayback();
        wake.Set();
    }

    void StartPlayback()
    {
        lock (threadGate)
        {
            if (playbackThread is not null || !running) return;
            playbackThread = new Thread(PlaybackLoop) { IsBackground = true, Name = "Playback" };
            playbackThread.Start();
        }
    }

    /// <summary>A voice to play or conceal, a tone, the self test, or a speaker report still due (the UI clears its marks).</summary>
    bool HasWork => selfTest || Sounds.Count > 0 || Mixer.HasWork || speakerReports.Pending;

    void PlaybackLoop()
    {
        var warnings = new List<string>();
        try
        {
            lock (gate) StartOutput(warnings);
        }
        catch (Exception e) // mixing goes on without a speaker instead of the thread taking the client down
        {
            warnings.Add(string.Format(Strings.Audio_SpeakerFailed, e.Message));
        }
        if (Text(warnings) is { } text) Warning?.Invoke(text);
        var bytes = new byte[AudioFormat.FrameSamples * sizeof(float)];
        var clock = Stopwatch.StartNew();
        long ticks = 0;
        while (running)
        {
            if (!HasWork)
            {
                if (!PlaybackNeeded)
                {
                    if (TryEnd()) return;
                    continue; // something came up meanwhile: look again instead of waiting for a wake that may not come
                }
                wake.WaitOne(); // a voice, a tone, the self test or the end wakes it
                clock.Restart(); // the own clock starts anew: the first frame at once, no catching up on the pause
                ticks = 0;
                continue;
            }
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

    /// <summary>Not needed any more and nothing left: closes the speaker once the last frames played, and ends the thread.</summary>
    /// <returns>False when something came up meanwhile.</returns>
    bool TryEnd()
    {
        // the end of the last tone: what is still buffered, plus what the device itself holds (opened with 30 ms)
        if (playback?.BufferedDuration is { } left) Thread.Sleep(left + DeviceLatency);
        lock (gate)
        lock (threadGate)
        {
            if (PlaybackNeeded || HasWork) return false;
            CloseOutput();
            playbackThread = null;
            return true;
        }
    }

    float[] Mix()
    {
        Interlocked.Increment(ref mixes);
        var (frame, active) = Mixer.Tick();
        if (deafened && !selfTest) Array.Clear(frame); // in the self test only the own voice is in the mixer
        LastOutputLevelDb = VoiceActivityDetector.LevelDb(frame); // voice only, before the tones
        Sounds.MixInto(frame);
        if (speakerReports.Next(active) is { } report) SpeakersChanged?.Invoke(report);
        return frame;
    }

    void CloseOutput()
    {
        playback = null;
        output?.Dispose();
        output = null;
    }

    void StopDevices()
    {
        capture?.Dispose();
        capture = null;
        voiceDetected = false; // Package 131: no microphone, no voice
        CloseOutput();
    }

    public void Dispose()
    {
        keys.Changed -= OnKeysChanged;
        lock (gate) running = false; // a microphone open still queued finds the engine gone (SyncCapture)
        wake.Set();
        Thread? thread;
        lock (threadGate) thread = playbackThread;
        thread?.Join(500);
        lock (gate) StopDevices();
    }
}

/// <summary>
/// Package 122: which mixer frames report the speakers to the UI, at most every 100 ms (5 frames): with many speakers the
/// set changes nearly every frame, and each report goes over the users on the UI thread.
/// </summary>
internal sealed class SpeakerReports
{
    public const int Frames = 5;

    List<ActiveSpeaker> last = [];
    int sinceReport = Frames;

    /// <summary>Package 124: someone was reported speaking, so a report of the silence is still due.</summary>
    public bool Pending => last.Count > 0;

    /// <param name="active">Reused by the mixer: a report is a copy (made at most every 100 ms).</param>
    /// <returns>The speakers to report after this frame, or null.</returns>
    public List<ActiveSpeaker>? Next(List<ActiveSpeaker> active)
    {
        // On change, and every 100 ms while someone talks: the UI holds an indicator for 300 ms after the last report. A
        // change within the 100 ms waits for the next report (the set reported last stays the one compared with).
        if (sinceReport < Frames) sinceReport++; // capped: it would overflow after months of silence
        if (sinceReport < Frames || (active.Count == 0 && last.Count == 0)) return null;
        sinceReport = 0;
        last = [.. active];
        return last;
    }
}
