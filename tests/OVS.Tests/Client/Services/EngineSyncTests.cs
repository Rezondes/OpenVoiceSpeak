using OVS.Client.Audio;
using OVS.Client.Audio.Transmit;
using OVS.Client.Input;
using OVS.Client.Localization;
using OVS.Client.Net;
using OVS.Client.Services;
using OVS.Client.Settings;
using OVS.Shared.Permissions;
using OVS.Shared.Protocol;
using OVS.Tests.TestSupport;
using static OVS.Tests.TestSupport.EngineProbe;

namespace OVS.Tests.Client.Services;

/// <summary>
/// Packages 51, 98, 122, 132, 135: keeping the audio engine in step with the connection (its state, its channel's bitrate,
/// the speaker gains) and the saved settings (also restarting the devices off the calling thread), on a real engine without
/// a window, a connection or a network. The devices are fakes.
/// </summary>
public sealed class EngineSyncTests : IDisposable
{
    readonly ManualTimeProvider clock = new();
    readonly KeyPoller keys;
    readonly AudioEngine engine;
    readonly Dictionary<string, float> savedVolumes = [];
    EngineConnectionState connectionState = EngineConnectionState.Disconnected;
    StateMirror? connectedMirror;
    FakeAudioHardware? hardware;

    public EngineSyncTests()
    {
        keys = new KeyPoller(clock);
        engine = new AudioEngine(keys, clock, hardware: null);
    }

    public void Dispose()
    {
        engine.Dispose();
        keys.Dispose();
    }

    /// <summary>A sync on the engine of this test (or <paramref name="target"/>); the connection and the saved volumes are what the fields hold when it asks.</summary>
    EngineSync CreateSync(AudioEngine? target = null) =>
        new(target ?? engine, () => connectionState, () => connectedMirror, fingerprint => savedVolumes.GetValueOrDefault(fingerprint, 1f));

    /// <summary>A server state whose users have the session ids <paramref name="sessionIds"/> and the fingerprint "fp" + id.</summary>
    static StateMirror CreateMirror(params uint[] sessionIds) =>
        Snapshots.Mirror(Snapshots.Snapshot("server", "", Guid.Empty, [], [], [.. sessionIds.Select(id => Snapshots.User(id, "user" + id, Guid.Empty, Permission.Speak))]));

    /// <summary>The engine's switches as the disconnected state leaves them, so a switch the sync changes shows.</summary>
    void SetEngineDisconnected()
    {
        engine.Connected = false;
        engine.SelfMuted = true;
        engine.Deafened = false;
        engine.HasSpeakLinked = false;
        engine.ChannelMuted = false;
    }

    /// <summary>An engine on the fake devices with its microphone open (the level meter wants it), so a restart reopens it.</summary>
    async Task<AudioEngine> OpenEngineWithMicrophoneAsync()
    {
        hardware = new FakeAudioHardware();
        var withDevices = new AudioEngine(keys, clock, hardware);
        withDevices.LevelMeter = true;
        await AsyncWait.UntilAsync(() => hardware.Counts.MicsOpen == 1);
        return withDevices;
    }

    // ---- SyncConnection: the connection's switches ----

    [Fact]
    public void EngineConnectionState_Disconnected_IsMutedWithoutConnectionSpeakRightOrChannel()
    {
        Assert.Equal(new EngineConnectionState(Connected: false, SelfMuted: true, Deafened: false, HasSpeakLinked: false, ChannelMuted: false, ChannelId: null,
            ChannelBitrate: null), EngineConnectionState.Disconnected);
    }

    [Fact]
    public void SyncConnection_OnlyConnectedSet_TurnsOnlyTheEnginesConnectedOn()
    {
        SetEngineDisconnected();
        connectionState = EngineConnectionState.Disconnected with { Connected = true };

        CreateSync().SyncConnection();

        Assert.Equal(new EngineSwitches(Connected: true, SelfMuted: true, Deafened: false, HasSpeakLinked: false, ChannelMuted: false), ReadEngineSwitches(engine));
    }

    [Fact]
    public void SyncConnection_OnlySelfMutedCleared_TurnsOnlyTheEnginesSelfMutedOff()
    {
        SetEngineDisconnected();
        connectionState = EngineConnectionState.Disconnected with { SelfMuted = false };

        CreateSync().SyncConnection();

        Assert.Equal(new EngineSwitches(Connected: false, SelfMuted: false, Deafened: false, HasSpeakLinked: false, ChannelMuted: false), ReadEngineSwitches(engine));
    }

