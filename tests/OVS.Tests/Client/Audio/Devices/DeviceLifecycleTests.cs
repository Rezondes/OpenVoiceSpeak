using OVS.Client.Audio.Capture;
using OVS.Client.Audio.Devices;
using OVS.Client.Localization;
using OVS.Tests.TestSupport;

namespace OVS.Tests.Client.Audio.Devices;

/// <summary>
/// Package 124 (the devices open only while needed), 137 (device changes and broken streams), 131 (a closed capture told
/// to the engine): the capture (microphone or test tone) and the speaker opened, closed and reopened on a fake hardware
/// and a manual clock. The engine's side is played by fields: its lock, whether the microphone is wanted and the playback
/// thread runs, the configuring of a new capture, the closed captures and the reports. Everything runs on the test
/// thread (the settle timer fires inside <c>Advance</c>). How a capture is opened from its source and set up is in
/// <c>CaptureSourceTests</c>.
/// </summary>
public sealed class DeviceLifecycleTests : IDisposable
{
    readonly object gate = new();
    readonly FakeAudioHardware hardware = new();
    readonly ManualTimeProvider time = new();
    readonly List<CapturePipeline> configured = [];
    readonly List<IReadOnlyList<string>> reports = [];
    readonly DeviceLifecycle devices;
    bool micWanted, playbackRunning;
    Exception? configureFails;
    int capturesClosed;
    int levels; // frames the configured captures delivered once started

    public DeviceLifecycleTests() => devices = Create(hardware);

    /// <summary>A lifecycle on the fields above; without hardware it runs as with --no-audio.</summary>
    DeviceLifecycle Create(IAudioHardware? on) =>
        new(gate, on, time, () => micWanted, () => playbackRunning, Configure, () => capturesClosed++, reports.Add);

    void Configure(CapturePipeline capture)
    {
        configured.Add(capture);
        capture.Level += _ => Interlocked.Increment(ref levels);
        if (configureFails is { } error) throw error;
    }

    /// <summary>Every reported text, in order.</summary>
    List<string> Notices => [.. reports.SelectMany(report => report)];

    public void Dispose()
    {
        devices.StopWatching();
        lock (gate) devices.Close();
    }

    /// <summary>The devices chosen in the settings, under the lock as the engine does.</summary>
    void Choose(string? input)
    {
        lock (gate) devices.Choose(input, null);
    }

    /// <summary>A device change, and the quiet time after it runs out.</summary>
    void Change()
    {
        hardware.RaiseChanged();
        time.Advance(DeviceLifecycle.SettleTime);
    }

    /// <summary>The stream of an open device breaks, and the quiet time after it runs out.</summary>
    void Break(AudioDirection direction)
    {
        hardware.RaiseFailed(direction);
        time.Advance(DeviceLifecycle.SettleTime);
    }

    [Fact]
    public void SyncCaptureAndReport_MicWanted_OpensTheChosenMic()
    {
        hardware.Plug("headset", AudioDirection.Input);
        Choose("headset");
        micWanted = true;

        devices.SyncCaptureAndReport();

        Assert.Empty(reports);
        Assert.Equal(("headset", 1), (hardware.Open.Mic, hardware.Counts.MicsOpen));
    }

    [Fact]
    public void SyncCaptureAndReport_MicNoLongerWanted_ClosesIt()
    {
        micWanted = true;
        devices.SyncCaptureAndReport();
        micWanted = false;
        devices.SyncCaptureAndReport();

        Assert.Equal((1, 0), (hardware.Counts.Mics, hardware.Counts.MicsOpen));
        Assert.Null(devices.CaptureSource.Capture);
    }

    [Fact]
    public void SyncCaptureAndReport_NoMicrophoneLeft_ReportsTheOpensMessage()
    {
        hardware.DefaultMic = null; // every microphone unplugged: the open throws
        micWanted = true;

        devices.SyncCaptureAndReport();

        Assert.Equal([string.Format(Strings.Audio_MicFailed, "no microphone")], Assert.Single(reports));
        Assert.Null(devices.CaptureSource.Capture);
        Assert.Empty(configured);
    }

    [Fact]
    public void SyncCaptureAndReport_ConfigureThrows_ClosesTheOpenedCaptureAndReports()
    {
        configureFails = new InvalidOperationException("configure failed");
        micWanted = true;

        devices.SyncCaptureAndReport();

        Assert.Equal((1, 0), (hardware.Counts.Mics, hardware.Counts.MicsOpen));
        Assert.True(Assert.Single(configured).IsDisposed);
        Assert.Null(devices.CaptureSource.Capture);
        Assert.Equal([string.Format(Strings.Audio_MicFailed, "configure failed")], Assert.Single(reports));
    }

