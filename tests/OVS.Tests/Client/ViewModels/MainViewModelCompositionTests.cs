using OVS.Client.Audio;
using OVS.Client.Audio.Processing;
using OVS.Client.Audio.Sounds;
using OVS.Client.Dialogs;
using OVS.Client.Input;
using OVS.Client.Logging;
using OVS.Client.Models;
using OVS.Client.Net;
using OVS.Client.Services;
using OVS.Client.Settings;
using OVS.Client.ViewModels;
using OVS.Tests.TestSupport;

namespace OVS.Tests.Client.ViewModels;

/// <summary>
/// The main view model runs on the parts its composition root built and builds none itself. Here the parts live in a
/// folder of their own, apart from the view model's profile folder, so a part it built itself would show.
/// </summary>
public sealed class MainViewModelCompositionTests() : TempDirectoryTest("ovs-compose-", quiet: true)
{
    readonly ManualTimeProvider time = new();

    string Parts => Path.Combine(dir, "parts");
    string Profile => Path.Combine(dir, "profile");

    /// <summary>
    /// A main view model on <see cref="Profile"/>, every part made on <see cref="Parts"/> and <see cref="time"/>; the engine
    /// and the device list on <paramref name="hardware"/>, the engine's tones from the same sound library, as
    /// <see cref="MainViewModel.Create"/> wires them.
    /// </summary>
    MainViewModel WithOwnParts(Action<Action> post, ClientSettings? settings = null, string? settingsWarning = null, KeyPoller? keys = null,
        FakeAudioHardware? hardware = null)
    {
        var log = new ClientLog(Parts, time);
        keys ??= new KeyPoller(time);
        var soundLibrary = new SoundLibrary(Parts, log.Write);
        return MainViewModelSetup.Compose(Profile, post, time, log, settings ?? new ClientSettings(), settingsWarning, new TrustedServerFingerprints(Parts),
            new ServerIconCache(Parts), soundLibrary, new LeaveTimer(time), keys, new AudioEngine(keys, time, hardware, new NativeCaptureFilters(log.Write), soundLibrary.Samples),
            hardware is null ? null : hardware.ListActive);
    }

    [Fact]
    public async Task Tick_NoVoiceConnection_StatsStayBlank()
    {
        await using var viewModel = MainViewModelSetup.Create(dir, action => action(), time: time);
        time.Advance(ConnectionStatsViewModel.RefreshInterval);

        viewModel.Tick();

        Assert.Equal((false, "-"), (viewModel.Stats.Unreachable, viewModel.Stats.BitrateText));
    }

    [Fact]
    public async Task Constructor_GivenParts_ExposesTheSameInstances()
    {
        var settings = new ClientSettings();
        var log = new ClientLog(Parts, time);
        var leave = new LeaveTimer(time);
        var keys = new KeyPoller(time);
        var audio = new AudioEngine(keys, time, hardware: null, new NativeCaptureFilters(log.Write));

        await using var viewModel = MainViewModelSetup.Compose(Profile, action => action(), time, log, settings, null, new TrustedServerFingerprints(Parts),
            new ServerIconCache(Parts), new SoundLibrary(Parts, log.Write), leave, keys, audio);

        Assert.Same(log, viewModel.Log);
        Assert.Same(settings, viewModel.Settings);
        Assert.Same(leave, viewModel.Bookmarks.Leave);
        Assert.Same(keys, viewModel.Keys);
        Assert.Same(audio, viewModel.Audio);
        Assert.Same(time, viewModel.Time);
    }

    /// <summary>Package 123, 140: a filter library that cannot load is told in the client log, the one <see cref="MainViewModel.Create"/> was given.</summary>
    [Fact]
    public async Task Create_GivenClientLog_CaptureFiltersLogToIt()
    {
        await using var viewModel = MainViewModelSetup.Create(Profile, action => action(), time: time);

        Assert.Equal(new Action<string>(viewModel.Log.Write), viewModel.Audio.Filters.Log);
    }

    [Fact]
    public async Task Constructor_SettingsWarning_ShownAsWarningNotice()
    {
        await using var viewModel = WithOwnParts(action => action(), settingsWarning: "The settings file was damaged");

        var notice = Assert.Single(viewModel.Notices);
        Assert.Equal(("The settings file was damaged", NoticeKind.Warning), (notice.Text, notice.Kind));
    }

    [Fact]
    public async Task Constructor_GivenIconCache_BookmarkTileShowsItsLogo()
    {
        byte[] logo = [1, 2, 3];
        new ServerIconCache(Parts).Save("voice.example.org", 7000, logo);

        await using var viewModel = WithOwnParts(action => action(), new ClientSettings { Bookmarks = [new Bookmark("Voice", "voice.example.org", 7000, "anna")] });

        Assert.Equal(logo, Assert.Single(viewModel.Bookmarks.Items).Icon);
    }

    [Fact]
    public async Task Constructor_GivenSoundLibrary_UnusedToneRemoved()
    {
        var tone = Path.Combine(Parts, SoundLibrary.Folder, "old.wav");
        Directory.CreateDirectory(Path.GetDirectoryName(tone)!);
        File.WriteAllBytes(tone, [1]);

        await using var viewModel = WithOwnParts(action => action()); // applying the settings cleans the tone folder up

        Assert.False(File.Exists(tone));
    }