    [Fact]
    public void SyncConnection_OnlyDeafenedSet_TurnsOnlyTheEnginesDeafenedOn()
    {
        SetEngineDisconnected();
        connectionState = EngineConnectionState.Disconnected with { Deafened = true };

        CreateSync().SyncConnection();

        Assert.Equal(new EngineSwitches(Connected: false, SelfMuted: true, Deafened: true, HasSpeakLinked: false, ChannelMuted: false), ReadEngineSwitches(engine));
    }

    [Fact]
    public void SyncConnection_OnlyHasSpeakLinkedSet_TurnsOnlyTheEnginesSpeakLinkedOn()
    {
        SetEngineDisconnected();
        connectionState = EngineConnectionState.Disconnected with { HasSpeakLinked = true };

        CreateSync().SyncConnection();

        Assert.Equal(new EngineSwitches(Connected: false, SelfMuted: true, Deafened: false, HasSpeakLinked: true, ChannelMuted: false), ReadEngineSwitches(engine));
    }

    [Fact]
    public void SyncConnection_OnlyChannelMutedSet_TurnsOnlyTheEnginesChannelMutedOn()
    {
        SetEngineDisconnected();
        connectionState = EngineConnectionState.Disconnected with { ChannelMuted = true };

        CreateSync().SyncConnection();

        Assert.Equal(new EngineSwitches(Connected: false, SelfMuted: true, Deafened: false, HasSpeakLinked: false, ChannelMuted: true), ReadEngineSwitches(engine));
    }

    // ---- SyncConnection: the channel's bitrate ----

    [Fact]
    public void SyncConnection_ChannelBitrate_SetsTheEnginesChannelBitrate()
    {
        connectionState = EngineConnectionState.Disconnected with { ChannelBitrate = 96 };

        CreateSync().SyncConnection();

        Assert.Equal(96, engine.ChannelBitrate);
    }

    [Fact]
    public void SyncConnection_NoChannelBitrate_SetsTheDefaultBitrate()
    {
        engine.ChannelBitrate = 96;
        connectionState = EngineConnectionState.Disconnected;

        CreateSync().SyncConnection();

        Assert.Equal(ProtocolDefaults.Bitrate, engine.ChannelBitrate);
    }

    [Fact]
    public void SyncConnection_BitrateAboveTheMaximum_SetsTheMaximum()
    {
        connectionState = EngineConnectionState.Disconnected with { ChannelBitrate = ProtocolLimits.MaxBitrate + 1 };

        CreateSync().SyncConnection();

        Assert.Equal(ProtocolLimits.MaxBitrate, engine.ChannelBitrate);
    }

    [Fact]
    public void SyncConnection_BitrateBelowTheMinimum_SetsTheMinimum()
    {
        connectionState = EngineConnectionState.Disconnected with { ChannelBitrate = ProtocolLimits.MinBitrate - 1 };

        CreateSync().SyncConnection();

        Assert.Equal(ProtocolLimits.MinBitrate, engine.ChannelBitrate);
    }

    /// <summary>The loss adapter on the test's clock, its bitrate lowered by loss from 64 to 32 kbps.</summary>
    void LowerBitrateByLoss()
    {
        engine.Loss = new LossAdapter(clock, 64);
        LowerByLoss(engine.Loss, clock);
    }

    [Fact]
    public void SyncConnection_ChannelNotSeenBefore_ResetsTheLossAdapterToTheChannelsBitrate()
    {
        LowerBitrateByLoss();
        connectionState = EngineConnectionState.Disconnected with { ChannelId = Guid.NewGuid(), ChannelBitrate = 96 };

        CreateSync().SyncConnection();

        Assert.Equal(96, engine.Loss.BitrateKbps);
    }

    [Fact]
    public void SyncConnection_BitrateAboveTheMaximumInANewChannel_ResetsTheLossAdapterToTheMaximum()
    {
        connectionState = EngineConnectionState.Disconnected with { ChannelId = Guid.NewGuid(), ChannelBitrate = ProtocolLimits.MaxBitrate + 1 };

        CreateSync().SyncConnection();

        Assert.Equal(ProtocolLimits.MaxBitrate, engine.Loss.BitrateKbps);
    }

    [Fact]
    public void SyncConnection_SameChannelAgain_KeepsTheLoweredBitrate()
    {
        var sync = CreateSync();
        connectionState = EngineConnectionState.Disconnected with { ChannelId = Guid.NewGuid(), ChannelBitrate = 64 };
        sync.SyncConnection();
        LowerBitrateByLoss();

        sync.SyncConnection();

        Assert.Equal(32, engine.Loss.BitrateKbps);
    }

