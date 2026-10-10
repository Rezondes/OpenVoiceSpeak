using OVS.Client.Audio;
using OVS.Client.Audio.Devices;
using OVS.Client.Audio.Transmit;
using OVS.Client.Localization;
using OVS.Client.Models;
using OVS.Client.Settings;
using OVS.Client.ViewModels;
using OVS.Shared.Permissions;
using OVS.Shared.Protocol.Messages.Users;
using OVS.Shared.Voice;
using OVS.Tests.TestSupport;
using static OVS.Tests.TestSupport.EngineProbe;
using static OVS.Tests.TestSupport.FakeAudioHardware;

namespace OVS.Tests.Client.Services;

/// <summary>
/// What <see cref="MainViewModel"/> hands its <see cref="OVS.Client.Services.EngineSync"/> and
/// <see cref="OVS.Client.Services.DeviceList"/> (the connection's state, the server's state, the saved volumes, the device
/// list and its page) and what it does with their answers, seen on the real engine: the rules themselves are in
/// <see cref="EngineSyncTests"/> and <see cref="DeviceListTests"/>. The channel bitrate, the channel mute, the slider and the
/// joining user are seen in <c>MainViewModelTests</c> (<c>Bitrate_FollowsCurrentChannel</c>, <c>Bitrate_Adapted_ResetOnMove</c>,
/// <c>ChannelMuted_HintAndNoSending</c>, <c>UserVolume_ChangesMixer_SavesByFingerprint</c>), the restart off the UI thread in
/// <c>MainViewModelCompositionTests</c>. Posts run at once, the devices are fakes.
/// </summary>
public sealed class EngineSyncWiringTests() : TempDirectoryTest("ovs-engine-sync-", quiet: true)
{
    MainViewModel CreateMainViewModel(FakeAudioHardware? hardware = null) => MainViewModelSetup.Create(dir, action => action(), hardware: hardware);

    /// <summary>Devices whose first two lists (the start's, one per direction) come at once and every later one waits for <paramref name="later"/>, then lists <paramref name="afterwards"/>.</summary>
    static FakeAudioHardware CreateHardwareWhoseLaterListsWait(ManualResetEventSlim later, Func<AudioDirection, List<AudioDevice>> afterwards)
    {
        int lists = 0;
        return new FakeAudioHardware
        {
            Listing = direction =>
            {
                if (Interlocked.Increment(ref lists) <= 2) return ListOneEach(direction);
                later.Wait(HangGuard.Limit);
                return afterwards(direction);
            },
        };
    }

    /// <summary>Opens the settings page, whose level meter opens the microphone, and waits until it is open.</summary>
    static async Task OpenSettingsWithMicrophoneAsync(MainViewModel mainViewModel, FakeAudioHardware hardware)
    {
        mainViewModel.OpenSettings();
        await AsyncWait.Until(() => hardware.Counts.MicsOpen == 1);
    }

    [Fact]
    public async Task ShowServer_NotMutedNotDeafened_EngineIsConnectedUnmutedWithTheSpeakRight()
    {
        await using var mainViewModel = CreateMainViewModel();

        MainViewModelSetup.ShowServer(mainViewModel, FakeServers.Admin());

        Assert.Equal(new EngineSwitches(Connected: true, SelfMuted: false, Deafened: false, HasSpeakLinked: true, ChannelMuted: false), ReadEngineSwitches(mainViewModel.Audio));
    }

    [Fact]
    public async Task StateChanged_SelfMuted_EngineIsOnlyMuted()
    {
        await using var mainViewModel = CreateMainViewModel();
        var server = MainViewModelSetup.ShowServer(mainViewModel, FakeServers.Admin())!;
        await server.SetSelfStateAsync(true, false);

        FakeServers.SetOwnRightsAndGroups(server, Permission.All, [WellKnownGroups.Admin]); // any state change makes the engine follow

        Assert.Equal(new EngineSwitches(Connected: true, SelfMuted: true, Deafened: false, HasSpeakLinked: true, ChannelMuted: false), ReadEngineSwitches(mainViewModel.Audio));
    }