    [Fact]
    public void SyncCaptureAndReport_NoHardware_OpensNoMicrophone()
    {
        var without = Create(null);
        micWanted = true;

        without.SyncCaptureAndReport();

        Assert.False(without.OpensDevices);
        Assert.Null(without.CaptureSource.Capture);
        Assert.Empty(reports);
    }

    [Fact]
    public void OpenSpeakerAndReport_CalledTwice_OpensOneSpeaker()
    {
        devices.OpenSpeakerAndReport();
        devices.OpenSpeakerAndReport();

        Assert.Equal((1, 1), (hardware.Counts.Speakers, hardware.Counts.SpeakersOpen));
        Assert.NotNull(devices.Speaker);
        Assert.Empty(reports);
    }

    [Fact]
    public void OpenSpeakerAndReport_AfterStopWatching_OpensNothing()
    {
        devices.StopWatching();

        devices.OpenSpeakerAndReport();

        Assert.Equal((0, 0, 0, 0), hardware.Counts);
        Assert.Empty(reports);
    }

    [Fact]
    public void CloseSpeaker_SpeakerOpen_ClosesIt()
    {
        devices.OpenSpeakerAndReport();

        lock (gate) devices.CloseSpeaker();

        Assert.Equal((1, 0), (hardware.Counts.Speakers, hardware.Counts.SpeakersOpen));
        Assert.Null(devices.Speaker);
    }

    [Fact]
    public void Close_MicAndSpeakerOpen_ClosesBothAndTellsTheCaptureClosed()
    {
        micWanted = true;
        devices.SyncCaptureAndReport();
        devices.OpenSpeakerAndReport();

        lock (gate) devices.Close();

        Assert.Equal((1, 0, 1, 0), hardware.Counts);
        Assert.Null(devices.CaptureSource.Capture);
        Assert.Null(devices.Speaker);
        Assert.Equal(1, capturesClosed);
    }

    [Fact]
    public void Restart_MicWantedAndSpeakerOpen_ReopensBothOnTheChosenDevicesAfterBeforeOpen()
    {
        micWanted = true;
        devices.SyncCaptureAndReport();
        devices.OpenSpeakerAndReport();
        hardware.Plug("headset", AudioDirection.Input);
        var atBeforeOpen = (0, 0, 0, 0);

        IReadOnlyList<string> warnings;
        lock (gate)
        {
            devices.Choose("headset", null);
            warnings = devices.Restart(() => atBeforeOpen = hardware.Counts);
        }

        Assert.Equal((1, 0, 1, 0), atBeforeOpen); // both closed, nothing opened yet
        Assert.Equal(("headset", "speaker-default"), hardware.Open);
        Assert.Equal((2, 1, 2, 1), hardware.Counts);
        Assert.Empty(warnings);
    }

    [Fact]
    public void Restart_SpeakerNeitherOpenNorPlaying_OpensOnlyTheCapture()
    {
        micWanted = true;
        devices.SyncCaptureAndReport();

        lock (gate) devices.Restart(() => { });

        Assert.Equal((2, 1, 0, 0), hardware.Counts);
    }

    [Fact]
    public void Choose_MicGivenUp_ReopensAtTheNextStreamError()
    {
        micWanted = true;
        devices.SyncCaptureAndReport();
        for (int index = 0; index < 3; index++) Break(AudioDirection.Input);
        Assert.Equal(3, hardware.Counts.Mics); // two reopens, then given up with a notice
        Assert.Single(Notices);

        Choose(null);
        Break(AudioDirection.Input);

        Assert.Equal((4, 1), (hardware.Counts.Mics, hardware.Counts.MicsOpen));
        Assert.Single(Notices);
    }

    [Fact]
    public void SetTone_MicOpen_ReplacesItUntilTheToneEnds()
    {
        micWanted = true;
        devices.SyncCaptureAndReport();
        try
        {
            devices.SetTone(440);
            Assert.Equal(440, devices.CaptureSource.ToneHz);
            Assert.Equal((1, 0), (hardware.Counts.Mics, hardware.Counts.MicsOpen)); // the tone is no device
            Assert.Same(configured[^1], devices.CaptureSource.Capture);
        }
        finally
        {
            devices.SetTone(null);
        }

        Assert.Equal((2, 1), (hardware.Counts.Mics, hardware.Counts.MicsOpen));
        Assert.Equal(3, configured.Count);
    }

    [Fact]
    public void SetTone_NoHardware_OpensTheToneUntilItEnds()
    {
        var without = Create(null);
        try
        {
            without.SetTone(440);
            Assert.Same(Assert.Single(configured), without.CaptureSource.Capture);
        }
        finally
        {
            without.SetTone(null);
        }

        Assert.Null(without.CaptureSource.Capture);
    }