    [Fact]
    public void SyncConnection_ChannelLeftAfterOne_ResetsTheLossAdapter()
    {
        var sync = CreateSync();
        connectionState = EngineConnectionState.Disconnected with { ChannelId = Guid.NewGuid(), ChannelBitrate = 64 };
        sync.SyncConnection();
        LowerBitrateByLoss();
        connectionState = EngineConnectionState.Disconnected;

        sync.SyncConnection();

        Assert.Equal(ProtocolDefaults.Bitrate, engine.Loss.BitrateKbps);
    }

    // ---- SyncConnection: when the gain table is built ----

    [Fact]
    public void SyncConnection_NoServerYet_BuildsNoGainTable()
    {
        var sync = CreateSync();

        sync.SyncConnection();

        Assert.Equal(0, sync.GainTableBuilds);
    }

    [Fact]
    public void SyncConnection_ServerNotSeenBefore_BuildsTheGainTableOnceForIt()
    {
        var sync = CreateSync();
        connectedMirror = CreateMirror(7);
        savedVolumes["fp7"] = 2f;

        sync.SyncConnection();

        Assert.Equal((1, 2f), (sync.GainTableBuilds, engine.Mixer.SpeakerGain(7)));
    }

    [Fact]
    public void SyncConnection_SameServerAsTheLastTable_BuildsNoNewTable()
    {
        var sync = CreateSync();
        connectedMirror = CreateMirror(7);
        sync.SyncGains();

        sync.SyncConnection();

        Assert.Equal(1, sync.GainTableBuilds);
    }

    [Fact]
    public void SyncConnection_OtherServerThanTheLastTable_BuildsTheTableOfTheOtherOne()
    {
        var sync = CreateSync();
        connectedMirror = CreateMirror(7);
        savedVolumes["fp7"] = 2f;
        sync.SyncGains();
        connectedMirror = CreateMirror(8);
        savedVolumes["fp8"] = 0.5f;

        sync.SyncConnection();

        Assert.Equal((2, 0.5f, 1f), (sync.GainTableBuilds, engine.Mixer.SpeakerGain(8), engine.Mixer.SpeakerGain(7)));
    }

    [Fact]
    public void SyncConnection_ServerGoneAfterATable_BuildsTheEmptyTable()
    {
        var sync = CreateSync();
        connectedMirror = CreateMirror(7);
        savedVolumes["fp7"] = 2f;
        sync.SyncGains();
        connectedMirror = null;

        sync.SyncConnection();

        Assert.Equal((2, 1f), (sync.GainTableBuilds, engine.Mixer.SpeakerGain(7)));
    }

    // ---- SyncGains ----

    [Fact]
    public void SyncGains_SavedVolumes_GiveOnlyTheSpeakersWithASavedVolumeTheirGain()
    {
        var sync = CreateSync();
        connectedMirror = CreateMirror(1, 2, 3);
        savedVolumes["fp1"] = 2f;
        savedVolumes["fp3"] = 0.5f;

        sync.SyncGains();

        Assert.Equal((2f, 1f, 0.5f), (engine.Mixer.SpeakerGain(1), engine.Mixer.SpeakerGain(2), engine.Mixer.SpeakerGain(3)));
    }

    [Fact]
    public void SyncGains_VolumeSavedForOneFingerprint_GoesToThatSessionOnly()
    {
        var sync = CreateSync();
        connectedMirror = CreateMirror(7, 8);
        savedVolumes["fp7"] = 2f;

        sync.SyncGains();

        Assert.Equal((2f, 1f), (engine.Mixer.SpeakerGain(7), engine.Mixer.SpeakerGain(8)));
    }

    [Fact]
    public void SyncGains_SpeakerNoLongerThere_LosesItsGain()
    {
        engine.Mixer.SetSpeakerGains(new Dictionary<uint, float> { [9] = 2f });
        var sync = CreateSync();
        connectedMirror = CreateMirror();

        sync.SyncGains();

        Assert.Equal(1f, engine.Mixer.SpeakerGain(9));
    }

    [Fact]
    public void SyncGains_Twice_CountsTwoBuilds()
    {
        var sync = CreateSync();

        sync.SyncGains();
        sync.SyncGains();

        Assert.Equal(2, sync.GainTableBuilds);
    }

    // ---- SyncMembers and ClearSpeakers ----

    [Fact]
    public void SyncMembers_SpeakerWhoLeft_LosesTheirDecoder()
    {
        var sync = CreateSync();
        connectedMirror = CreateMirror(8);
        PushFrameOf(engine.Mixer, 7);

        sync.SyncMembers();

        Assert.Null(engine.Mixer.BufferOf(7));
    }

    [Fact]
    public void SyncMembers_SpeakerStillThere_KeepsTheirDecoder()
    {
        var sync = CreateSync();
        connectedMirror = CreateMirror(8);
        PushFrameOf(engine.Mixer, 8);

        sync.SyncMembers();

        Assert.NotNull(engine.Mixer.BufferOf(8));
    }