    [Fact]
    public async Task StateChanged_SelfDeafened_EngineIsDeafenedAndMuted()
    {
        await using var mainViewModel = CreateMainViewModel();
        var server = MainViewModelSetup.ShowServer(mainViewModel, FakeServers.Admin())!;
        await server.SetSelfStateAsync(false, true);

        FakeServers.SetOwnRightsAndGroups(server, Permission.All, [WellKnownGroups.Admin]); // any state change makes the engine follow

        Assert.Equal(new EngineSwitches(Connected: true, SelfMuted: true, Deafened: true, HasSpeakLinked: true, ChannelMuted: false), ReadEngineSwitches(mainViewModel.Audio));
    }

    [Fact]
    public async Task RightsChanged_SpeakLinkedLost_EngineLosesOnlySpeakLinked()
    {
        await using var mainViewModel = CreateMainViewModel();
        var server = MainViewModelSetup.ShowServer(mainViewModel, FakeServers.Admin())!;

        FakeServers.SetOwnRightsAndGroups(server, Permission.Speak, [FakeServers.GuestGroup.Id]);

        Assert.Equal(new EngineSwitches(Connected: true, SelfMuted: false, Deafened: false, HasSpeakLinked: false, ChannelMuted: false), ReadEngineSwitches(mainViewModel.Audio));
    }

    [Fact]
    public async Task ShowServer_NoServerAfterOne_EngineIsNotConnectedMutedWithoutSpeakRight()
    {
        await using var mainViewModel = CreateMainViewModel();
        MainViewModelSetup.ShowServer(mainViewModel, FakeServers.Admin());

        MainViewModelSetup.ShowServer(mainViewModel, null);

        Assert.Equal(new EngineSwitches(Connected: false, SelfMuted: true, Deafened: false, HasSpeakLinked: false, ChannelMuted: false), ReadEngineSwitches(mainViewModel.Audio));
    }

    [Fact]
    public async Task ShowServer_UserWithSavedVolume_BuildsTheGainTableOnceGivesThemTheirGainAndOthersOne()
    {
        var saved = new ClientSettings();
        saved.SetVolume("fp2", 2f);
        saved.Save(dir);
        await using var mainViewModel = CreateMainViewModel();

        MainViewModelSetup.ShowServer(mainViewModel, FakeServers.Admin());

        Assert.Equal((1, 2f, 1f), (mainViewModel.EngineSync.GainTableBuilds, mainViewModel.Audio.Mixer.SpeakerGain(2), mainViewModel.Audio.Mixer.SpeakerGain(1)));
    }

    [Fact]
    public async Task ShowServer_AnotherServerAfterOne_BuildsTheGainTableAgain()
    {
        await using var mainViewModel = CreateMainViewModel();
        MainViewModelSetup.ShowServer(mainViewModel, FakeServers.Admin());

        MainViewModelSetup.ShowServer(mainViewModel, FakeServers.Admin());

        Assert.Equal(2, mainViewModel.EngineSync.GainTableBuilds);
    }

    [Fact]
    public async Task ShowServer_VolumesAppliedBefore_TableUsesThem()
    {
        await using var mainViewModel = CreateMainViewModel();
        var settings = new ClientSettings();
        settings.SetVolume("fp2", 2f);
        mainViewModel.ApplySettings(settings);

        MainViewModelSetup.ShowServer(mainViewModel, FakeServers.Admin());

        Assert.Equal(2f, mainViewModel.Audio.Mixer.SpeakerGain(2));
    }

    [Fact]
    public async Task StateChanged_UserLeft_DropsTheirDecoder()
    {
        await using var mainViewModel = CreateMainViewModel();
        var server = MainViewModelSetup.ShowServer(mainViewModel, FakeServers.Admin())!;
        PushFrameOf(mainViewModel.Audio.Mixer, 2);

        server.Apply(new UserLeft(2));

        Assert.Null(mainViewModel.Audio.Mixer.BufferOf(2));
    }

