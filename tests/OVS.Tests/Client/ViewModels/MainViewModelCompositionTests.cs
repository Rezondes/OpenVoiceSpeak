using OVS.Client.Audio;
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

    /// <summary>A main view model on <see cref="Profile"/>, every part made on <see cref="Parts"/> and <see cref="time"/>.</summary>
    MainViewModel WithOwnParts(Action<Action> post, ClientSettings? settings = null, string? settingsWarning = null, KeyPoller? keys = null,
        AudioEngine? audio = null)
    {
        var log = new ClientLog(Parts, time);
        keys ??= new KeyPoller(time);
        return MainViewModelSetup.Compose(Profile, post, time, log, settings ?? new ClientSettings(), settingsWarning, new KnownServers(Parts),
            new ServerIconCache(Parts), new SoundLibrary(Parts, log.Write), new LeaveTimer(time), keys, audio ?? new AudioEngine(keys, time, useDevices: false, log.Write));
    }

    [Fact]
    public async Task Constructor_GivenParts_ExposesTheSameInstances()
    {
        var settings = new ClientSettings();
        var log = new ClientLog(Parts, time);
        var leave = new LeaveTimer(time);
        var keys = new KeyPoller(time);
        var audio = new AudioEngine(keys, time, useDevices: false, log.Write);

        await using var vm = MainViewModelSetup.Compose(Profile, a => a(), time, log, settings, null, new KnownServers(Parts),
            new ServerIconCache(Parts), new SoundLibrary(Parts, log.Write), leave, keys, audio);

        Assert.Same(log, vm.Log);
        Assert.Same(settings, vm.Settings);
        Assert.Same(leave, vm.Leave);
        Assert.Same(keys, vm.Keys);
        Assert.Same(audio, vm.Audio);
        Assert.Same(time, vm.Time);
    }

    [Fact]
    public async Task Constructor_SettingsWarning_ShownAsWarningNotice()
    {
        await using var vm = WithOwnParts(a => a(), settingsWarning: "The settings file was damaged");

        var notice = Assert.Single(vm.Notices);
        Assert.Equal(("The settings file was damaged", NoticeKind.Warning), (notice.Text, notice.Kind));
    }

    [Fact]
    public async Task Constructor_GivenIconCache_BookmarkTileShowsItsLogo()
    {
        byte[] logo = [1, 2, 3];
        new ServerIconCache(Parts).Save("voice.example.org", 7000, logo);

        await using var vm = WithOwnParts(a => a(), new ClientSettings { Bookmarks = [new Bookmark("Voice", "voice.example.org", 7000, "anna")] });

        Assert.Equal(logo, Assert.Single(vm.Bookmarks).Icon);
    }

    [Fact]
    public async Task Constructor_GivenSoundLibrary_UnusedToneRemoved()
    {
        var tone = Path.Combine(Parts, SoundLibrary.Folder, "old.wav");
        Directory.CreateDirectory(Path.GetDirectoryName(tone)!);
        File.WriteAllBytes(tone, [1]);

        await using var vm = WithOwnParts(a => a()); // applying the settings cleans the tone folder up

        Assert.False(File.Exists(tone));
    }

    [Fact]
    public async Task AddNotice_ClockMovedOn_StampedWithItsLocalTime()
    {
        await using var vm = WithOwnParts(a => a());
        time.Advance(TimeSpan.FromHours(3)); // far from the machine's own clock

        vm.AddNotice("Saved");

        var notice = Assert.Single(vm.Notices);
        Assert.Equal(time.GetLocalNow().DateTime, notice.Time);
        Assert.Equal(DateTimeKind.Local, notice.Time.Kind);
    }

    [Fact]
    public async Task OpenSettings_GivenClock_PendingMarksRunOnIt()
    {
        await using var vm = WithOwnParts(a => a());
        vm.OpenSettings();
        var saving = vm.SettingsPage!.Saving;

        saving.Start();
        time.Advance(PendingViewModel.ShowAfter); // only the given clock moves

        Assert.True(saving.IsBusy);
    }

    [Fact]
    public async Task DisposeAsync_GivenKeysAndAudio_DisposesBoth()
    {
        Thread? poller = null;
        var devices = new FakeAudioDevices();
        var keys = new KeyPoller(time, _ =>
        {
            poller = Thread.CurrentThread;
            return false;
        });
        var vm = WithOwnParts(a => a(), keys: keys, audio: new AudioEngine(keys, time, useDevices: false, devices: devices));
        await AsyncWait.Until(() => poller is not null);

        await vm.DisposeAsync();

        Assert.False(poller!.IsAlive); // the key poller's thread ended
        Assert.Equal(0, devices.Subscribers); // the engine no longer listens to the devices
    }

    [Fact]
    public void Create_ConstructorThrows_DisposesTheAudioEngine()
    {
        var devices = new FakeAudioDevices();

        Assert.ThrowsAny<Exception>(() => MainViewModel.Create(Profile, a => a(), time, new ClientLog(Parts, time), settings: null!, null,
            useAudioDevices: false, devices: devices));

        Assert.Equal(0, devices.Subscribers);
    }

    [Fact]
    public async Task Connect_GivenKnownServers_NewPinSavedThere()
    {
        var (server, ui, vm) = await ConnectAsync();
        try
        {
            Assert.True(File.Exists(Path.Combine(Parts, KnownServers.FileName)));
            Assert.False(File.Exists(Path.Combine(Profile, KnownServers.FileName)));
        }
        finally
        {
            await CloseAsync(server, ui, vm);
        }
    }

    [Fact]
    public async Task Connect_GivenClock_ConnectionPartsHoldIt()
    {
        var (server, ui, vm) = await ConnectAsync();
        try
        {
            Assert.Same(time, await ui.InvokeAsync(() => Task.FromResult(vm.ConnectionTime)));
            Assert.Same(time, await ui.InvokeAsync(() => Task.FromResult(vm.Server?.Time)));
            Assert.Same(time, await ui.InvokeAsync(() => Task.FromResult(vm.Voice?.Time)));
        }
        finally
        {
            await CloseAsync(server, ui, vm);
        }
    }

    /// <summary>A view model with its own parts connected to a fresh test server, its certificate trusted.</summary>
    async Task<(TestServer Server, TestDispatcher Ui, MainViewModel Vm)> ConnectAsync()
    {
        var server = await TestServer.StartAsync();
        var ui = new TestDispatcher();
        var vm = await ui.InvokeAsync(() => Task.FromResult(WithOwnParts(ui.Post)));
        vm.ConfirmTofu = _ => Task.FromResult(true);
        await ui.InvokeAsync<object?>(async () =>
        {
            await vm.ConnectAsync(new ConnectChoice("127.0.0.1", server.Port, "anna", null, SaveBookmark: false));
            return null;
        });
        return (server, ui, vm);
    }

    static async Task CloseAsync(TestServer server, TestDispatcher ui, MainViewModel vm)
    {
        await ui.InvokeAsync<object?>(async () =>
        {
            await vm.DisposeAsync();
            return null;
        });
        ui.Dispose();
        await server.DisposeAsync();
    }
}
