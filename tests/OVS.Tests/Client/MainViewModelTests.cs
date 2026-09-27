using OVS.Client.Audio;
using OVS.Client.Input;
using OVS.Client.Settings;
using OVS.Client.ViewModels;
using OVS.Tests.TestSupport;

namespace OVS.Tests.Client;

/// <summary>Application shell against a real server, without audio devices.</summary>
public sealed class MainViewModelTests : IAsyncLifetime
{
    readonly TestDispatcher ui = new();
    readonly string dir = Directory.CreateTempSubdirectory("ovs-mvm-").FullName;
    TestServer server = null!;
    MainViewModel vm = null!;

    public async Task InitializeAsync()
    {
        server = await TestServer.StartAsync(d => d.Settings.WelcomeText = "Willkommen auf dem Testserver!");
        vm = await ui.InvokeAsync(() => Task.FromResult(new MainViewModel(dir, ui.Post, useAudioDevices: false)));
        vm.ConfirmTofu = _ => Task.FromResult(true);
    }

    public async Task DisposeAsync()
    {
        await ui.InvokeAsync<object?>(async () =>
        {
            await vm.DisposeAsync();
            return null;
        });
        ui.Dispose();
        await server.DisposeAsync();
        Directory.Delete(dir, true);
    }

    Task<T> OnUi<T>(Func<T> read) => ui.InvokeAsync(() => Task.FromResult(read()));

    Task ConnectAsync(bool saveBookmark) =>
        ui.InvokeAsync<object?>(async () =>
        {
            await vm.ConnectAsync(new ConnectChoice("127.0.0.1", server.Port, "anna", null, saveBookmark));
            return null;
        });

    [Fact]
    public async Task Connect_SavesBookmark_ToSettingsFile()
    {
        await ConnectAsync(saveBookmark: true);
        Assert.True(await OnUi(() => vm.IsConnected));

        var saved = ClientSettings.Load(dir, out _);
        var bookmark = Assert.Single(saved.Bookmarks);
        Assert.Equal(("127.0.0.1", server.Port, "anna"), (bookmark.Host, bookmark.Port, bookmark.Nickname));
    }

    [Fact]
    public async Task Connect_ShowsWelcomeText_AndPing()
    {
        await ConnectAsync(saveBookmark: false);
        Assert.Contains(await OnUi(() => vm.Notices.ToList()), n => n.Kind == NoticeKind.Welcome && n.Text == "Willkommen auf dem Testserver!");

        string ping = "";
        for (int i = 0; i < 40 && ping.Length == 0; i++)
        {
            await Task.Delay(50);
            ping = await OnUi(() =>
            {
                vm.Tick();
                return vm.PingText;
            });
        }
        Assert.StartsWith("Ping ", ping);
    }

    [Fact]
    public async Task Notices_HaveKinds_ErrorAndDisconnect()
    {
        await ConnectAsync(saveBookmark: false);
        await ui.InvokeAsync<object?>(async () =>
        {
            await vm.Server!.CreateChannelAsync("Raid", ""); // a guest may not
            return null;
        });
        Notice? error = null;
        for (int i = 0; i < 40 && error is null; i++)
        {
            await Task.Delay(50);
            error = await OnUi(() => vm.Notices.FirstOrDefault(n => n.Kind == NoticeKind.Error));
        }
        Assert.Equal("Dafür fehlt dir das Recht.", error?.Text);

        await server.Control.StopAsync();
        Notice? bye = null;
        for (int i = 0; i < 60 && bye is null; i++)
        {
            await Task.Delay(50);
            bye = await OnUi(() => vm.Notices.FirstOrDefault(n => n.Kind == NoticeKind.Warning));
        }
        Assert.StartsWith("Getrennt: ", bye?.Text);
        Assert.Matches(@"^\d{2}:\d{2}:\d{2}  Getrennt: ", bye!.ToString()); // the debug API keeps its "time  text" form
    }

    [Fact]
    public async Task StartScreen_BookmarksAndConnectingState()
    {
        Assert.False(await OnUi(() => vm.HasBookmarks));
        Assert.Equal("PTT: Maustaste 4", await OnUi(() => vm.TalkHint));
        var changed = new List<string?>();
        await OnUi(() =>
        {
            vm.PropertyChanged += (_, e) => changed.Add(e.PropertyName);
            return 0;
        });

        await ConnectAsync(saveBookmark: true);
        Assert.True(await OnUi(() => vm.HasBookmarks));
        Assert.Equal("anna", (await OnUi(() => vm.Bookmarks)).Single().Nickname);
        Assert.Contains(nameof(MainViewModel.Bookmarks), changed);
        Assert.Contains(nameof(MainViewModel.IsConnecting), changed);
        Assert.False(await OnUi(() => vm.IsConnecting));
    }

    [Fact]
    public void Deafened_Engine_IgnoresIncomingVoice()
    {
        using var keys = new KeyPoller();
        using var engine = new AudioEngine(keys, useDevices: false) { Deafened = true };
        engine.OnVoice(7, 0, 0, new VoiceEncoder().Encode(new float[AudioFormat.FrameSamples]));
        Assert.Empty(engine.FramesReceived);
        Assert.Equal(0, engine.Mixer.SpeakerCount);
    }

    [Fact]
    public async Task ToneInput_ReportsInputLevel()
    {
        using var keys = new KeyPoller();
        using var engine = new AudioEngine(keys, useDevices: false);
        var level = new TaskCompletionSource<float>();
        engine.InputLevel += db => level.TrySetResult(db);
        engine.SetTone(440);
        // 0.3 amplitude sine: RMS 0.212 = -13.5 dBFS
        Assert.InRange(await level.Task.WaitAsync(TimeSpan.FromSeconds(3)), -15f, -12f);
    }
}