    [Fact]
    public async Task StateChanged_AnotherUserLeft_KeepsTheDecoderOfThoseStillThere()
    {
        await using var mainViewModel = CreateMainViewModel();
        var server = MainViewModelSetup.ShowServer(mainViewModel, FakeServers.Admin())!;
        PushFrameOf(mainViewModel.Audio.Mixer, 1);

        server.Apply(new UserLeft(2));

        Assert.NotNull(mainViewModel.Audio.Mixer.BufferOf(1));
    }

    [Fact]
    public async Task StateChanged_NobodyCameOrWent_DropsNoDecoder()
    {
        await using var mainViewModel = CreateMainViewModel();
        var server = MainViewModelSetup.ShowServer(mainViewModel, FakeServers.Admin())!;
        PushFrameOf(mainViewModel.Audio.Mixer, 99);

        FakeServers.SetOwnRightsAndGroups(server, Permission.All, [WellKnownGroups.Admin]); // any state change that is no member change

        Assert.NotNull(mainViewModel.Audio.Mixer.BufferOf(99));
    }

    [Fact]
    public async Task DisconnectAsync_SpeakersPresent_DropsEveryDecoder()
    {
        await using var mainViewModel = CreateMainViewModel();
        MainViewModelSetup.ShowServer(mainViewModel, FakeServers.Admin());
        PushFrameOf(mainViewModel.Audio.Mixer, 1);
        PushFrameOf(mainViewModel.Audio.Mixer, AudioEngine.SelfTestSpeaker);

        await mainViewModel.DisconnectAsync();

        Assert.Equal(0, mainViewModel.Audio.Mixer.SpeakerCount);
    }

    [Fact]
    public async Task DisconnectAsync_LossStreakRunning_EndsItSoTheNextFigureStartsANewOneAndNoStepDownComes()
    {
        var clock = new ManualTimeProvider();
        await using var mainViewModel = MainViewModelSetup.Create(dir, action => action(), time: clock);
        var lossy = new LossReport(100, 20);
        mainViewModel.Audio.Loss = new LossAdapter(clock, 64);
        mainViewModel.Audio.Loss.Report(lossy);
        clock.Advance(TimeSpan.FromSeconds(4));
        mainViewModel.Audio.Loss.Report(lossy);

        await mainViewModel.DisconnectAsync();
        clock.Advance(TimeSpan.FromSeconds(1));
        mainViewModel.Audio.Loss.Report(lossy);

        Assert.Equal(64, mainViewModel.Audio.Loss.BitrateKbps);
    }

    [Fact]
    public async Task ApplySettings_OutputVolumeOutOfRange_EngineGetsTheClampedVolume()
    {
        await using var mainViewModel = CreateMainViewModel();

        mainViewModel.ApplySettings(new ClientSettings { OutputVolume = 9f });

        Assert.Equal(2f, mainViewModel.Audio.Mixer.Volume);
    }

    [Fact]
    public async Task ApplySettings_VoiceActivation_EngineTakesTheMode()
    {
        await using var mainViewModel = CreateMainViewModel();

        mainViewModel.ApplySettings(new ClientSettings { Mode = TransmitMode.VoiceActivation });

        Assert.Equal(TransmitMode.VoiceActivation, mainViewModel.Audio.Mode);
    }

    [Fact]
    public async Task ApplySettingsAsync_NoDevices_CompletesOnTheCallingThreadAndEngineTakesTheMode()
    {
        await using var mainViewModel = CreateMainViewModel();

        var applying = mainViewModel.ApplySettingsAsync(new ClientSettings { Mode = TransmitMode.VoiceActivation });

        Assert.Equal((true, TransmitMode.VoiceActivation), (applying.IsCompleted, mainViewModel.Audio.Mode));
    }

    [Fact]
    public async Task ApplySettings_SavedDeviceMissing_ShowsTheFallbackAsWarningNotice()
    {
        var hardware = new FakeAudioHardware();
        await using var mainViewModel = CreateMainViewModel(hardware);
        await OpenSettingsWithMicrophoneAsync(mainViewModel, hardware);

        mainViewModel.ApplySettings(new ClientSettings { InputDeviceId = "gone" });

        var notice = Assert.Single(mainViewModel.Notices);
        Assert.Equal((Strings.Audio_DeviceFallback, NoticeKind.Warning), (notice.Text, notice.Kind));
    }

