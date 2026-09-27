using OVS.Client.Audio;
using OVS.Client.Input;
using OVS.Client.Settings;
using OVS.Client.ViewModels;
using OVS.Shared.Protocol;
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

    /// <summary>Package 29: toggle keys work like the buttons and reach the server.</summary>
    [Fact]
    public async Task ToggleActions_SyncWithServer()
    {
        await ConnectAsync(saveBookmark: false);
        await using var bert = await TestClient.ConnectAsync(server, "bert");
        var annaId = await OnUi(() => vm.Server!.Mirror.SelfId);

        vm.Keys.Simulate(KeyAction.ToggleMute, true);
        await Task.Delay(60);
        vm.Keys.Simulate(KeyAction.ToggleMute, false);
        var muted = await bert.WaitForAsync<UserUpdated>(u => u.User.SessionId == annaId && u.User.SelfMuted);
        Assert.False(muted.User.SelfDeafened);
        Assert.True(await OnUi(() => vm.Server!.SelfMuted));

        vm.Keys.Simulate(KeyAction.ToggleDeafen, true);
        await Task.Delay(60);
        vm.Keys.Simulate(KeyAction.ToggleDeafen, false);
        await bert.WaitForAsync<UserUpdated>(u => u.User.SessionId == annaId && u.User.SelfDeafened);
    }

    [Fact]
    public async Task TalkHint_NoPttBinding_UntilBound()
    {
        Assert.True(await OnUi(() => vm.HasNoPttBinding));
        Assert.Equal("Keine PTT-Taste belegt", await OnUi(() => vm.TalkHint));
        await OnUi(() =>
        {
            var s = vm.Settings;
            s.KeyBindings = [new KeyBinding(KeyAction.PushToTalk, new KeyChord(KeyPoller.VkXButton1))];
            vm.ApplySettings(s);
            return 0;
        });
        Assert.False(await OnUi(() => vm.HasNoPttBinding));
        Assert.Equal("PTT: Maustaste 4", await OnUi(() => vm.TalkHint));
    }

    /// <summary>Package 30: the logo is downloaded once, then taken from the cache; the bookmark tile shows it.</summary>
    [Fact]
    public async Task ServerIcon_LoadedOnce_ThenFromCache_ShownOnTile()
    {
        var data = Directory.CreateTempSubdirectory("ovs-logo-server-").FullName;
        var png = TestImages.Encode(128, 128);
        File.WriteAllBytes(Path.Combine(data, "server-icon.png"), png);
        await using var logoServer = await TestServer.StartAsync(dataDir: data);
        Task Connect() => ui.InvokeAsync<object?>(async () =>
        {
            await vm.ConnectAsync(new ConnectChoice("127.0.0.1", logoServer.Port, "anna", null, SaveBookmark: true));
            return null;
        });

        await Connect();
        byte[]? shown = null;
        for (int i = 0; i < 60 && shown is null; i++)
        {
            await Task.Delay(50);
            shown = await OnUi(() => vm.Server?.IconPng);
        }
        Assert.Equal(png, shown);

        await Connect(); // second time straight from the cache
        Assert.Equal(png, await OnUi(() => vm.Server!.IconPng));
        Assert.Equal(png, (await OnUi(() => vm.Bookmarks)).Single().Icon);

        var log = string.Join(Environment.NewLine, Directory.GetFiles(Path.Combine(dir, "logs"), "client-*.log").Select(path =>
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            return new StreamReader(stream).ReadToEnd();
        }));
        Assert.Equal(1, log.Split(Environment.NewLine).Count(l => l.Contains("Server-Logo anfordern")));
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

    /// <summary>Package 32: welcome in "Allgemein", the channel tab talks to the real server both ways.</summary>
    [Fact]
    public async Task Chat_EndToEnd()
    {
        await ConnectAsync(saveBookmark: false);
        await using var bert = await TestClient.ConnectAsync(server, "bert");
        Assert.Equal(["Allgemein", "Lobby"], await OnUi(() => vm.Chat!.Tabs.Select(t => t.Title).ToList()));
        Assert.Contains(await OnUi(() => vm.Chat!.General.Entries.ToList()), e => e.IsWelcome && e.Text == "Willkommen auf dem Testserver!");

        await bert.SendAsync(new SendChat(ChatTarget.Channel, null, "hallo anna"));
        ChatEntry? received = null;
        for (int i = 0; i < 60 && received is null; i++)
        {
            await Task.Delay(50);
            received = await OnUi(() => vm.Chat!.ChannelTab.Entries.FirstOrDefault(e => e.IsMessage));
        }
        Assert.Equal(("bert", "hallo anna", false), (received?.From, received?.Text, received?.IsOwn));
        Assert.Equal(1, await OnUi(() => vm.Chat!.ChannelTab.Unread));

        await ui.InvokeAsync<object?>(async () =>
        {
            vm.Chat!.Selected = vm.Chat.ChannelTab;
            vm.Chat.Draft = "hallo bert";
            await vm.Chat.SendCommand.ExecuteAsync(null);
            return null;
        });
        var echo = await bert.WaitForAsync<ChatMessage>(m => m.Text == "hallo bert");
        Assert.Equal("anna", echo.FromNickname);
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
        Assert.Equal("Keine PTT-Taste belegt", await OnUi(() => vm.TalkHint)); // new profiles have no keys (Package 29)
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

    /// <summary>A20: settings and administration are pages of the main window, not windows.</summary>
    [Fact]
    public async Task Pages_OpenAndClose_LevelMeterRuns()
    {
        await ui.InvokeAsync<object?>(async () =>
        {
            vm.OpenSettings();
            Assert.Equal(Page.Settings, vm.Page);
            vm.SettingsPage!.CancelCommand.Execute(null);
            Assert.Equal(Page.Home, vm.Page);
            Assert.Null(vm.SettingsPage);

            await vm.OpenAdminAsync(); // not connected: nothing to administer
            Assert.Equal(Page.Home, vm.Page);
            vm.Server = FakeServers.Admin();
            await vm.OpenAdminAsync();
            Assert.Equal(Page.Admin, vm.Page);
            await vm.DisconnectAsync(); // the administration needs a server
            Assert.Equal(Page.Home, vm.Page);
            Assert.Null(vm.AdminPage);
            return null;
        });

        await OnUi(() =>
        {
            vm.OpenSettings();
            vm.Audio.SetTone(440);
            return 0;
        });
        double level = -60;
        for (int i = 0; i < 60 && level < -30; i++)
        {
            await Task.Delay(50);
            level = await OnUi(() => vm.SettingsPage!.InputLevelDb);
        }
        Assert.InRange(level, -15, -12); // 0.3 amplitude sine: -13.5 dBFS

        await OnUi(() =>
        {
            vm.SettingsPage!.SelectedTheme = SettingsViewModel.Themes.Single(t => t.Value == AppTheme.Dark);
            vm.SettingsPage.SaveCommand.Execute(null);
            return 0;
        });
        Assert.Equal(Page.Home, await OnUi(() => vm.Page));
        Assert.Equal(AppTheme.Dark, ClientSettings.Load(dir, out _).Theme);
    }

    [Fact]
    public void Deafened_Engine_IgnoresIncomingVoice()
    {
        using var keys = new KeyPoller(); // no bindings: only simulated keys count
        using var engine = new AudioEngine(keys, useDevices: false) { Deafened = true };
        engine.OnVoice(7, 0, 0, new VoiceEncoder().Encode(new float[AudioFormat.FrameSamples]));
        Assert.Empty(engine.FramesReceived);
        Assert.Equal(0, engine.Mixer.SpeakerCount);
    }

    [Fact]
    public async Task ToneInput_ReportsInputLevel()
    {
        using var keys = new KeyPoller(); // no bindings: only simulated keys count
        using var engine = new AudioEngine(keys, useDevices: false);
        var level = new TaskCompletionSource<float>();
        engine.InputLevel += db => level.TrySetResult(db);
        engine.SetTone(440);
        // 0.3 amplitude sine: RMS 0.212 = -13.5 dBFS
        Assert.InRange(await level.Task.WaitAsync(TimeSpan.FromSeconds(3)), -15f, -12f);
    }
}
