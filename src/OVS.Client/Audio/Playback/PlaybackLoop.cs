using System.Diagnostics;
using OVS.Client.Audio.Capture;
using OVS.Client.Audio.Codec;
using OVS.Client.Audio.Devices;
using OVS.Client.Audio.Sounds;
using OVS.Client.Audio.Transmit;

namespace OVS.Client.Audio.Playback;

/// <summary>
/// Package 124: the playback thread. It mixes the voices and the tones into a frame whenever the speaker's buffer runs
/// below its target, or every 20 ms on its own clock while there is no speaker (the audio is thrown away, so reception
/// and indicators still work). It starts on demand, sleeps while there is nothing to play and ends when nothing is needed
/// any more. The speaker is the <see cref="DeviceLifecycle"/>'s: opened at the thread's start, closed at its end under
/// the engine's lock, which it is handed. The engine's flags reach it through readers.
/// </summary>
#pragma warning disable CA1001 // one wake event per audio engine for the life of the app; the playback thread may still wait on it after a stop
internal sealed class PlaybackLoop
#pragma warning restore CA1001
{
    /// <summary>How much the loop keeps buffered for the speaker; the echo canceller's delay hint starts from it.</summary>
    internal const int TargetBufferMs = 40;

    // Small buffers keep mouth-to-ear latency low; the refill loop wakes about every 15 ms.
    static readonly TimeSpan TargetBuffer = TimeSpan.FromMilliseconds(TargetBufferMs);

    // What the speaker device itself still holds after its buffer ran empty: its own buffer, plus 10 ms of margin.
    // Package 173 (A181): how often a waiting loop asks the native speaker whether its device stopped
    static readonly TimeSpan FailurePollInterval = TimeSpan.FromMilliseconds(100);

    static readonly TimeSpan DeviceLatency = TimeSpan.FromMilliseconds(WasapiHardware.SpeakerBufferMs + 10);

    readonly object gate; // the engine's: the devices and settings
    readonly object threadGate = new(); // the playback thread's start and end; never held while taking gate
    readonly DeviceLifecycle devices;
    readonly VoiceMixer mixer;
    readonly SoundQueue sounds;
    readonly Func<bool> needed, selfTest, deafened;
    Thread? playbackThread;
    readonly AutoResetEvent wake = new(false);
    volatile bool stopped; // the playback thread's end
    readonly SpeakerReportThrottle speakerReports = new();
    readonly float[] farEnd = new float[AudioFormat.FrameSamples]; // Package 142: playback thread only
    readonly float[] heard = new float[AudioFormat.StereoFrameSamples]; // Package 174: voices and tones, while the speaker plays the tones itself
    IPlaybackBuffer? attachedTo; // Package 176: the speaker the mixer was offered to (playback thread only)
    bool mixedBySpeaker; // Package 176: that speaker mixes the voices itself
    long mixes;

    /// <param name="gate">The engine's lock over the devices; the speaker closes under it.</param>
    /// <param name="devices">Opens the speaker, holds it and the capture whose echo canceller hears the mix.</param>
    /// <param name="mixer">The voices, ticked once per frame.</param>
    /// <param name="sounds">The tones, mixed on top of the voices.</param>
    /// <param name="needed">Whether the engine needs the playback thread now (connected or in the self test), even with nothing to play.</param>
    /// <param name="selfTest">Package 53: the own voice comes back, also while deafened, and keeps the loop mixing.</param>
    /// <param name="deafened">The voices are silenced; the tones still play.</param>
    internal PlaybackLoop(object gate, DeviceLifecycle devices, VoiceMixer mixer, SoundQueue sounds, Func<bool> needed, Func<bool> selfTest,
        Func<bool> deafened)
    {
        this.gate = gate;
        this.devices = devices;
        this.mixer = mixer;
        this.sounds = sounds;
        this.needed = needed;
        this.selfTest = selfTest;
        this.deafened = deafened;
    }

    /// <summary>Package 124: tests count the frames mixed.</summary>
    internal long MixCount => Interlocked.Read(ref mixes);

    /// <summary>Whether the playback thread runs, which needs the speaker.</summary>
    internal bool Running
    {
        get { lock (threadGate) return playbackThread is not null; }
    }

    internal float LastOutputLevelDb { get; private set; } = -120f;

    internal event Action<List<ActiveSpeaker>>? SpeakersChanged;

    /// <summary>Something to play arrived: the playback loop runs (started if needed) and is awake.</summary>
    internal void Kick()
    {
        Start();
        wake.Set();
    }

    void Start()
    {
        lock (threadGate)
        {
            if (playbackThread is not null || stopped) return;
            playbackThread = new Thread(Run) { IsBackground = true, Name = "Playback" };
            playbackThread.Start();
        }
    }

    /// <summary>The idle loop looks again whether it has work or may end.</summary>
    internal void Wake() => wake.Set();

    /// <summary>A voice to play or conceal, a tone, the self test, or a speaker report still due (the UI clears its marks).</summary>
    bool HasWork => selfTest() || sounds.Count > 0 || mixer.HasWork || speakerReports.Pending;

    void Run()
    {
        devices.OpenSpeakerAndReport();
        var clock = Stopwatch.StartNew(); // clock: real time
        long ticks = 0;
        while (!stopped)
        {
            if (!HasWork)
            {
                if (!needed())
                {
                    if (TryEnd()) return;
                    continue; // something came up meanwhile: look again instead of waiting for a wake that may not come
                }
                devices.Speaker?.PollFailure(); // Package 173 (A181): a device that stopped while nothing played is reported too
                wake.WaitOne(FailurePollInterval); // clock: real time; a voice, a tone, the self test or the end wakes it at once
                clock.Restart(); // the own clock starts anew: the first frame at once, no catching up on the pause
                ticks = 0;
                continue;
            }
            var buffer = devices.Speaker;
            if (buffer is not null && !ReferenceEquals(buffer, attachedTo))
            {
                attachedTo = buffer;
                mixedBySpeaker = buffer.AttachMixer(mixer); // Package 176: a native speaker mixes the voices on its render thread
            }
            if (buffer is null || mixedBySpeaker)
            {
                // No speaker: keep mixing on our own clock and throw the audio away. Package 176: a speaker that mixes
                // itself: follow it on the same clock (the tones' ends, who speaks, the level), writing nothing.
                buffer?.PollFailure();
                if (clock.Elapsed < AudioFormat.FrameDuration * ticks)
                {
                    wake.WaitOne(5); // clock: real time, new work or the stop wakes it at once
                    continue;
                }
                ticks++;
                if (buffer is null) Mix();
                else Follow(buffer);
                continue;
            }
            buffer.PollFailure(); // Package 173: a device that stopped is reported once, also while the full buffer keeps the loop waiting
            if (buffer.BufferedDuration >= TargetBuffer)
            {
                wake.WaitOne(5); // clock: real time, new work or the stop wakes it at once
                continue;
            }
            bool tonesPlayedBySpeaker = buffer.PlaysSounds;
            if (tonesPlayedBySpeaker) sounds.HandOver(buffer); // Package 174: they start with the frame written next
            var frame = MixForSpeaker(tonesPlayedBySpeaker);
            buffer.AddSamples(frame);
            ticks = (long)(clock.Elapsed / AudioFormat.FrameDuration);
        }
    }

    /// <summary>
    /// Package 176: one frame's turn beside a speaker that mixes the voices itself: hands it the new tones, advances them
    /// (so the loop knows when they end), and reports who it heard and the level from what its render thread mixed.
    /// </summary>
    void Follow(IPlaybackBuffer speaker)
    {
        sounds.HandOver(speaker);
        Array.Clear(heard);
        sounds.MixInto(heard);
        var heardNow = mixer.ReadActive();
        LastOutputLevelDb = mixer.LastLevelDb;
        if (speakerReports.Next(heardNow) is { } report) SpeakersChanged?.Invoke(report);
    }

    /// <summary>Not needed any more and nothing left: closes the speaker once the last frames played, and ends the thread.</summary>
    /// <returns>False when something came up meanwhile.</returns>
    bool TryEnd()
    {
        // the end of the last tone: what is still buffered, plus what the device itself holds (DeviceLatency)
        if (devices.Speaker?.BufferedDuration is { } left) wake.WaitOne(left + DeviceLatency); // clock: real time, new work or the stop ends the wait
        lock (gate)
        lock (threadGate)
        {
            if (needed() || HasWork) return false;
            devices.CloseSpeaker();
            playbackThread = null;
            return true;
        }
    }

    /// <summary>
    /// Package 140: a frame for the speaker, which is also the echo canceller's far end: exactly what goes to the speaker
    /// (voices after the limiter, and the tones), also in the self test, where it keeps the own voice from coming back
    /// through the speakers. Playback thread; internal so tests mix by hand. Package 142: the frame is stereo, the
    /// canceller hears it downmixed (a mono voice exactly as before); in music mode the capture does not use it.
    /// </summary>
    /// <param name="tonesPlayedBySpeaker">Package 174: the speaker mixes the tones itself, so the frame holds the voices only and the far end both.</param>
    internal float[] MixForSpeaker(bool tonesPlayedBySpeaker = false)
    {
        var frame = Mix(tonesPlayedBySpeaker);
        if (devices.CaptureSource.Capture is { EchoCancellation: true, Music: false, Canceller: { } canceller })
        {
            SampleConversion.Downmix(tonesPlayedBySpeaker ? heard : frame, farEnd);
            canceller.Render(farEnd);
        }
        return frame;
    }

    float[] Mix(bool tonesPlayedBySpeaker = false)
    {
        Interlocked.Increment(ref mixes);
        var (frame, active) = mixer.Tick();
        if (deafened() && !selfTest()) Array.Clear(frame); // in the self test only the own voice is in the mixer
        LastOutputLevelDb = VoiceActivityDetector.LevelDb(frame, 2); // voice only, before the tones; Package 142: the louder side
        if (tonesPlayedBySpeaker)
        {
            frame.CopyTo(heard, 0);
            sounds.MixInto(heard); // Package 174: advances the tones the speaker plays, for the far end and their end
        }
        else sounds.MixInto(frame);
        if (speakerReports.Next(active) is { } report) SpeakersChanged?.Invoke(report);
        return frame;
    }

    /// <summary>
    /// The engine goes away: ends the thread (waiting for it at most 500 ms) and refuses a later start; closes nothing,
    /// the speaker stays the <see cref="DeviceLifecycle"/>'s to close.
    /// </summary>
    internal void Stop()
    {
        stopped = true;
        wake.Set();
        Thread? thread;
        lock (threadGate) thread = playbackThread;
        thread?.Join(500); // clock: real time, bounded for a device call that blocks (the loop itself wakes at once)
    }
}
