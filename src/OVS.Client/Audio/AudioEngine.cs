using System.Collections.Concurrent;
using OVS.Client.Audio.Capture;
using OVS.Client.Audio.Devices;
using OVS.Client.Audio.Playback;
using OVS.Client.Audio.Processing;
using OVS.Client.Audio.Sounds;
using OVS.Client.Audio.Transmit;
using OVS.Client.Input;
using OVS.Client.Settings;
using OVS.Shared.Protocol;
using OVS.Shared.Voice;

namespace OVS.Client.Audio;

/// <summary>
/// Owns the mixer, the microphone and speaker through <see cref="DeviceLifecycle"/> and the playback thread through
/// <see cref="PlaybackLoop"/>. Recreating devices on Configure keeps the connection. Without devices (--no-audio) the
/// mixer runs on its own 20 ms clock, so reception and indicators still work. Package 124 (A132): the devices are only
/// open while needed, and the playback loop sleeps while there is nothing to play.
/// </summary>
public sealed class AudioEngine : IDisposable
{
    /// <summary>
    /// Package 140 (A149): the echo canceller's delay hint, from a mixed frame to its echo in the capture: the playback
    /// buffer, the speaker's device buffer and the microphone's.
    /// </summary>
    internal const int EchoDelayMs = PlaybackLoop.TargetBufferMs + WasapiHardware.SpeakerBufferMs + CapturePipeline.MicBufferMs;

    readonly object gate = new(); // the devices and settings
    readonly object micGate = new(); // the micWanted transition; never held while taking gate
    readonly KeyPoller keys;
    readonly PlaybackLoop playbackLoop;
    readonly ConcurrentDictionary<uint, long> framesReceived = new();
    volatile bool selfMuted = true, deafened, hasSpeakLinked, connected, channelMuted, selfTest, levelMeter, micWanted, voiceDetected;
    bool talkKeyHeld; // key thread only (Package 131)
    bool loopbackFrame; // capture thread only: the frame just decided goes to the own ears
    uint loopbackSeq;
    ClientSettings settings = new();
    ClientSettings? live; // the settings page's values until the next Configure
    byte? lastTarget;
    int levelCounter;
    long framesSent;

    /// <param name="time">Package 135: the clock of the loss adapter (tests pass a manual one); Package 137: of the device reopen.</param>
    /// <param name="hardware">Package 124: where microphone and speaker come from; null runs without them (--no-audio, tests).</param>
    /// <param name="filters">
    /// Package 123, 140: the filters each new capture gets, with their log line when a library cannot load; null: the
    /// shipped libraries, without a log.
    /// </param>
    /// <param name="samples">Package 48: a sound's samples for its setting (the profile's own tones); null: the default tones.</param>
    /// <param name="keys">The key poller push-to-talk and the self test read.</param>
    public AudioEngine(KeyPoller keys, TimeProvider time, IAudioHardware? hardware, NativeCaptureFilters? filters = null,
        Func<SoundEvent, SoundSetting, float[]>? samples = null)
    {
        this.keys = keys;
        Filters = filters ?? new NativeCaptureFilters(log: null);
        Loss = new LossAdapter(time, ProtocolDefaults.Bitrate);
        // before the keys' tones can start the playback thread, which opens the speaker through it
        DeviceLifecycle = new DeviceLifecycle(gate, hardware, time, () => micWanted, () => PlaybackRunning, ConfigureCapture,
            () => voiceDetected = false, Report);
        playbackLoop = new PlaybackLoop(gate, DeviceLifecycle, Mixer, soundQueue, () => PlaybackNeeded, () => selfTest, () => deafened);
        Sounds = new SoundPlayer(soundQueue, () => settings, () => deafened, playbackLoop.Kick, samples); // before the keys' tones can ask for it
        keys.Changed += OnKeysChanged;
    }

    /// <summary>
    /// The microphone (or the test tone in its place) and the speaker: opened, closed and reopened under this engine's
    /// lock, each new capture set up by <see cref="ConfigureCapture"/>.
    /// </summary>
    public DeviceLifecycle DeviceLifecycle { get; }