    [Fact]
    public async Task SetTone_NoMicWanted_StartsTheToneWithoutADevice()
    {
        try
        {
            devices.SetTone(440);

            await AsyncWait.UntilAsync(() => Volatile.Read(ref levels) > 0); // started: its thread delivers frames
            Assert.Equal((0, 0, 0, 0), hardware.Counts);
        }
        finally
        {
            devices.SetTone(null);
        }
    }

    [Fact]
    public void SetTone_AfterStopWatching_OpensNoTone()
    {
        devices.StopWatching();

        devices.SetTone(440);

        Assert.Equal(440, devices.CaptureSource.ToneHz);
        Assert.Null(devices.CaptureSource.Capture);
        Assert.Empty(configured);
    }

    [Fact]
    public void DeviceChange_ToneRunning_KeepsTheTone()
    {
        micWanted = true;
        try
        {
            devices.SetTone(440);
            var tone = devices.CaptureSource.Capture;
            hardware.DefaultMic = "usb-mic";

            Change();

            Assert.Same(tone, devices.CaptureSource.Capture);
            Assert.Equal(0, hardware.Counts.Mics);
        }
        finally
        {
            devices.SetTone(null);
        }
    }

    [Fact]
    public void DeviceChange_ChosenMicBack_ReopensItOnceQuiet()
    {
        Choose("headset");
        micWanted = true;
        devices.SyncCaptureAndReport();
        Assert.Equal([Strings.Audio_DeviceFallback], Assert.Single(reports)); // not plugged in: the default opens
        hardware.Plug("headset", AudioDirection.Input);

        hardware.RaiseChanged();
        time.Advance(DeviceLifecycle.SettleTime - TimeSpan.FromMilliseconds(1));
        Assert.Equal("mic-default", hardware.Open.Mic); // still within the quiet time
        time.Advance(TimeSpan.FromMilliseconds(1));

        Assert.Equal(("headset", 2, 1), (hardware.Open.Mic, hardware.Counts.Mics, hardware.Counts.MicsOpen));
        Assert.Same(configured[^1], devices.CaptureSource.Capture);
        Assert.Equal(1, capturesClosed);
        Assert.Single(reports);
    }

    [Fact]
    public void DeviceChanges_KeepComing_OneReopenOnceTheLastIsQuiet()
    {
        micWanted = true;
        devices.SyncCaptureAndReport();
        hardware.DefaultMic = "usb-mic";

        hardware.RaiseChanged();
        time.Advance(TimeSpan.FromMilliseconds(500));
        hardware.RaiseChanged(); // the quiet time starts anew
        time.Advance(DeviceLifecycle.SettleTime - TimeSpan.FromMilliseconds(1));
        Assert.Equal(1, hardware.Counts.Mics);
        time.Advance(TimeSpan.FromMilliseconds(1));
        Assert.Equal(("usb-mic", 2), (hardware.Open.Mic, hardware.Counts.Mics));
        time.Advance(TimeSpan.FromSeconds(5));

        Assert.Equal(2, hardware.Counts.Mics); // once
    }

    [Fact]
    public void DeviceChange_SameDevicesStillCurrent_ReopensNothing()
    {
        micWanted = true;
        devices.SyncCaptureAndReport();
        devices.OpenSpeakerAndReport();

        Change();

        Assert.Equal((1, 1, 1, 1), hardware.Counts);
        Assert.Equal(0, capturesClosed);
    }

    [Fact]
    public void DeviceChange_UnrelatedWhileOnTheFallback_ReopensNothingAndNoSecondNotice()
    {
        hardware.Plug("headset", AudioDirection.Input);
        Choose("headset");
        micWanted = true;
        devices.SyncCaptureAndReport();
        hardware.Unplug("headset");
        Change(); // on the default now, with the lost notice

        Change();

        Assert.Equal(("mic-default", 2, 1), (hardware.Open.Mic, hardware.Counts.Mics, hardware.Counts.MicsOpen));
        Assert.Equal([Strings.Audio_MicLost], Notices);
    }

    [Fact]
    public void DeviceChange_ChosenMicRemoved_DefaultOpensWithTheLostNotice()
    {
        hardware.Plug("headset", AudioDirection.Input);
        Choose("headset");
        micWanted = true;
        devices.SyncCaptureAndReport();

        hardware.Unplug("headset");
        Change();

        Assert.Equal(("mic-default", 1), (hardware.Open.Mic, hardware.Counts.MicsOpen));
        Assert.Equal([Strings.Audio_MicLost], Notices);
    }

    [Fact]
    public void DeviceChange_ChosenMicGoneAgainAfterReturning_LostNoticeAgain()
    {
        hardware.Plug("headset", AudioDirection.Input);
        Choose("headset");
        micWanted = true;
        devices.SyncCaptureAndReport();
        hardware.Unplug("headset");
        Change();
        hardware.Plug("headset", AudioDirection.Input);
        Change();

        hardware.Unplug("headset");
        Change();

        Assert.Equal([Strings.Audio_MicLost, Strings.Audio_MicLost], Notices);
    }