    [Fact]
    public void SyncMembers_SelfTestVoice_KeepsItsDecoderThoughNoUserHasItsId()
    {
        var sync = CreateSync();
        connectedMirror = CreateMirror(8);
        PushFrameOf(engine.Mixer, AudioEngine.SelfTestSpeaker);

        sync.SyncMembers();

        Assert.NotNull(engine.Mixer.BufferOf(AudioEngine.SelfTestSpeaker));
    }

    [Fact]
    public void SyncMembers_UserWithSavedVolume_BuildsTheGainTableOnceForThem()
    {
        var sync = CreateSync();
        connectedMirror = CreateMirror(7);
        savedVolumes["fp7"] = 2f;

        sync.SyncMembers();

        Assert.Equal((1, 2f), (sync.GainTableBuilds, engine.Mixer.SpeakerGain(7)));
    }

    [Fact]
    public void SyncMembers_NoServer_BuildsATableOnceAndDropsNobody()
    {
        var sync = CreateSync();
        PushFrameOf(engine.Mixer, 7);

        sync.SyncMembers();

        Assert.Equal((1, true), (sync.GainTableBuilds, engine.Mixer.BufferOf(7) is not null));
    }

    [Fact]
    public void ClearSpeakers_SpeakersPresent_DropsEveryDecoder()
    {
        var sync = CreateSync();
        connectedMirror = CreateMirror(7, 8);
        PushFrameOf(engine.Mixer, 7);
        PushFrameOf(engine.Mixer, 8);

        sync.ClearSpeakers();

        Assert.Equal(0, engine.Mixer.SpeakerCount);
    }

    // ---- Configure ----

    [Fact]
    public void Configure_Settings_TakeEffectOnTheEngine()
    {
        var sync = CreateSync();

        sync.Configure(new ClientSettings { Mode = TransmitMode.VoiceActivation, OutputVolume = 0.3f });

        Assert.Equal((TransmitMode.VoiceActivation, 0.3f), (engine.Mode, engine.Mixer.Volume));
    }

    [Fact]
    public void Configure_NoDevicesOpen_ReturnsNoWarning()
    {
        var sync = CreateSync();

        var warning = sync.Configure(new ClientSettings());

        Assert.Null(warning);
    }

    [Fact]
    public async Task Configure_SavedDeviceMissing_ReturnsTheFallbackWarning()
    {
        using var withDevices = await OpenEngineWithMicrophoneAsync();
        var sync = CreateSync(withDevices);

        var warning = sync.Configure(new ClientSettings { InputDeviceId = "gone" });

        Assert.Equal(Strings.Audio_DeviceFallback, warning);
    }

    // ---- ConfigureAsync ----

    [Fact]
    public async Task ConfigureAsync_NoDevices_AppliesAtOnceOnTheCallingThreadWithoutWarning()
    {
        var sync = CreateSync();

        var configuring = sync.ConfigureAsync(new ClientSettings { Mode = TransmitMode.VoiceActivation });

        Assert.Equal((true, TransmitMode.VoiceActivation, (string?)null), (configuring.IsCompleted, engine.Mode, await configuring));
    }

    [Fact]
    public async Task ConfigureAsync_DeviceOpenWaits_ReturnsBeforeTheRestartIsDone()
    {
        using var withDevices = await OpenEngineWithMicrophoneAsync();
        var sync = CreateSync(withDevices);
        hardware!.OpenGate.Reset(); // the restart's open waits, so a restart on this thread would not return

        var configuring = sync.ConfigureAsync(new ClientSettings());

        Assert.False(configuring.IsCompleted);
        hardware.OpenGate.Set();
        await HangGuard.BoundAsync(configuring);
    }

    [Fact]
    public async Task ConfigureAsync_SavedDeviceMissing_ReturnsTheFallbackWarning()
    {
        using var withDevices = await OpenEngineWithMicrophoneAsync();
        var sync = CreateSync(withDevices);

        var warning = await HangGuard.BoundAsync(sync.ConfigureAsync(new ClientSettings { InputDeviceId = "gone" }));

        Assert.Equal(Strings.Audio_DeviceFallback, warning);
    }

    [Fact]
    public async Task ConfigureAsync_DevicesOpen_AppliesTheSettingsToTheEngine()
    {
        using var withDevices = await OpenEngineWithMicrophoneAsync();
        var sync = CreateSync(withDevices);

        await HangGuard.BoundAsync(sync.ConfigureAsync(new ClientSettings { Mode = TransmitMode.VoiceActivation }));

        Assert.Equal(TransmitMode.VoiceActivation, withDevices.Mode);
    }
}
