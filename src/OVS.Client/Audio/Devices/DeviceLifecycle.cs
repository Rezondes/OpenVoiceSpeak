using System.Diagnostics;
using OVS.Client.Audio.Capture;
using OVS.Client.Localization;

namespace OVS.Client.Audio.Devices;

/// <summary>
/// Package 124, 137: opens a configured capture and the speaker, closes them, and reopens them after
/// device changes once those have been quiet. The capture comes from its <see cref="CaptureSource"/> (the microphone
/// or, while one is set, a test tone in its place), which opens, holds and closes it; this type decides when. It runs
/// under the audio engine's lock, which it is handed (no lock of its own; a member marked "under gate" asserts that its
/// caller holds it). The engine configures each new capture through the callback it hands in, before the capture
/// starts, and turns what failed into a notice.
/// </summary>
public sealed class DeviceLifecycle
{
    readonly object gate; // the engine's: the devices and settings
    readonly IAudioHardware? hardware;
    readonly Func<bool> micWanted, playbackRunning;
    readonly Action captureClosed;
    readonly Action<IReadOnlyList<string>> report;
    readonly ITimer? settle; // Package 137: the reopen after device changes, once they have been quiet
    bool running = true; // under gate
    string? inputId, outputId; // the chosen devices, null for "Standard"; under gate
    volatile PlaybackBuffer? speaker; // the open speaker
    readonly Endpoint micEndpoint, speakerEndpoint; // Package 137, under gate (but StreamError)
    bool deviceReopen; // Package 137, under gate: the open running is the one after a device change