    [Fact]
    public void StreamFailure_SpeakerOpen_ReopensItOnTheSameDevice()
    {
        devices.OpenSpeakerAndReport();

        Break(AudioDirection.Output);

        Assert.Equal(("speaker-default", 2, 1), (hardware.Open.Speaker, hardware.Counts.Speakers, hardware.Counts.SpeakersOpen));
        Assert.Empty(reports);
    }

    [Fact]
    public void StreamFailures_MoreThanTenSecondsApart_NeverGivenUp()
    {
        micWanted = true;
        devices.SyncCaptureAndReport();

        for (int index = 0; index < 4; index++)
        {
            time.Advance(TimeSpan.FromSeconds(11)); // the reopened stream ran longer than 10 s
            Break(AudioDirection.Input);
        }

        Assert.Equal((5, 1), (hardware.Counts.Mics, hardware.Counts.MicsOpen));
        Assert.Empty(reports);
    }

    [Fact]
    public void StreamFailures_NewDeviceAfterGivingUp_GetsItsOwnThreeTries()
    {
        micWanted = true;
        devices.SyncCaptureAndReport();
        for (int index = 0; index < 3; index++) Break(AudioDirection.Input); // given up on the default
        hardware.DefaultMic = "usb-mic";
        Change();
        Assert.Equal(("usb-mic", 4), (hardware.Open.Mic, hardware.Counts.Mics));

        Break(AudioDirection.Input);
        Break(AudioDirection.Input);

        Assert.Equal((6, 1), (hardware.Counts.Mics, hardware.Counts.MicsOpen)); // both reopened: not given up
        Assert.Single(Notices); // the one for the default
    }

    [Fact]
    public void DeviceChange_SpeakerOpenFailedWhilePlaybackRuns_OpensTheNewDefault()
    {
        playbackRunning = true;
        hardware.DefaultSpeaker = null;
        devices.OpenSpeakerAndReport();
        Assert.Equal([string.Format(Strings.Audio_SpeakerFailed, "no speaker")], Assert.Single(reports));

        hardware.DefaultSpeaker = "usb-speaker";
        Change();

        Assert.Equal(("usb-speaker", 1), (hardware.Open.Speaker, hardware.Counts.SpeakersOpen));
        Assert.Single(reports);
    }

    [Fact]
    public void DeviceChange_SpeakerCannotStart_GivenUpAfterThreeFailedOpens()
    {
        playbackRunning = true;
        hardware.StartFails = true;
        devices.OpenSpeakerAndReport(); // the first failure

        for (int index = 0; index < 4; index++) Change(); // two more tries, then none

        var failed = string.Format(Strings.Audio_SpeakerFailed, "device cannot start");
        Assert.Equal([failed, failed, failed], Notices);
        Assert.Equal(0, hardware.Counts.Speakers);
    }

    [Fact]
    public void DeviceChange_MicGivenUp_LaterChangesAndTimeOpenNothing()
    {
        hardware.StartFails = true;
        micWanted = true;
        devices.SyncCaptureAndReport(); // the first failure
        Change();
        Change(); // the third: given up

        Change();
        time.Advance(TimeSpan.FromSeconds(30));

        var failed = string.Format(Strings.Audio_MicFailed, "device cannot start");
        Assert.Equal([failed, failed, failed], Notices); // no fourth try
        Assert.Null(devices.CaptureSource.Capture);
    }

    [Fact]
    public void DeviceChange_BothOpensThrow_OneReportWithBothFailures()
    {
        micWanted = true;
        devices.SyncCaptureAndReport();
        devices.OpenSpeakerAndReport();

        hardware.DefaultMic = hardware.DefaultSpeaker = null; // everything unplugged
        Change();

        Assert.Equal((1, 0, 1, 0), hardware.Counts);
        Assert.Equal([string.Format(Strings.Audio_MicFailed, "no microphone"), string.Format(Strings.Audio_SpeakerFailed, "no speaker")],
            Assert.Single(reports));
    }

    [Fact]
    public void StopWatching_ChangePending_LeavesTheHardwareAndOpensNothing()
    {
        Assert.Equal((1, 1), (hardware.Subscribers, hardware.FailedSubscribers));
        micWanted = true;
        hardware.DefaultMic = "usb-mic";
        hardware.RaiseChanged();

        devices.StopWatching();
        time.Advance(TimeSpan.FromSeconds(5));
        devices.SyncCaptureAndReport();

        Assert.Equal((0, 0), (hardware.Subscribers, hardware.FailedSubscribers));
        Assert.Equal((0, 0, 0, 0), hardware.Counts);
    }
}