    /// <summary>Package 123, 140: attached to each new capture in <see cref="ConfigureCapture"/>, under gate.</summary>
    internal NativeCaptureFilters Filters { get; }

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
        if (connected || selfTest) Sounds.Play(held ? SoundEvent.TalkKeyDown : SoundEvent.TalkKeyUp);
    }

    /// <summary>Decodes, buffers and mixes the voices of the others.</summary>
    public VoiceMixer Mixer { get; } = new();
    /// <summary>Push-to-talk or voice activation.</summary>
    public TransmitMode Mode { get; set; }
    /// <summary>The own microphone is muted: nothing is sent.</summary>
    public bool SelfMuted { get => selfMuted; set => selfMuted = value; }
    /// <summary>The own user may talk over links.</summary>
    public bool HasSpeakLinked { get => hasSpeakLinked; set => hasSpeakLinked = value; }

    /// <summary>A voice link is up; while false nothing is sent and the mixer is cleared.</summary>
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

    volatile int channelBitrate = ProtocolDefaults.Bitrate;

    /// <summary>
    /// Package 132 (A141): the own channel's kbps, set on join, move and when it is edited; also for link push-to-talk,
    /// whose listeners sit in other channels. The default while in no channel. Package 135: a new value is the loss
    /// adapter's new ceiling (an edit keeps a lowered bitrate lowered); a move resets it (<c>EngineSync.SyncConnection</c>).
    /// </summary>
    public int ChannelBitrate
    {
        get => channelBitrate;
        set
        {
            if (value == channelBitrate) return;
            channelBitrate = value;
            Loss.SetCeiling(value);
        }
    }

    /// <summary>Package 135 (A144): fed with the uplink loss every 2 s; the channel's bitrate is its ceiling. Tests swap in one on their own clock.</summary>
    public LossAdapter Loss { get; internal set; }

    /// <summary>
    /// Package 132: what the capture encodes with; the self test as a channel at the default would send it. Package 135:
    /// the adapted bitrate, at most the channel's.
    /// </summary>
    internal int EncoderBitrate => selfTest ? ProtocolDefaults.Bitrate : Loss.BitrateKbps;

    /// <summary>Package 135: the loss Opus expects; the self test sends nothing, so it keeps the initial one like its 64 kbps.</summary>
    internal int EncoderLossHint => selfTest ? LossAdapter.InitialHint : Loss.LossHint;

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
            Apply(live);
        }
    }

    /// <summary>
    /// The one place that copies settings onto the engine, its keys and the open capture: the live preview and a save
    /// both come here, and a capture that opens later takes the same values through <see cref="ApplyCaptureSettings"/>.
    /// The open capture is there only for the live preview (a save closes it first). Under gate.
    /// </summary>
    void Apply(ClientSettings values)
    {
        Mode = values.Mode;
        Mixer.Volume = values.OutputVolume;
        keys.ReleaseDelay = TimeSpan.FromMilliseconds(values.PttReleaseDelayMs); // Package 130
        if (DeviceLifecycle.CaptureSource.Capture is { } capture) ApplyCaptureSettings(capture, values);
    }

    /// <summary>The settings a capture works with; a new capture setting is mapped here and nowhere else.</summary>
    internal static void ApplyCaptureSettings(CapturePipeline capture, ClientSettings values)
    {
        capture.Gain = values.InputGain;
        capture.Vad.ThresholdDb = values.VadThresholdDb;
        capture.Vad.Automatic = values.VadAutomatic;
        capture.NoiseSuppression = values.NoiseSuppression;
        capture.AutoGain = values.AutoGain;
        capture.EchoCancellation = values.EchoCancellation; // Package 140
        capture.Music = values.AudioContent == AudioContent.Music; // Package 142
    }

    /// <summary>The settings in use: the settings page's while it previews, else the saved ones.</summary>
    ClientSettings Current => live ?? settings;

    /// <summary>
    /// Sets a new capture up before it starts (the lifecycle's <see cref="CaptureSource"/> calls it under gate): its
    /// filters, the send path's decision and encoder values, the settings in use, the encoded frames and the level.
    /// </summary>
    void ConfigureCapture(CapturePipeline capture)
    {
        Filters.AttachTo(capture, EchoDelayMs);
        capture.ChooseTarget = ChooseTarget;
        capture.Bitrate = () => EncoderBitrate; // Package 132
        capture.LossHint = () => EncoderLossHint; // Package 135
        ApplyCaptureSettings(capture, Current);
        capture.FrameEncoded += OnFrameEncoded;
        capture.Level += OnLevel;
    }

    /// <summary>Package 124: tests count the frames mixed and see whether the playback thread runs.</summary>
    internal long MixCount => playbackLoop.MixCount;

    internal bool PlaybackRunning => playbackLoop.Running;

    // ---- Sounds (Package 47, 48) ----

    /// <summary>The notification sounds, on the saved settings, the deafened flag and the samples source; the playback loop mixes what they queue.</summary>
    public SoundPlayer Sounds { get; }

    readonly SoundQueue soundQueue = new(); // filled by Sounds, mixed by the playback loop

    /// <summary>The own sound is off: nothing is played and nothing is sent.</summary>
    public bool Deafened
    {
        get => deafened;
        set
        {
            deafened = value;
            if (value) Mixer.Clear();
        }
    }

    /// <summary>Voice frames sent since the start.</summary>
    public long FramesSent => Interlocked.Read(ref framesSent);
    /// <summary>Voice frames received per speaker since the start.</summary>
    public IReadOnlyDictionary<uint, long> FramesReceived => new Dictionary<uint, long>(framesReceived);

    /// <summary>The voices' level in the last frame mixed, before the tones.</summary>
    public float LastOutputLevelDb => playbackLoop.LastOutputLevelDb;

    /// <summary>Set to VoiceClient.SendVoice while connected.</summary>
    public Action<byte[], byte>? Send { get; set; }

    /// <summary>Package 122: who is heard, on the playback thread, at most every 100 ms and once more when all fell silent.</summary>
    public event Action<List<ActiveSpeaker>>? SpeakersChanged
    {
        add => playbackLoop.SpeakersChanged += value;
        remove => playbackLoop.SpeakersChanged -= value;
    }

    /// <summary>Raised when sending starts or stops, with the target, null when it stops.</summary>
    public event Action<byte?>? TransmitChanged;
    /// <summary>Raised with each frame's input level in dB.</summary>
    public event Action<float>? InputLevel;

    /// <summary>Package 129 (A138): the voice gate's threshold in use, for the level meter; the manual one without a microphone.</summary>
    public float InputThresholdDb => DeviceLifecycle.CaptureSource.Capture?.Vad.ThresholdInUseDb ?? Current.VadThresholdDb;

    /// <summary>Takes the settings; the devices open now only if they are needed (Package 124), and then with these.</summary>
    /// <returns>A warning for the user, or null.</returns>
    public string? Configure(ClientSettings newSettings)
    {
        lock (gate)
        {
            settings = newSettings;
            live = null;
            DeviceLifecycle.Choose(settings.InputDeviceId, settings.OutputDeviceId); // Package 137: also tries a given-up device again
            // Apply after the capture closed (also when closing threw): the one that opens next takes them in ConfigureCapture
            return JoinWarnings(DeviceLifecycle.Restart(() => Apply(settings)));
        }
    }

    /// <summary>Package 124: what went wrong with devices opened in the background reaches the user as one notice.</summary>
    void Report(IReadOnlyList<string> warnings)
    {
        if (JoinWarnings(warnings) is { } text) Warning?.Invoke(text);
    }

    /// <summary>The warnings of one round of opens as one notice, each text once; null when there are none.</summary>
    internal static string? JoinWarnings(IReadOnlyList<string> warnings) => warnings.Count == 0 ? null : string.Join(" ", warnings.Distinct());

    /// <summary>
    /// Package 124: called whenever a flag that decides about the devices is set (the UI sets them again and again).
    /// The microphone opens or closes in the background, off the UI thread; the playback thread ends by itself.
    /// </summary>
    void DevicesChanged()
    {
        // the idle loop sees that the self test began, or that it may end
        if (PlaybackNeeded) playbackLoop.Kick();
        else playbackLoop.Wake();
        lock (micGate)
        {
            if (micWanted == MicNeeded) return;
            micWanted = MicNeeded;
        }
        if (!DeviceLifecycle.OpensDevices) return; // the test tone needs no device and runs as long as it is set
        ThreadPool.QueueUserWorkItem(_ => DeviceLifecycle.SyncCaptureAndReport());
    }

    void OnFrameEncoded(byte[] opus, byte target)
    {
        if (loopbackFrame)
        {
            Mixer.Push(SelfTestSpeaker, loopbackSeq++, opus, false);
            playbackLoop.Kick();
            return;
        }
        if (Send is not { } send) return;
        send(opus, target);
        Interlocked.Increment(ref framesSent);
    }

    /// <summary>The capture's decision per frame; internal so tests feed frames by hand (Package 130).</summary>
    internal byte? ChooseTarget(bool voiceActive)
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
            loopbackFrame = TransmitRule.ChooseTarget(mode: Mode, pttDown: keys.PttDown, linkPttDown: keys.LinkPttDown, vadActive: voiceActive, muted: keys.MuteHeld, hasSpeakLinked: hasSpeakLinked) is not null;
            return loopbackFrame ? VoiceHeader.TargetChannel : null;
        }
        // Push-to-mute counts like the own mute: it beats every way of sending (Package 29).
        var target = TransmitRule.ChooseTarget(mode: Mode, pttDown: keys.PttDown, linkPttDown: keys.LinkPttDown, vadActive: voiceActive, muted: selfMuted || !connected || keys.MuteHeld || channelMuted, hasSpeakLinked: hasSpeakLinked);
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

    /// <summary>VoiceClient callback, network thread; Package 136: with the copy of the frame before, if it came along.</summary>
    public void OnVoice(uint speaker, uint seq, byte target, byte[] opus, byte[]? copy = null)
    {
        if (deafened) return;
        framesReceived.AddOrUpdate(speaker, 1, (_, count) => count + 1);
        Mixer.Push(speaker, seq, opus, target == VoiceHeader.TargetLinked, copy: copy);
        playbackLoop.Kick();
    }

    /// <summary>Package 140: a frame for the speaker and the echo canceller's far end, as the playback thread mixes it; tests mix by hand.</summary>
    internal float[] MixForSpeaker() => playbackLoop.MixForSpeaker();

    /// <summary>Stops capture and playback and releases the devices.</summary>
    public void Dispose()
    {
        keys.Changed -= OnKeysChanged;
        DeviceLifecycle.StopWatching(); // Windows stops notifying (Package 137); a microphone open still queued finds the devices gone
        playbackLoop.Stop();
        lock (gate) DeviceLifecycle.Close();
    }
}