    /// <param name="gate">The engine's lock over the devices and its settings; every open and close runs under it.</param>
    /// <param name="hardware">Package 124: where microphone and speaker come from; null runs without them (--no-audio, tests).</param>
    /// <param name="time">Package 137: the clock of the settle timer and of a stream error's age.</param>
    /// <param name="micWanted">Package 124: whether the microphone is needed now (the engine's flags).</param>
    /// <param name="playbackRunning">Whether the playback thread runs, which needs the speaker.</param>
    /// <param name="configure">The engine's set-up of a new capture, handed to the <see cref="CaptureSource"/> (see there).</param>
    /// <param name="captureClosed">Package 131: a capture was closed for a reopen after a device change or by <see cref="Close"/>.</param>
    /// <param name="report">Package 124: the warnings of one round of opens in the background, for one notice; called only with some.</param>
    internal DeviceLifecycle(object gate, IAudioHardware? hardware, TimeProvider time, Func<bool> micWanted, Func<bool> playbackRunning,
        Action<CapturePipeline> configure, Action captureClosed, Action<IReadOnlyList<string>> report)
    {
        this.gate = gate;
        this.hardware = hardware;
        this.micWanted = micWanted;
        this.playbackRunning = playbackRunning;
        this.captureClosed = captureClosed;
        this.report = report;
        micEndpoint = new Endpoint(time);
        speakerEndpoint = new Endpoint(time);
        CaptureSource = new CaptureSource(gate, hardware, configure);
        if (hardware is null) return;
        settle = time.CreateTimer(_ => ReopenAfterChange(), null, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
        hardware.Changed += OnDeviceChanged;
        hardware.Failed += OnDeviceFailed;
    }

    /// <summary>
    /// Whether it runs on a microphone and a speaker (false with --no-audio and in most tests): only then is there a
    /// device list to load and a device restart that may take long.
    /// </summary>
    public bool OpensDevices => hardware is not null;

    /// <summary>The capture and where it comes from (the microphone or the test tone); opened and closed as this type decides.</summary>
    public CaptureSource CaptureSource { get; }

    /// <summary>
    /// Package 137: what one direction opened last and how often it failed in a row, so a device change reopens it only
    /// when another device would open now or its stream broke, and a device that keeps failing is given up. Under gate,
    /// except <see cref="StreamError"/>.
    /// </summary>
    sealed class Endpoint(TimeProvider time)
    {
        long openedAt;
        int failures; // in a row on Id: opens that threw, stream errors soon after opening

        /// <summary>What the last open resolved to.</summary>
        public string? Id { get; private set; }

        public bool FellBack; // the user was told the chosen device is missing
        public Exception? StreamError; // set on device threads, taken when the devices settled

        /// <summary>An open of <paramref name="id"/> begins; another device than last time starts its failure count anew.</summary>
        public void Opened(string? id)
        {
            if (id != Id) failures = 0;
            Id = id;
            openedAt = time.GetTimestamp();
        }

        /// <summary>The open threw.</summary>
        public void OpenFailed() => failures++;

        /// <summary>The stream broke: soon after opening it counts as the open failing again, later as a new first trouble.</summary>
        public void StreamBroke() => failures = time.GetElapsedTime(openedAt) < StableAfter ? failures + 1 : 1;

        /// <summary>The device is not tried again until another one would open or the devices are chosen again.</summary>
        public bool GivenUp => failures >= MaxFailures;

        /// <summary>Saving the settings tries a given-up device again.</summary>
        public void Reset() => failures = 0;
    }

    /// <summary>Package 137: after this many failures in a row the same device is not tried again until another would open.</summary>
    const int MaxFailures = 3;

    /// <summary>Package 137: a stream error later than this after opening is a new trouble, not the open failing.</summary>
    static readonly TimeSpan StableAfter = TimeSpan.FromSeconds(10);

    /// <summary>Package 137 (A146): how long device changes must have been quiet before what is open reopens (Fluxer's 800 ms).</summary>
    public static readonly TimeSpan SettleTime = TimeSpan.FromMilliseconds(800);

    /// <summary>Package 137: on a COM or device thread, so it only (re)arms the timer: each change starts the quiet time anew.</summary>
    void OnDeviceChanged()
    {
        try
        {
            settle?.Change(SettleTime, Timeout.InfiniteTimeSpan);
        }
        catch (ObjectDisposedException)
        {
            // a change arriving while the engine goes away
        }
    }

    /// <summary>Package 137: an open device's stream broke; on its own thread, so it only marks it and arms the timer.</summary>
    void OnDeviceFailed(AudioDirection direction, Exception error)
    {
        Volatile.Write(ref (direction == AudioDirection.Input ? micEndpoint : speakerEndpoint).StreamError, error);
        OnDeviceChanged();
    }

    /// <summary>
    /// Package 137 (A146): the devices settled. Each direction in use opens again only when another device would open
    /// now (the chosen one went or came back, "Standard" got a new default) or its stream broke, so the many unrelated
    /// notifications (sleeping monitors, Bluetooth profiles, docks) leave a running microphone and speaker alone.
    /// Timer thread.
    /// </summary>
    void ReopenAfterChange()
    {
        var warnings = new List<string>();
        lock (gate)
        {
            if (!running) return;
            deviceReopen = true;
            try
            {
                ReopenIfDue(micEndpoint, AudioDirection.Input, inputId, CaptureSource.UsesMicrophone && micWanted(), CaptureSource.Capture is not null, ReopenCapture, Strings.Audio_MicFailed, warnings);
                ReopenIfDue(speakerEndpoint, AudioDirection.Output, outputId, SpeakerInUse, speaker is not null, ReopenSpeaker, Strings.Audio_SpeakerFailed, warnings);
            }
            finally
            {
                deviceReopen = false;
            }
        }
        if (warnings.Count > 0) report(warnings);
    }

    /// <summary>Package 137: one direction after a device change; a failure becomes a notice in its own words. Under gate.</summary>
    void ReopenIfDue(Endpoint endpoint, AudioDirection direction, string? chosen, bool needed, bool open, Action<List<string>> reopen, string failedText, List<string> warnings)
    {
        Debug.Assert(Monitor.IsEntered(gate));
        var error = Interlocked.Exchange(ref endpoint.StreamError, null);
        if (!needed || hardware is null) return;
        try
        {
            if (hardware.FindCurrentDeviceId(chosen, direction) == endpoint.Id)
            {
                if (open && error is null) return; // an unrelated change: the device in use stays as it is
                if (error is not null) endpoint.StreamBroke();
                if (endpoint.GivenUp)
                {
                    // given up on this device until another one would open or the settings are saved; a failed open told the user already
                    if (error is not null) warnings.Add(string.Format(failedText, error.Message));
                    return;
                }
            }
            reopen(warnings);
        }
        catch (Exception exception) // a background thread must not take the client down: the user gets a notice instead
        {
            warnings.Add(string.Format(failedText, exception.Message));
        }
    }

    /// <summary>Package 137: under gate.</summary>
    void ReopenCapture(List<string> warnings)
    {
        Debug.Assert(Monitor.IsEntered(gate));
        CaptureSource.Close();
        captureClosed();
        SyncCapture(warnings);
    }

    /// <summary>Package 137: under gate.</summary>
    void ReopenSpeaker(List<string> warnings)
    {
        Debug.Assert(Monitor.IsEntered(gate));
        CloseSpeaker();
        OpenSpeaker(warnings);
    }

    /// <summary>Package 137: what a direction opens now, for its failure count. Under gate.</summary>
    void RecordOpen(Endpoint endpoint, string? chosen, AudioDirection direction)
    {
        Debug.Assert(Monitor.IsEntered(gate));
        endpoint.Opened(hardware!.FindCurrentDeviceId(chosen, direction));
    }

    /// <summary>
    /// Package 137: the devices chosen in the settings (null: "Standard"), opened from now on; saving tries a given-up
    /// device again. Under gate.
    /// </summary>
    internal void Choose(string? inputId, string? outputId)
    {
        Debug.Assert(Monitor.IsEntered(gate));
        this.inputId = inputId;
        this.outputId = outputId;
        micEndpoint.Reset();
        speakerEndpoint.Reset();
    }

    /// <summary>
    /// Package 137: the hardware's fallback warning as it reaches the user. After a device change it names the device
    /// that went missing, once until that device is back; any other open warns as before. Under gate.
    /// </summary>
    void ReportFallback(List<string> opened, Endpoint endpoint, string lost, List<string> warnings)
    {
        Debug.Assert(Monitor.IsEntered(gate));
        bool now = opened.Remove(Strings.Audio_DeviceFallback);
        if (now && !(deviceReopen && endpoint.FellBack)) warnings.Add(deviceReopen ? lost : Strings.Audio_DeviceFallback);
        endpoint.FellBack = now;
        warnings.AddRange(opened);
    }

    /// <summary>The open speaker, or null; the playback thread reads it per frame.</summary>
    internal PlaybackBuffer? Speaker => speaker;

    /// <summary>The speaker is open, or the playback thread runs and needs it. Under gate.</summary>
    bool SpeakerInUse
    {
        get
        {
            Debug.Assert(Monitor.IsEntered(gate));
            return speaker is not null || playbackRunning();
        }
    }

    public void SetTone(double? frequency)
    {
        lock (gate)
        {
            CaptureSource.ToneHz = frequency;
            CaptureSource.Close();
            SyncCapture([]);
        }
    }

    /// <summary>
    /// The settings were saved: closes the capture and the speaker, runs <paramref name="beforeOpen"/> (also when closing
    /// threw), then opens the capture as needed and the speaker if it was in use. Under gate.
    /// </summary>
    /// <returns>The warnings of the opens.</returns>
    internal IReadOnlyList<string> Restart(Action beforeOpen)
    {
        Debug.Assert(Monitor.IsEntered(gate));
        var warnings = new List<string>();
        bool speakerInUse = SpeakerInUse;
        try
        {
            Close();
        }
        finally
        {
            beforeOpen();
        }
        SyncCapture(warnings);
        if (speakerInUse) OpenSpeaker(warnings); // a device changed while connected takes effect at once
        return warnings;
    }

    /// <summary>
    /// Package 124: opens or closes the microphone as the engine's flags want it, on a pool thread; a failure becomes a
    /// notice instead of taking the client down.
    /// </summary>
    internal void SyncCaptureAndReport()
    {
        var warnings = new List<string>();
        try
        {
            lock (gate) SyncCapture(warnings);
        }
        catch (Exception exception) // a background thread must not take the client down: the user gets a notice instead
        {
            warnings.Add(string.Format(Strings.Audio_MicFailed, exception.Message));
        }
        if (warnings.Count > 0) report(warnings);
    }

    /// <summary>The playback thread's start: opens the speaker if needed; a failure becomes a notice and the mixing goes on without it.</summary>
    internal void OpenSpeakerAndReport()
    {
        var warnings = new List<string>();
        try
        {
            lock (gate) OpenSpeaker(warnings);
        }
        catch (Exception exception) // mixing goes on without a speaker instead of the thread taking the client down
        {
            warnings.Add(string.Format(Strings.Audio_SpeakerFailed, exception.Message));
        }
        if (warnings.Count > 0) report(warnings);
    }

    /// <summary>Opens or closes the capture as needed: the tone always, the microphone while wanted. Under gate.</summary>
    void SyncCapture(List<string> warnings)
    {
        Debug.Assert(Monitor.IsEntered(gate));
        if (!running) return; // an open queued before the engine's Dispose comes too late
        bool needed = !CaptureSource.UsesMicrophone || (micWanted() && hardware is not null);
        if (needed && CaptureSource.Capture is null) StartCapture(warnings);
        else if (!needed) CaptureSource.Close();
    }

    /// <summary>Under gate.</summary>
    void StartCapture(List<string> warnings)
    {
        Debug.Assert(Monitor.IsEntered(gate));
        var opened = new List<string>();
        bool device = CaptureSource.UsesMicrophone; // Package 137: the test tone leaves the microphone's state alone
        try
        {
            if (device) RecordOpen(micEndpoint, inputId, AudioDirection.Input);
            CaptureSource.Open(inputId, opened);
        }
        catch (Exception exception) // the open, set-up and start of one capture: a device that cannot open throws (IAudioHardware)
        {
            CaptureSource.Close();
            warnings.Add(string.Format(Strings.Audio_MicFailed, exception.Message));
            if (device) micEndpoint.OpenFailed();
        }
        finally
        {
            if (device) ReportFallback(opened, micEndpoint, Strings.Audio_MicLost, warnings);
        }
    }

    /// <summary>Under gate.</summary>
    void OpenSpeaker(List<string> warnings)
    {
        Debug.Assert(Monitor.IsEntered(gate));
        if (hardware is null || speaker is not null || !running) return;
        var opened = new List<string>();
        try
        {
            RecordOpen(speakerEndpoint, outputId, AudioDirection.Output);
            speaker = hardware.OpenOutput(outputId, opened);
        }
        catch (Exception exception) // a device that cannot open throws (IAudioHardware)
        {
            warnings.Add(string.Format(Strings.Audio_SpeakerFailed, exception.Message));
            speakerEndpoint.OpenFailed();
        }
        finally
        {
            ReportFallback(opened, speakerEndpoint, Strings.Audio_SpeakerLost, warnings);
        }
    }

    /// <summary>Under gate.</summary>
    internal void CloseSpeaker()
    {
        Debug.Assert(Monitor.IsEntered(gate));
        var open = speaker;
        speaker = null;
        open?.Dispose();
    }

    /// <summary>Closes the capture and the speaker. Under gate.</summary>
    internal void Close()
    {
        Debug.Assert(Monitor.IsEntered(gate));
        CaptureSource.Close();
        captureClosed(); // Package 131: no microphone, no voice
        CloseSpeaker();
    }

    /// <summary>
    /// The engine goes away: unsubscribes from the hardware (Package 137: Windows stops notifying), stops the settle
    /// timer and refuses later opens, also one already queued; closes nothing (<see cref="Close"/> does).
    /// </summary>
    internal void StopWatching()
    {
        if (hardware is not null)
        {
            hardware.Changed -= OnDeviceChanged;
            hardware.Failed -= OnDeviceFailed;
        }
        settle?.Dispose();
        lock (gate) running = false;
    }
}