    [Fact]
    public async Task AddNotice_ClockMovedOn_StampedWithItsLocalTime()
    {
        await using var viewModel = WithOwnParts(action => action());
        time.Advance(TimeSpan.FromHours(3)); // far from the machine's own clock

        viewModel.AddNotice("Saved");

        var notice = Assert.Single(viewModel.Notices);
        Assert.Equal(time.GetLocalNow().DateTime, notice.Time);
        Assert.Equal(DateTimeKind.Local, notice.Time.Kind);
    }

    [Fact]
    public async Task OpenSettings_GivenClock_PendingMarksRunOnIt()
    {
        await using var viewModel = WithOwnParts(action => action());
        viewModel.PageFlow.OpenSettings();
        var saving = viewModel.PageFlow.SettingsPage!.Saving;

        saving.Start();
        time.Advance(PendingActionViewModel.ShowAfter); // only the given clock moves

        Assert.True(saving.IsBusy);
    }

    [Fact]
    public async Task ApplySettingsAsync_GivenHardware_RestartsDevicesOffTheCallingThread()
    {
        var hardware = new FakeAudioHardware();
        await using var viewModel = WithOwnParts(action => action(), hardware: hardware);
        viewModel.PageFlow.OpenSettings(); // the level meter opens the microphone
        await AsyncWait.Until(() => hardware.Counts.MicsOpen == 1);

        hardware.OpenGate.Reset(); // the restart's open waits, so a restart on this thread would not return
        var applying = viewModel.ApplySettingsAsync(new ClientSettings());
        Assert.False(applying.IsCompleted);
        hardware.OpenGate.Set();
        await applying;

        Assert.Equal((2, 1), (hardware.Counts.Mics, hardware.Counts.MicsOpen)); // the microphone opened anew
    }

    [Fact]
    public async Task DisposeAsync_GivenKeysAndAudio_DisposesBoth()
    {
        Thread? poller = null;
        var hardware = new FakeAudioHardware();
        var keys = new KeyPoller(time, _ =>
        {
            poller = Thread.CurrentThread;
            return false;
        });
        var viewModel = WithOwnParts(action => action(), keys: keys, hardware: hardware);
        await AsyncWait.Until(() => poller is not null);

        await viewModel.DisposeAsync();

        Assert.False(poller!.IsAlive); // the key poller's thread ended
        Assert.Equal(0, hardware.Subscribers); // the engine no longer listens to the devices
    }

    [Fact]
    public void Create_ConstructorThrows_DisposesTheAudioEngine()
    {
        var hardware = new FakeAudioHardware();

        Assert.ThrowsAny<Exception>(() => MainViewModel.Create(Profile, action => action(), time, new ClientLog(Parts, time), settings: null!, null,
            hardware: hardware));

        Assert.Equal(0, hardware.Subscribers);
    }

    [Fact]
    public async Task Connect_GivenKnownServers_NewPinSavedThere()
    {
        var (server, ui, viewModel) = await ConnectAsync();
        try
        {
            Assert.True(File.Exists(Path.Combine(Parts, TrustedServerFingerprints.FileName)));
            Assert.False(File.Exists(Path.Combine(Profile, TrustedServerFingerprints.FileName)));
        }
        finally
        {
            await CloseAsync(server, ui, viewModel);
        }
    }

    [Fact]
    public async Task Connect_GivenClock_ConnectionPartsHoldIt()
    {
        var (server, ui, viewModel) = await ConnectAsync();
        try
        {
            Assert.Same(time, await ui.InvokeAsync(() => Task.FromResult(viewModel.Connection.ConnectionTime)));
            Assert.Same(time, await ui.InvokeAsync(() => Task.FromResult(viewModel.Server?.Time)));
            Assert.Same(time, await ui.InvokeAsync(() => Task.FromResult(viewModel.Connection.VoiceLink.Client?.Time)));
        }
        finally
        {
            await CloseAsync(server, ui, viewModel);
        }
    }

    /// <summary>A view model with its own parts connected to a fresh test server, its certificate trusted.</summary>
    async Task<ClientRig> ConnectAsync()
    {
        var server = await TestServer.StartAsync();
        var ui = new TestDispatcher();
        var viewModel = await ui.InvokeAsync(() => Task.FromResult(WithOwnParts(ui.Post)));
        viewModel.ConfirmTofu = _ => Task.FromResult(true);
        await ui.InvokeAsync<object?>(async () =>
        {
            await viewModel.Connection.ConnectAsync(new ConnectChoice("127.0.0.1", server.Port, "anna", null, SaveBookmark: false));
            return null;
        });
        return new ClientRig(server, ui, viewModel);
    }

    static async Task CloseAsync(TestServer server, TestDispatcher ui, MainViewModel viewModel)
    {
        await ui.InvokeAsync<object?>(async () =>
        {
            await viewModel.DisposeAsync();
            return null;
        });
        ui.Dispose();
        await server.DisposeAsync();
    }
}