    [Fact]
    public async Task ApplySettingsAsync_SavedDeviceMissing_ShowsTheFallbackAsWarningNotice()
    {
        var hardware = new FakeAudioHardware();
        await using var mainViewModel = CreateMainViewModel(hardware);
        await OpenSettingsWithMicrophoneAsync(mainViewModel, hardware);

        await HangGuard.Bound(mainViewModel.ApplySettingsAsync(new ClientSettings { InputDeviceId = "gone" }));

        var notice = Assert.Single(mainViewModel.Notices);
        Assert.Equal((Strings.Audio_DeviceFallback, NoticeKind.Warning), (notice.Text, notice.Kind));
    }

    [Fact]
    public async Task OpenSettings_ListLoadedAtStart_ShowsInputsAndOutputsAtOnce()
    {
        using var later = new ManualResetEventSlim();
        await using var mainViewModel = CreateMainViewModel(CreateHardwareWhoseLaterListsWait(later, ListOneEach));
        await AsyncWait.Until(() => mainViewModel.Devices.Known is not null); // the list loads in the background

        mainViewModel.OpenSettings(); // its own refresh waits

        Assert.Equal<string?>([null, Headset.Id], mainViewModel.SettingsPage!.Inputs.Select(option => option.Id));
        Assert.Equal<string?>([null, Speakers.Id], mainViewModel.SettingsPage.Outputs.Select(option => option.Id));
        later.Set();
    }

    [Fact]
    public async Task Devices_ListLoaded_IsKeptOnlyWhenTheUiThreadRuns()
    {
        var queued = new List<Action>();
        var arrived = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var mainViewModel = MainViewModelSetup.Create(dir, action =>
        {
            lock (queued) queued.Add(action);
            arrived.TrySetResult();
        }, hardware: new FakeAudioHardware { Listing = ListOneEach });
        await HangGuard.Bound(arrived.Task); // the start's list is loaded and waits for the UI thread

        var knownBeforeTheUiThreadRuns = mainViewModel.Devices.Known;
        Action[] due;
        lock (queued) due = [.. queued];
        foreach (var action in due) action();

        Assert.Null(knownBeforeTheUiThreadRuns);
        Assert.NotNull(mainViewModel.Devices.Known);
    }

    [Fact]
    public async Task OpenSettings_ListLoadsAfterwards_PageShowsBothLists()
    {
        using var later = new ManualResetEventSlim();
        var hardware = new FakeAudioHardware { Listing = direction => { later.Wait(HangGuard.Limit); return ListOneEach(direction); } };
        await using var mainViewModel = CreateMainViewModel(hardware);
        mainViewModel.OpenSettings(); // nothing is known yet

        later.Set();
        await AsyncWait.Until(() => mainViewModel.SettingsPage!.Outputs.Count == 2);

        Assert.Equal<string?>([null, Headset.Id], mainViewModel.SettingsPage!.Inputs.Select(option => option.Id));
        Assert.Equal<string?>([null, Speakers.Id], mainViewModel.SettingsPage.Outputs.Select(option => option.Id));
    }

    [Fact]
    public async Task OpenSettings_DeviceChangedSinceTheStart_PageShowsTheNewListAfterTheRefresh()
    {
        using var later = new ManualResetEventSlim();
        await using var mainViewModel = CreateMainViewModel(CreateHardwareWhoseLaterListsWait(later,
            direction => direction == AudioDirection.Input ? [Webcam] : [MonitorOutput]));
        await AsyncWait.Until(() => mainViewModel.Devices.Known is not null);
        mainViewModel.OpenSettings(); // shows the list from the start, its own refresh waits

        later.Set();
        await AsyncWait.Until(() => mainViewModel.SettingsPage!.Inputs.Any(option => option.Id == Webcam.Id));

        Assert.Equal<string?>([null, Webcam.Id], mainViewModel.SettingsPage!.Inputs.Select(option => option.Id));
    }
}
