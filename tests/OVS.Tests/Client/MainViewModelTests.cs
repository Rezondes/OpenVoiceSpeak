using System.Collections.Concurrent;
using NAudio.Wave;
using OVS.Client.Audio;
using OVS.Client.Input;
using OVS.Client.Net;
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
    readonly FakeKeys keys = new(); // Package 125: a real key pressed while the tests run does not count
    TestServer server = null!;
    MainViewModel vm = null!;

    public async Task InitializeAsync()
    {
        server = await TestServer.StartAsync(d => d.Settings.WelcomeText = "Willkommen auf dem Testserver!");
        vm = await ui.InvokeAsync(() => Task.FromResult(new MainViewModel(dir, ui.Post, useAudioDevices: false, keyState: keys.IsDown)));
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

    /// <summary>
    /// Reads on the UI thread until the value fits, at most 10 s by the clock (not by a loop count: network, audio and
    /// pool threads may come late on a busy runner). Returns the last value, so the caller's assert names it.
    /// </summary>
    async Task<T> OnUiUntil<T>(Func<T> read, Func<T, bool> done)
    {
        var watch = System.Diagnostics.Stopwatch.StartNew();
        while (true)
        {
            var value = await OnUi(read);
            if (done(value) || watch.ElapsedMilliseconds > 10_000) return value;
            await Task.Delay(20);
        }
    }

    Task ConnectAsync(bool saveBookmark) =>
        ui.InvokeAsync<object?>(async () =>
        {
            await vm.ConnectAsync(new ConnectChoice("127.0.0.1", server.Port, "anna", null, saveBookmark));
            return null;
        });

    /// <summary>Package 34: in a muted channel the client warns and does not send.</summary>
    [Fact]
    public async Task ChannelMuted_HintAndNoSending()
    {
        var (hint, muted) = await OnUi(() =>
        {
            vm.Server = FakeServers.Admin();
            vm.Server.Apply(new ChannelUpdated(new ChannelInfo(FakeServers.Lobby, "Lobby", "Start", 0, IsMuted: true)));
            vm.Tick();
            return (vm.VoiceHint, vm.Audio.ChannelMuted);
        });
        Assert.Equal("Stummer Channel: niemand hört dich.", hint);
        Assert.True(muted);
        await ui.InvokeAsync<object?>(async () =>
        {
            await vm.DisconnectAsync();
            return null;
        });
        Assert.False(await OnUi(() => vm.Audio.ChannelMuted));
    }

    /// <summary>Package 132 (A141): the encoder's bitrate is the own channel's: on join, move and when that channel is edited.</summary>
    [Fact]
    public async Task Bitrate_FollowsCurrentChannel()
    {
        var steps = await OnUi(() =>
        {
            var seen = new List<int>();
            vm.Server = FakeServers.Admin();
            seen.Add(vm.Audio.ChannelBitrate); // the Lobby at its default
            vm.Server.Apply(new ChannelUpdated(new ChannelInfo(FakeServers.Raid, "Raid", "", 1, Bitrate: 96)));
            seen.Add(vm.Audio.ChannelBitrate); // another channel: nothing changes
            vm.Server.Apply(new UserUpdated(new UserInfo(1, "fp1", "ich", FakeServers.Raid, false, false, false,
                OVS.Shared.Permissions.Permission.All, [OVS.Shared.Permissions.WellKnownGroups.Admin])));
            seen.Add(vm.Audio.ChannelBitrate); // moved into it
            vm.Server.Apply(new ChannelUpdated(new ChannelInfo(FakeServers.Raid, "Raid", "", 1, Bitrate: 24)));
            seen.Add(vm.Audio.ChannelBitrate); // the own channel edited
            return seen;
        });
        Assert.Equal([64, 64, 96, 24], steps);
        await ui.InvokeAsync<object?>(async () =>
        {
            await vm.DisconnectAsync();
            return null;
        });
        Assert.Equal(64, await OnUi(() => vm.Audio.ChannelBitrate));
    }

    // ---- Package 131 (A140): talking into a muted microphone ----

    const string SelfMutedHint = "Du bist stummgeschaltet: niemand hört dich.";

    /// <summary>Holds or lets go of an action's key and ticks until the client has seen it; returns the hint then.</summary>
    async Task<string> HoldKey(KeyAction action, bool down)
    {
        vm.Keys.Simulate(action, down);
        await OnUiUntil(() => vm.Keys.IsDown(action), d => d == down);
        return await OnUi(() =>
        {
            vm.Tick();
            return vm.VoiceHint;
        });
    }

    Task<string> TickHint() => OnUi(() =>
    {
        vm.Tick();
        return vm.VoiceHint;
    });

    [Fact]
    public async Task TalkWhileMuted_ShowsHint_2s()
    {
        var time = new ManualTimeProvider();
        await OnUi(() =>
        {
            vm.Clock = time;
            vm.Server = FakeServers.Admin();
            return vm.Server.SetSelfStateAsync(true, false);
        });
        Assert.Equal("", await TickHint()); // muted, but not talking
        Assert.Equal(SelfMutedHint, await HoldKey(KeyAction.PushToTalk, true));
        time.Advance(TimeSpan.FromSeconds(5)); // held: stays
        Assert.Equal(SelfMutedHint, await TickHint());
        Assert.Equal(SelfMutedHint, await HoldKey(KeyAction.PushToTalk, false));
        time.Advance(TimeSpan.FromMilliseconds(1900));
        Assert.Equal(SelfMutedHint, await TickHint());
        time.Advance(TimeSpan.FromMilliseconds(200));
        Assert.Equal("", await TickHint()); // 2 s after the release
    }

    [Theory]
    [InlineData("deafened", "Du bist taub geschaltet: niemand hört dich.")]
    [InlineData("server", "Du bist vom Server stummgeschaltet: niemand hört dich.")]
    public async Task TalkWhileDeafenedOrServerMuted_ShowsHint(string state, string hint)
    {
        await OnUi(() =>
        {
            vm.Server = FakeServers.Admin();
            if (state == "deafened") return vm.Server.SetSelfStateAsync(true, true);
            vm.Server.Apply(new UserUpdated(vm.Server.Mirror.Self! with { ServerMuted = true }));
            return Task.CompletedTask;
        });
        Assert.Equal("", await TickHint());
        Assert.Equal(hint, await HoldKey(KeyAction.LinkPushToTalk, true)); // link push-to-talk counts too
        await HoldKey(KeyAction.LinkPushToTalk, false);
    }

    [Fact]
    public async Task VoiceWhileMuted_ShowsHint()
    {
        await OnUi(() =>
        {
            vm.Server = FakeServers.Admin();
            return vm.Server.SetSelfStateAsync(true, false);
        });
        vm.Audio.Decide(true); // the voice gate hears voice, the capture thread asks
        Assert.Equal("", await TickHint()); // push-to-talk mode: voice alone is no attempt to talk
        await OnUi(() =>
        {
            vm.ApplySettings(new ClientSettings { Mode = TransmitMode.VoiceActivation });
            return vm.Audio.Decide(false);
        });
        Assert.Equal("", await TickHint());
        vm.Audio.Decide(true);
        Assert.Equal(SelfMutedHint, await TickHint());
    }

    [Fact]
    public async Task NotMuted_NoHint()
    {
        await OnUi(() => vm.Server = FakeServers.Admin());
        Assert.Equal("", await HoldKey(KeyAction.PushToTalk, true));
        await HoldKey(KeyAction.PushToTalk, false);

        // Package 125: push-to-mute is muting on purpose, so no hint
        vm.Keys.Simulate(KeyAction.PushToMute, true);
        Assert.True(await OnUiUntil(() => vm.Server!.SelfMuted, m => m));
        Assert.Equal("", await HoldKey(KeyAction.PushToTalk, true));
        await HoldKey(KeyAction.PushToTalk, false);
        await HoldKey(KeyAction.PushToMute, false);

        // the existing hints come first
        await OnUi(() =>
        {
            vm.Server!.Apply(new ChannelUpdated(new ChannelInfo(FakeServers.Lobby, "Lobby", "Start", 0, IsMuted: true)));
            return vm.Server.SetSelfStateAsync(true, false);
        });
        Assert.Equal("Stummer Channel: niemand hört dich.", await HoldKey(KeyAction.PushToTalk, true));
        await HoldKey(KeyAction.PushToTalk, false);
    }

    string ClientLogText()
    {
        vm.Log.Flush(); // Package 124: written in the background
        return string.Join(Environment.NewLine, Directory.GetFiles(Path.Combine(dir, "logs"), "client-*.log").Select(path =>
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            return new StreamReader(stream).ReadToEnd();
        }));
    }

    /// <summary>Package 39: a wrong password is never stored, the right one only when asked for, and never logged.</summary>
    [Fact]
    public async Task Connect_SavePassword_OnlyAfterSuccess()
    {
        await using var locked = await TestServer.StartAsync(password: "richtig-geheim");
        Task Connect(string password, bool save) => ui.InvokeAsync<object?>(async () =>
        {
            await vm.ConnectAsync(new ConnectChoice("127.0.0.1", locked.Port, "anna", password, SaveBookmark: true, SavePassword: save));
            return null;
        });

        await Connect("falsch-geheim", save: true);
        Assert.False(await OnUi(() => vm.IsConnected));
        Assert.False(ClientSettings.Load(dir, out _).Bookmarks.Single().HasSavedPassword);

        await Connect("richtig-geheim", save: true);
        Assert.True(await OnUi(() => vm.IsConnected));
        Assert.Equal("richtig-geheim", ClientSettings.Load(dir, out _).Bookmarks.Single().SavedPassword());

        await Connect("richtig-geheim", save: false); // unticked: the stored one goes away
        Assert.False(ClientSettings.Load(dir, out _).Bookmarks.Single().HasSavedPassword);

        var log = ClientLogText();
        Assert.DoesNotContain("geheim", log);
        Assert.Contains("Passwort verschlüsselt gespeichert", log);
    }

    // ---- Package 40: bookmarks in the sidebar ----

    async Task<TestServer> LockedServerAsync() => await TestServer.StartAsync(password: "richtig");

    /// <summary>Like a click in the sidebar: the bookmark is one of the saved ones.</summary>
    Task ConnectBookmark(Bookmark bookmark) => ui.InvokeAsync<object?>(async () =>
    {
        vm.Settings.Bookmarks.Add(bookmark);
        await vm.ConnectBookmarkAsync(bookmark);
        return null;
    });

    [Fact]
    public async Task Bookmark_Connect_UsesSavedPassword()
    {
        await using var locked = await LockedServerAsync();
        int asked = 0;
        vm.Dialogs = new Dialogs { AskPassword = _ => { asked++; return Task.FromResult<PasswordAnswer?>(null); } };
        await ConnectBookmark(new Bookmark("Gilde", "127.0.0.1", locked.Port, "anna", PasswordProtector.Protect("richtig")));
        Assert.True(await OnUi(() => vm.IsConnected));
        Assert.Equal(0, asked);
    }

    [Fact]
    public async Task Bookmark_WrongSavedPassword_AsksAndSavesNew()
    {
        await using var locked = await LockedServerAsync();
        var answers = new Queue<PasswordAnswer?>([new PasswordAnswer("auch falsch", true), new PasswordAnswer("richtig", true)]);
        vm.Dialogs = new Dialogs { AskPassword = _ => Task.FromResult(answers.Dequeue()) };
        await ConnectBookmark(new Bookmark("Gilde", "127.0.0.1", locked.Port, "anna", PasswordProtector.Protect("veraltet")));

        Assert.True(await OnUi(() => vm.IsConnected));
        Assert.Empty(answers); // asked twice: the second answer fitted
        var saved = ClientSettings.Load(dir, out _).Bookmarks.Single();
        Assert.Equal(("Gilde", "richtig"), (saved.Name, saved.SavedPassword()));
    }

    [Fact]
    public async Task Bookmark_AskPassword_Cancel_StaysDisconnected()
    {
        await using var locked = await LockedServerAsync();
        vm.Dialogs = new Dialogs { AskPassword = _ => Task.FromResult<PasswordAnswer?>(null) };
        await ConnectBookmark(new Bookmark("Gilde", "127.0.0.1", locked.Port, "anna"));
        Assert.False(await OnUi(() => vm.IsConnected));
        Assert.StartsWith("Abgelehnt", await OnUi(() => vm.Status));
    }

    [Fact]
    public async Task Bookmark_EditAndDelete_Persist()
    {
        var original = new Bookmark("Alt", "alt.example.org", 7000, "anna");
        await OnUi(() =>
        {
            vm.Settings.Bookmarks.Add(original);
            vm.Settings.Save(dir);
            return 0;
        });
        vm.Dialogs = new Dialogs
        {
            EditBookmark = b => Task.FromResult<BookmarkEdit?>(new BookmarkEdit("Neu", "neu.example.org", 7100, "berta", "pw", SavePassword: true)),
            Confirm = _ => Task.FromResult(true),
        };
        await ui.InvokeAsync<object?>(async () =>
        {
            await vm.EditBookmarkAsync(original);
            return null;
        });
        var edited = ClientSettings.Load(dir, out _).Bookmarks.Single();
        Assert.Equal(("Neu", "neu.example.org", 7100, "berta", "pw"), (edited.Name, edited.Host, edited.Port, edited.Nickname, edited.SavedPassword()));

        await ui.InvokeAsync<object?>(async () =>
        {
            await vm.DeleteBookmarkAsync(vm.Settings.Bookmarks.Single());
            return null;
        });
        Assert.Empty(ClientSettings.Load(dir, out _).Bookmarks);
        Assert.False(await OnUi(() => vm.HasBookmarks));
    }

    /// <summary>Package 43: at start a newer release is offered; "Später" downloads nothing; switched off, nothing is asked.</summary>
    [Fact]
    public async Task StartupCheck_Prompt_LaterDoesNothing()
    {
        var release = new
        {
            tag_name = "deploy-bbbbbbb",
            name = "OpenVoiceSpeak 280926.0b2c",
            body = "",
            published_at = DateTimeOffset.UtcNow,
            assets = new[]
            {
                new { name = "OVS.Client.exe", browser_download_url = "https://example.org/OVS.Client.exe" },
                new { name = "OVS.Client.exe.sha256", browser_download_url = "https://example.org/OVS.Client.exe.sha256" },
            },
        };
        var http = new FakeHttp(_ => FakeHttp.Json(release));
        var offered = new List<string>();
        var running = new OVS.Shared.BuildInfo(DateTimeOffset.UtcNow.AddDays(-1), "aaaaaaa", IsCi: true);
        await OnUi(() =>
        {
            vm.Updates = new OVS.Client.Net.UpdateChecker(new HttpClient(http), running);
            vm.Installer = new OVS.Client.Net.UpdateInstaller(new HttpClient(http), Path.Combine(dir, "gibt-es-nicht.exe"), _ => offered.Add("neu gestartet"));
            vm.Dialogs = new Dialogs { OfferUpdate = offer => { offered.Add(offer.Version); return Task.FromResult(false); } };
            return 0;
        });
        await ui.InvokeAsync<object?>(async () =>
        {
            await vm.StartupUpdateCheckAsync();
            return null;
        });
        Assert.Equal(["280926.0b2c"], offered);
        Assert.Single(http.Requests); // only the question, no download

        await OnUi(() =>
        {
            var s = vm.Settings;
            s.CheckForUpdates = false;
            vm.ApplySettings(s);
            return 0;
        });
        await ui.InvokeAsync<object?>(async () =>
        {
            await vm.StartupUpdateCheckAsync();
            return null;
        });
        Assert.Single(http.Requests);
    }

    /// <summary>Package 62: once accepted, the progress card is up until the update fails (or starts).</summary>
    [Fact]
    public async Task Update_Accepted_ShowsProgress_UntilFailure()
    {
        var release = new
        {
            tag_name = "deploy-bbbbbbb",
            name = "OpenVoiceSpeak 280926.0b2c",
            body = "",
            published_at = DateTimeOffset.UtcNow,
            assets = new[]
            {
                new { name = "OVS.Client.exe", browser_download_url = "https://example.org/OVS.Client.exe" },
                new { name = "OVS.Client.exe.sha256", browser_download_url = "https://example.org/OVS.Client.exe.sha256" },
            },
        };
        var http = new FakeHttp(r => r.RequestUri!.Host == "api.github.com" ? FakeHttp.Json(release)
            : r.RequestUri.AbsolutePath.EndsWith(".sha256") ? FakeHttp.Bytes(System.Text.Encoding.ASCII.GetBytes(new string('0', 64)))
            : FakeHttp.Bytes(new byte[200_000]));
        var running = new OVS.Shared.BuildInfo(DateTimeOffset.UtcNow.AddDays(-1), "aaaaaaa", IsCi: true);
        var seen = new List<OVS.Client.Net.UpdateProgress?>();
        await OnUi(() =>
        {
            File.WriteAllText(Path.Combine(dir, "OVS.Client.exe"), "alt");
            vm.Updates = new OVS.Client.Net.UpdateChecker(new HttpClient(http), running);
            vm.Installer = new OVS.Client.Net.UpdateInstaller(new HttpClient(http), Path.Combine(dir, "OVS.Client.exe"), _ => { });
            vm.Dialogs = new Dialogs { OfferUpdate = _ => Task.FromResult(true) };
            vm.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(MainViewModel.UpdateInProgress)) seen.Add(vm.UpdateInProgress);
            };
            return 0;
        });
        var result = await ui.InvokeAsync(() => vm.CheckForUpdatesAsync());

        Assert.Equal(OVS.Client.Localization.Strings.Update_Damaged, result);
        Assert.Contains(seen, p => p is { Phase: OVS.Client.Net.UpdatePhase.Downloading, Bytes: 200_000, Total: 200_000 });
        Assert.All(seen.OfType<OVS.Client.Net.UpdateProgress>(), p => Assert.Equal("280926.0b2c", p.Version));
        Assert.Null(seen[^1]);
        Assert.False(await OnUi(() => vm.IsUpdating));
    }

    [Fact]
    public async Task Connect_SavesBookmark_ToSettingsFile()
    {
        await ConnectAsync(saveBookmark: true);
        Assert.True(await OnUi(() => vm.IsConnected));

        var saved = ClientSettings.Load(dir, out _);
        var bookmark = Assert.Single(saved.Bookmarks);
        Assert.Equal(("127.0.0.1", server.Port, "anna"), (bookmark.Host, bookmark.Port, bookmark.Nickname));
    }

    /// <summary>
    /// Holds a key until the client has seen it, then lets go. A fixed short press could fall between two polls of the
    /// key thread when the machine is busy (CI), and then nothing happened at all.
    /// </summary>
    async Task PressUntil(KeyAction action, Func<bool> done)
    {
        vm.Keys.Simulate(action, true);
        try
        {
            var watch = System.Diagnostics.Stopwatch.StartNew();
            while (!await OnUi(done) && watch.ElapsedMilliseconds < 5000) await Task.Delay(10);
        }
        finally
        {
            vm.Keys.Simulate(action, false);
        }
    }

    /// <summary>Package 29: toggle keys work like the buttons and reach the server.</summary>
    [Fact]
    public async Task ToggleActions_SyncWithServer()
    {
        await ConnectAsync(saveBookmark: false);
        await using var bert = await TestClient.ConnectAsync(server, "bert");
        var annaId = await OnUi(() => vm.Server!.Mirror.SelfId);

        await PressUntil(KeyAction.ToggleMute, () => vm.Server!.SelfMuted);
        var muted = await bert.WaitForAsync<UserUpdated>(u => u.User.SessionId == annaId && u.User.SelfMuted);
        Assert.False(muted.User.SelfDeafened);
        Assert.True(await OnUi(() => vm.Server!.SelfMuted));

        await PressUntil(KeyAction.ToggleDeafen, () => vm.Server!.SelfDeafened);
        await bert.WaitForAsync<UserUpdated>(u => u.User.SessionId == annaId && u.User.SelfDeafened);
    }

    [Fact]
    public async Task TalkHint_NoPttBinding_UntilBound()
    {
        await OnUi(() => // an existing profile without keys (a new one gets a PTT key, Package 59)
        {
            var s = vm.Settings;
            s.KeyBindings = [];
            vm.ApplySettings(s);
            return 0;
        });
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

        await OnUi(() => // Package 41: several PTT keys are all listed
        {
            var s = vm.Settings;
            s.KeyBindings = [new KeyBinding(KeyAction.PushToTalk, new KeyChord(KeyPoller.VkXButton1)), new KeyBinding(KeyAction.PushToTalk, new KeyChord(KeyPoller.VkXButton2))];
            vm.ApplySettings(s);
            return 0;
        });
        Assert.Equal("PTT: Maustaste 4, Maustaste 5", await OnUi(() => vm.TalkHint));
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
        var shown = await OnUiUntil(() => vm.Server?.IconPng, s => s is not null);
        Assert.Equal(png, shown);

        await Connect(); // second time straight from the cache
        Assert.Equal(png, await OnUi(() => vm.Server!.IconPng));
        Assert.Equal(png, (await OnUi(() => vm.Bookmarks)).Single().Icon);

        var log = ClientLogText();
        Assert.Equal(1, log.Split(Environment.NewLine).Count(l => l.Contains("Server-Logo anfordern")));
    }

    /// <summary>A refused logo request is sent once more after the server's interval, if the logo is still wanted.</summary>
    [Fact]
    public async Task ServerIcon_RateLimited_RetriedOnceAfterInterval()
    {
        var time = new ManualTimeProvider();
        var sent = new List<Request>();
        var snapshot = new ServerSnapshot(new ServerSettingsInfo("Gilde", "", false, IconHash: "abc"), FakeServers.Lobby,
            [new ChannelInfo(FakeServers.Lobby, "Lobby", "", 0)], [], [],
            [new UserInfo(1, "fp1", "ich", FakeServers.Lobby, false, false, false, OVS.Shared.Permissions.Permission.None, [])]);
        var server = new ServerViewModel(new StateMirror(new Welcome(1, "", snapshot)), r =>
        {
            lock (sent) sent.Add(r);
            return Task.CompletedTask;
        }, time);
        int Requests()
        {
            lock (sent) return sent.Count(r => r is GetServerIcon);
        }
        string LastId()
        {
            lock (sent) return sent.Last(r => r is GetServerIcon).RequestId!;
        }
        // The retry comes from a delay on the manual clock (continued on the pool) and is posted to the UI thread:
        // a retry is waited for up to 10 s, the "no second retry" check keeps a fixed one-second window.
        async Task<int> AfterRefusal(bool expectRetry)
        {
            var id = LastId();
            await OnUi(() =>
            {
                server.Apply(new Error(id, Codes.RateLimited));
                return 0;
            });
            var before = Requests();
            time.Advance(TimeSpan.FromSeconds(11));
            var watch = System.Diagnostics.Stopwatch.StartNew();
            while (Requests() == before && watch.ElapsedMilliseconds < (expectRetry ? 10_000 : 1000))
            {
                await Task.Delay(50);
                await OnUi(() => 0); // lets the posted retry run
            }
            return Requests();
        }

        await OnUi(() =>
        {
            vm.Server = server;
            vm.Server = server;
            vm.TrackServerIcon(server, "127.0.0.1", 1);
            return 0;
        });
        Assert.Equal(1, Requests());
        Assert.Equal(2, await AfterRefusal(expectRetry: true));
        Assert.Equal(2, await AfterRefusal(expectRetry: false)); // only once
    }

    [Fact]
    public async Task Connect_ShowsWelcomeText_AndPing()
    {
        await ConnectAsync(saveBookmark: false);
        Assert.Contains(await OnUi(() => vm.Notices.ToList()), n => n.Kind == NoticeKind.Welcome && n.Text == "Willkommen auf dem Testserver!");

        var ping = await OnUiUntil(() =>
        {
            vm.Tick();
            return vm.PingText;
        }, p => p.Length > 0);
        Assert.StartsWith("Ping ", ping);
    }

    /// <summary>Package 126: an outage after UDP worked is logged once when lost and once when back.</summary>
    [Fact]
    public async Task UdpOutage_LoggedOnceEachWay()
    {
        using var relay = new LossyUdpRelay(server.VoiceEndPoint);
        var time = new ManualTimeProvider();
        await OnUi(() => (vm.VoiceRelay, vm.VoiceTime) = (relay.EndPoint, time));
        await ConnectAsync(saveBookmark: false);
        string Logged()
        {
            vm.Tick();
            return ClientLogText();
        }
        Assert.Contains("UDP erreichbar", await OnUiUntil(Logged, l => l.Contains("UDP erreichbar")));

        relay.DropAll = true;
        for (int i = 0; i < 40; i++) time.Advance(TimeSpan.FromMilliseconds(500)); // 20 s
        Assert.Contains("UDP nicht erreichbar", await OnUi(Logged));
        relay.DropAll = false;
        for (int i = 0; i < 10; i++) time.Advance(TimeSpan.FromMilliseconds(500));
        await OnUiUntil(Logged, l => l.Contains("UDP wieder erreichbar"));

        var log = await OnUi(Logged); // one more tick adds nothing
        foreach (var line in new[] { "UDP erreichbar", "UDP nicht erreichbar", "UDP wieder erreichbar" })
            Assert.Single(log.Split(Environment.NewLine), l => l.Contains(line));
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
        var received = await OnUiUntil(() => vm.Chat!.ChannelTab.Entries.FirstOrDefault(e => e.IsMessage), e => e is not null);
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

    /// <summary>Package 33: an incoming whisper opens a tab in the background, the reply goes back only to bert.</summary>
    [Fact]
    public async Task Private_EndToEnd()
    {
        await ConnectAsync(saveBookmark: false);
        await using var bert = await TestClient.ConnectAsync(server, "bert");
        await using var carla = await TestClient.ConnectAsync(server, "carla");
        var annaId = await OnUi(() => vm.Server!.Mirror.SelfId);

        await bert.SendAsync(new SendChat(ChatTarget.Private, annaId, "psst"));
        var tab = await OnUiUntil(() => vm.Chat!.Tabs.FirstOrDefault(t => t.IsPrivate), t => t is not null);
        Assert.Equal(("@bert", 1), (tab?.Title, tab?.Unread));
        Assert.True(await OnUi(() => vm.Chat!.Selected.IsGeneral)); // in the background

        await ui.InvokeAsync<object?>(async () =>
        {
            vm.Server!.Channels.SelectMany(c => c.Users).Single(u => u.Nickname == "bert").MessageCommand.Execute(null);
            vm.Chat!.Draft = "psst zurück";
            await vm.Chat.SendCommand.ExecuteAsync(null);
            return null;
        });
        Assert.Same(tab, await OnUi(() => vm.Chat!.Selected));
        var reply = await bert.WaitForAsync<ChatMessage>(m => m.Text == "psst zurück");
        Assert.Equal(ChatTarget.Private, reply.Target);
        await carla.AssertNoMessageAsync<ChatMessage>();
    }

    [Fact]
    public async Task Notices_HaveKinds_ErrorAndDisconnect()
    {
        await ConnectAsync(saveBookmark: false);
        await ui.InvokeAsync<object?>(async () =>
        {
            await vm.Server!.SendAsync(new CreateChannel("Raid", "")); // a guest may not
            return null;
        });
        var error = await OnUiUntil(() => vm.Notices.FirstOrDefault(n => n.Kind == NoticeKind.Error), n => n is not null);
        Assert.Equal("Dafür fehlt dir das Recht.", error?.Text);

        await server.Control.StopAsync();
        var bye = await OnUiUntil(() => vm.Notices.FirstOrDefault(n => n.Kind == NoticeKind.Warning), n => n is not null);
        Assert.StartsWith("Getrennt: ", bye?.Text);
        Assert.Matches(@"^\d{2}:\d{2}:\d{2}  Getrennt: ", bye!.ToString()); // the debug API keeps its "time  text" form
    }

    [Fact]
    public async Task StartScreen_BookmarksAndConnectingState()
    {
        Assert.False(await OnUi(() => vm.HasBookmarks));
        Assert.Equal($"PTT: {DefaultKeys.PushToTalk(KeyPoller.MouseButtonCount()).Name}", await OnUi(() => vm.TalkHint)); // Package 59: new profiles get a PTT key
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
        var level = await OnUiUntil(() => vm.SettingsPage!.InputLevelDb, l => l >= -30);
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

    /// <summary>
    /// Package 52: listing the audio devices took about 650 ms on the UI thread (a name per device), so the whole app
    /// froze for a second whenever the settings opened. The list now loads in the background.
    /// </summary>
    [Fact]
    public async Task OpenSettings_DoesNotWaitForDeviceList()
    {
        using var gate = new ManualResetEventSlim();
        var settings = new ClientSettings { InputDeviceId = "mic-2" };
        settings.Save(dir);
        var main = await ui.InvokeAsync(() => Task.FromResult(new MainViewModel(dir, ui.Post, useAudioDevices: false)));
        main.DeviceSource = flow =>
        {
            gate.Wait(TimeSpan.FromSeconds(10));
            return flow == NAudio.CoreAudioApi.DataFlow.Capture
                ? [new AudioDevice("mic-1", "Headset"), new AudioDevice("mic-2", "XLR Mic")]
                : [new AudioDevice("out-1", "Kopfhörer")];
        };

        var watch = System.Diagnostics.Stopwatch.StartNew();
        var (page, inputs, hint) = await OnUi(() =>
        {
            main.OpenSettings();
            return (main.Page, main.SettingsPage!.Inputs.Select(i => i.Name).ToList(), main.SettingsPage.DeviceHint);
        });
        // not blocked by the gate (which holds 10 s): the margin is for a slow runner, the UI round trip alone may take a while
        Assert.True(watch.ElapsedMilliseconds < 5000, $"{watch.ElapsedMilliseconds} ms");
        Assert.Equal(Page.Settings, page);
        Assert.Equal(["Standardgerät", "Geräte werden geladen ..."], inputs);
        Assert.Null(hint);

        gate.Set();
        await OnUiUntil(() => main.SettingsPage!.Inputs.Count, n => n == 3);
        Assert.Equal(["Standardgerät", "Headset", "XLR Mic"], await OnUi(() => main.SettingsPage!.Inputs.Select(i => i.Name).ToList()));
        Assert.Equal("mic-2", await OnUi(() => main.SettingsPage!.SelectedInput.Id));
        Assert.Null(await OnUi(() => main.SettingsPage!.DeviceHint));

        // second time: the last list is there at once, a fresh one follows
        gate.Reset();
        var again = await OnUi(() =>
        {
            main.ClosePage();
            main.OpenSettings();
            return main.SettingsPage!.Inputs.Select(i => i.Name).ToList();
        });
        Assert.Equal(["Standardgerät", "Headset", "XLR Mic"], again);
        gate.Set();
        await ui.InvokeAsync<object?>(async () =>
        {
            await main.DisposeAsync();
            return null;
        });
    }

    /// <summary>Package 61: the background changes while sliding, "Verwerfen" brings the saved one back.</summary>
    [Fact]
    public async Task Appearance_LivePreview_DiscardRestores_SaveKeeps()
    {
        Assert.Equal(new BackgroundAppearance(1f, false), await OnUi(() => vm.Appearance));
        var live = await OnUi(() =>
        {
            vm.OpenSettings();
            vm.SettingsPage!.BackgroundOpacityPercent = 30;
            vm.SettingsPage.BlurBackground = true;
            return vm.Appearance;
        });
        Assert.Equal(new BackgroundAppearance(0.3f, true), live with { Opacity = MathF.Round(live.Opacity, 3) });
        Assert.Equal(new BackgroundAppearance(1f, false), await OnUi(() =>
        {
            vm.SettingsPage!.CancelCommand.Execute(null);
            return vm.Appearance;
        }));
        var saved = await OnUi(() =>
        {
            vm.OpenSettings();
            vm.SettingsPage!.BackgroundOpacityPercent = 60;
            vm.SettingsPage.SaveCommand.Execute(null);
            return (vm.Appearance.Opacity, vm.Settings.BackgroundOpacity);
        });
        Assert.Equal((0.6f, 0.6f), (MathF.Round(saved.Item1, 3), MathF.Round(saved.Item2, 3)));
        Assert.Equal(0.6f, ClientSettings.Load(dir, out _).BackgroundOpacity, 3);
    }

    /// <summary>Package 99: the display mode is previewed at once, "Verwerfen" brings the saved one back, "Speichern" keeps it.</summary>
    [Fact]
    public async Task Display_LivePreviewAndCancelRevert()
    {
        var simplified = SettingsViewModel.Displays.Single(d => d.Value == DisplayMode.Simplified);
        Assert.Equal(DisplayMode.Animated, await OnUi(() => vm.Appearance.Display));
        Assert.Equal(DisplayMode.Simplified, await OnUi(() =>
        {
            vm.OpenSettings();
            vm.SettingsPage!.SelectedDisplay = simplified;
            return vm.Appearance.Display;
        }));
        Assert.Equal(DisplayMode.Animated, await OnUi(() =>
        {
            vm.SettingsPage!.CancelCommand.Execute(null);
            return vm.Appearance.Display;
        }));
        Assert.Equal(DisplayMode.Simplified, await OnUi(() =>
        {
            vm.OpenSettings();
            vm.SettingsPage!.SelectedDisplay = simplified;
            vm.SettingsPage.SaveCommand.Execute(null);
            return vm.Appearance.Display;
        }));
        Assert.Equal(DisplayMode.Simplified, ClientSettings.Load(dir, out _).Display);
    }

    /// <summary>
    /// Package 121: a key set on the page works at once (so the self test hears push-to-talk), leaving without saving
    /// brings the saved keys back, saving keeps the new ones.
    /// </summary>
    [Fact]
    public async Task SettingsPage_KeyBindingLive_RevertedOnCancel()
    {
        var key = new KeyBinding(KeyAction.PushToTalk, new KeyChord(KeyPoller.VkRControl));
        async Task<IReadOnlyList<KeyBinding>> SetKeyOnPage()
        {
            await ui.InvokeAsync<object?>(async () =>
            {
                vm.OpenSettings();
                vm.SettingsPage!.IsPushToTalk = true;
                vm.SettingsPage.EditKeyBinding = (_, _) => Task.FromResult<KeyBinding?>(key);
                await vm.SettingsPage.SetPttKeyCommand.ExecuteAsync(null);
                return null;
            });
            return await OnUi(() => vm.Keys.Bindings);
        }

        await OnUi(() =>
        {
            vm.ApplySettings(new ClientSettings { Mode = TransmitMode.VoiceActivation });
            return 0;
        });
        Assert.Empty(await OnUi(() => vm.Keys.Bindings));

        Assert.Equal([key], await SetKeyOnPage());
        Assert.Empty(await OnUi(() =>
        {
            vm.SettingsPage!.CancelCommand.Execute(null);
            return vm.Keys.Bindings;
        }));

        Assert.Equal([key], await SetKeyOnPage());
        Assert.Empty(await OnUi(() =>
        {
            vm.SettingsPage!.KeyBindings[0].RemoveCommand.Execute(null);
            return vm.Keys.Bindings;
        }));
        Assert.Empty(await OnUi(() =>
        {
            vm.ClosePage(); // what Esc does
            return vm.Keys.Bindings;
        }));

        Assert.Equal([key], await SetKeyOnPage());
        Assert.Equal([key], await OnUi(() =>
        {
            vm.SettingsPage!.SaveCommand.Execute(null);
            return vm.Keys.Bindings;
        }));
        Assert.Equal([key], ClientSettings.Load(dir, out _).KeyBindings);
    }

    /// <summary>Leaving the settings any other way (Esc, the administration) also drops the preview.</summary>
    [Fact]
    public async Task LeavingSettingsWithoutSaving_EndsThePreview()
    {
        var after = await OnUi(() =>
        {
            vm.OpenSettings();
            vm.SettingsPage!.SelectedDisplay = SettingsViewModel.Displays.Single(d => d.Value == DisplayMode.Simplified);
            vm.SettingsPage.BlurBackground = true;
            vm.ClosePage(); // what Esc does
            return vm.Appearance;
        });
        Assert.Equal(new BackgroundAppearance(1f, false, DisplayMode.Animated), after);
        Assert.Equal(DisplayMode.Animated, ClientSettings.Load(dir, out _).Display);
    }

    /// <summary>Package 53: the self test mutes and deafens (also at the server) and puts everything back afterwards.</summary>
    [Fact]
    public async Task SelfTest_MutesAndDeafens_RestoresPreviousState()
    {
        await ConnectAsync(saveBookmark: false);
        async Task<(bool Muted, bool Deafened)> SeenByServer(bool deafened)
        {
            await OnUiUntil(() => vm.Server!.Self!.IsDeafened, d => d == deafened);
            return await OnUi(() => (vm.Server!.Self!.StatusText.Length > 0, vm.Server.Self.IsDeafened));
        }
        Assert.Equal((false, false), await SeenByServer(false));

        await ui.InvokeAsync<object?>(async () =>
        {
            vm.OpenSettings();
            await vm.SettingsPage!.ToggleSelfTestCommand.ExecuteAsync(null);
            return null;
        });
        Assert.True(await OnUi(() => vm.Audio.SelfTest && vm.SettingsPage!.IsSelfTesting));
        Assert.Equal((true, true), await SeenByServer(true));

        await ui.InvokeAsync<object?>(async () =>
        {
            await vm.SettingsPage!.ToggleSelfTestCommand.ExecuteAsync(null);
            return null;
        });
        Assert.False(await OnUi(() => vm.Audio.SelfTest));
        Assert.Equal((false, false), await SeenByServer(false));

        // closing the settings ends it too; a slider moved meanwhile goes back on "Verwerfen"
        await ui.InvokeAsync<object?>(async () =>
        {
            await vm.SettingsPage!.ToggleSelfTestCommand.ExecuteAsync(null);
            vm.SettingsPage.OutputVolumePercent = 50;
            return null;
        });
        Assert.Equal(0.5f, await OnUi(() => vm.Audio.Mixer.Volume));
        Assert.Equal((true, true), await SeenByServer(true));
        await OnUi(() =>
        {
            vm.SettingsPage!.CancelCommand.Execute(null);
            return 0;
        });
        Assert.Equal((false, false), await SeenByServer(false));
        Assert.False(await OnUi(() => vm.Audio.SelfTest));
        Assert.Equal(1f, await OnUi(() => vm.Audio.Mixer.Volume));

        // disconnecting ends it as well
        await ui.InvokeAsync<object?>(async () =>
        {
            vm.OpenSettings();
            await vm.SettingsPage!.ToggleSelfTestCommand.ExecuteAsync(null);
            await vm.DisconnectAsync();
            return null;
        });
        Assert.False(await OnUi(() => vm.Audio.SelfTest || vm.SettingsPage!.IsSelfTesting));
    }

    /// <summary>Package 51: the slider acts on the mixer at once, is kept by fingerprint and follows a new session.</summary>
    [Fact]
    public async Task UserVolume_ChangesMixer_SavesByFingerprint()
    {
        var settingsBefore = await OnUi(() =>
        {
            var before = vm.Settings;
            vm.Server = FakeServers.Admin();
            vm.Server.Channels.SelectMany(c => c.Users).Single(u => u.Nickname == "anna").VolumePercent = 200;
            return before;
        });
        Assert.Equal(2f, await OnUi(() => vm.Audio.Mixer.SpeakerGain(2)));
        Assert.Equal(1f, await OnUi(() => vm.Audio.Mixer.SpeakerGain(1)));
        Assert.Same(settingsBefore, await OnUi(() => vm.Settings)); // no ApplySettings: the audio devices keep running
        Assert.Equal(2f, ClientSettings.Load(dir, out _).VolumeFor("fp2"));

        // anna reconnects with a new session id: her volume comes along
        await OnUi(() =>
        {
            vm.Server!.Apply(new UserLeft(2));
            vm.Server.Apply(new UserJoined(new UserInfo(9, "fp2", "anna", FakeServers.Lobby, false, false, false, OVS.Shared.Permissions.Permission.Speak, [])));
            return 0;
        });
        Assert.Equal(2f, await OnUi(() => vm.Audio.Mixer.SpeakerGain(9)));
        Assert.Equal(1f, await OnUi(() => vm.Audio.Mixer.SpeakerGain(2)));
        Assert.Equal(200, await OnUi(() => vm.Server!.Channels.SelectMany(c => c.Users).Single(u => u.SessionId == 9).VolumePercent));

        await OnUi(() =>
        {
            vm.Server!.Channels.SelectMany(c => c.Users).Single(u => u.SessionId == 9).ResetVolumeCommand.Execute(null);
            return 0;
        });
        Assert.Equal(1f, await OnUi(() => vm.Audio.Mixer.SpeakerGain(9)));
        Assert.DoesNotContain("fp2", File.ReadAllText(Path.Combine(dir, ClientSettings.FileName)));
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

    /// <summary>
    /// Package 129: in automatic mode the settings page shows the threshold the gate learned, reported with the level:
    /// the steady tone (-13.5 dBFS) sets it to its upper bound, -28 dBFS.
    /// </summary>
    [Fact]
    public async Task SettingsPage_ShowsThresholdInUse()
    {
        await OnUi(() =>
        {
            vm.ApplySettings(new ClientSettings { Mode = TransmitMode.VoiceActivation });
            vm.OpenSettings();
            vm.Audio.SetTone(440);
            return 0;
        });
        Assert.Equal(-28, await OnUiUntil(() => vm.SettingsPage!.ThresholdInUseDb, t => t == -28));
        Assert.Equal(-28, await OnUi(() => vm.SettingsPage!.MeterThresholdDb));
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
        Assert.InRange(await level.Task.WaitAsync(TimeSpan.FromSeconds(10)), -15f, -12f);
    }

    /// <summary>Package 102: a removed bookmark folds away in the sidebar; with no window it goes at once.</summary>
    [Fact]
    public async Task RemovedBookmark_FoldsAway()
    {
        var (shownAfter, liveAfter) = await OnUi(() =>
        {
            var settings = vm.Settings;
            settings.Bookmarks.Add(new Bookmark("A", "a.example.org", 7000, "ich"));
            settings.Bookmarks.Add(new Bookmark("B", "b.example.org", 7000, "ich"));
            vm.ApplySettings(settings);
            vm.Leave.Delay = TimeSpan.FromMinutes(1);
            var less = vm.Settings;
            less.Bookmarks.RemoveAll(b => b.Name == "B");
            vm.ApplySettings(less);
            return (vm.Bookmarks.Count, vm.Bookmarks.Live().Count());
        });
        Assert.Equal((2, 1), (shownAfter, liveAfter));
    }

    /// <summary>Package 99, 61: the saved look counts from the start (display, background opacity and blur).</summary>
    [Fact]
    public async Task SavedAppearance_AppliesAtStart()
    {
        var saved = Directory.CreateTempSubdirectory("ovs-look-").FullName;
        try
        {
            new ClientSettings { Display = DisplayMode.Simplified, BackgroundOpacity = 0.5f, BlurBackground = true }.Save(saved);
            var started = new MainViewModel(saved, a => a(), useAudioDevices: false);
            Assert.Equal(new BackgroundAppearance(0.5f, true, DisplayMode.Simplified), started.Appearance);
            await started.DisposeAsync();
        }
        finally
        {
            Directory.Delete(saved, true);
        }
    }

    const int MuteKey = 0x78, DeafenKey = 0x79, TalkKey = 0x7A; // F9, F10, F11

    /// <summary>Package 125: push-to-mute, a deafen and a talk key bound; the fake server collects the requests, the list the tones.</summary>
    async Task<(List<Request> Sent, List<SoundEvent> Heard)> WithPushToMute()
    {
        var sent = new List<Request>();
        var heard = new List<SoundEvent>();
        await OnUi(() =>
        {
            vm.ApplySettings(new ClientSettings
            {
                KeyBindings =
                [
                    new(KeyAction.PushToMute, new KeyChord(MuteKey)), new(KeyAction.ToggleDeafen, new KeyChord(DeafenKey)),
                    new(KeyAction.PushToTalk, new KeyChord(TalkKey)),
                ],
            });
            vm.Server = FakeServers.Admin(sent);
            vm.Server.SoundRequested += heard.Add;
            return 0;
        });
        return (sent, heard);
    }

    /// <summary>The mute states sent, without the request ids.</summary>
    static List<(bool Muted, bool Deafened)> States(List<Request> sent) => sent.OfType<SetSelfState>().Select(r => (r.Muted, r.Deafened)).ToList();

    /// <summary>Holds or lets go the push-to-mute key (through the bindings) and waits until the key thread saw it.</summary>
    async Task HoldMuteKey(bool down)
    {
        keys.Set(down, MuteKey);
        Assert.Equal(down, await OnUiUntil(() => vm.Keys.MuteHeld, h => h == down));
    }

    /// <summary>
    /// Presses the deafen key and waits for its effect. Key events reach the UI thread in order, so whatever the
    /// push-to-mute key did before is handled by then, and a check that it did nothing proves something.
    /// </summary>
    async Task PressDeafenKey()
    {
        bool before = await OnUi(() => vm.Server!.SelfDeafened);
        keys.Set(true, DeafenKey);
        Assert.NotEqual(before, await OnUiUntil(() => vm.Server!.SelfDeafened, d => d != before));
        keys.Set(false, DeafenKey);
    }

    /// <summary>
    /// Package 125: holding push-to-mute mutes like the button (state, tone, server); letting go undoes it. That
    /// nothing is sent meanwhile is the engine's part (SendPathTests.PushToMute_Held_SendsNothing).
    /// </summary>
    [Fact]
    public async Task PushToMuteHeld_MutesLikeButton_ReleaseRestores()
    {
        var (sent, heard) = await WithPushToMute();

        await HoldMuteKey(true);
        Assert.True(await OnUiUntil(() => vm.Server!.SelfMuted, m => m));
        Assert.Equal([(true, false)], await OnUi(() => States(sent)));
        Assert.Equal([SoundEvent.MicOff], await OnUi(() => heard.ToList()));

        await HoldMuteKey(false);
        Assert.False(await OnUiUntil(() => vm.Server!.SelfMuted, m => !m));
        Assert.Equal([(true, false), (false, false)], await OnUi(() => States(sent)));
        Assert.Equal([SoundEvent.MicOff, SoundEvent.MicOn], await OnUi(() => heard.ToList()));
    }

    /// <summary>Package 125: already muted or deafened, holding and letting go changes nothing.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PushToMute_AlreadyMuted_NoChange(bool deafened)
    {
        var (sent, heard) = await WithPushToMute();
        await OnUi(() =>
        {
            (deafened ? vm.Server!.ToggleDeafenCommand : vm.Server!.ToggleMuteCommand).Execute(null);
            sent.Clear();
            heard.Clear();
            return 0;
        });

        await HoldMuteKey(true);
        await HoldMuteKey(false);
        await PressDeafenKey();

        // only the deafen key: muted and deafened, or (from deafened) back to unmuted as before
        Assert.Equal([deafened ? (false, false) : (true, true)], await OnUi(() => States(sent)));
        Assert.Equal([deafened ? SoundEvent.SoundOn : SoundEvent.SoundOff], await OnUi(() => heard.ToList()));
    }

    /// <summary>Package 125: the mute button pressed while the key is held decides; letting go does not undo it.</summary>
    [Fact]
    public async Task MuteButtonWhileHeld_Sticks()
    {
        var (sent, _) = await WithPushToMute();
        await HoldMuteKey(true);
        await OnUiUntil(() => vm.Server!.SelfMuted, m => m);
        Assert.False(await OnUi(() =>
        {
            vm.Server!.ToggleMuteCommand.Execute(null);
            return vm.Server.SelfMuted;
        }));

        await HoldMuteKey(false);
        await PressDeafenKey();
        Assert.Equal([(true, false), (false, false), (true, true)], await OnUi(() => States(sent)));
    }

    /// <summary>Package 125: another hold key (push-to-talk) tapped meanwhile does not count as a new press of push-to-mute.</summary>
    [Fact]
    public async Task MuteButtonWhileHeld_TalkKeyTapped_Sticks()
    {
        var (sent, _) = await WithPushToMute();
        await HoldMuteKey(true);
        await OnUiUntil(() => vm.Server!.SelfMuted, m => m);
        await OnUi(() =>
        {
            vm.Server!.ToggleMuteCommand.Execute(null);
            return 0;
        });

        keys.Set(true, TalkKey);
        await OnUiUntil(() => vm.Keys.PttDown, d => d);
        keys.Set(false, TalkKey);
        await OnUiUntil(() => vm.Keys.PttDown, d => !d);
        await HoldMuteKey(false);
        await PressDeafenKey();
        Assert.Equal([(true, false), (false, false), (true, true)], await OnUi(() => States(sent)));
    }

    /// <summary>Package 125: connecting while the key is held mutes at once, and letting go unmutes.</summary>
    [Fact]
    public async Task PushToMute_HeldBeforeConnecting_MutesOnConnect()
    {
        await OnUi(() =>
        {
            vm.ApplySettings(new ClientSettings { KeyBindings = [new(KeyAction.PushToMute, new KeyChord(MuteKey))] });
            return 0;
        });
        await HoldMuteKey(true);
        var sent = new List<Request>();
        Assert.True(await OnUi(() =>
        {
            vm.Server = FakeServers.Admin(sent);
            return vm.Server.SelfMuted;
        }));
        await HoldMuteKey(false);
        Assert.False(await OnUiUntil(() => vm.Server!.SelfMuted, m => !m));
        Assert.Equal([(true, false), (false, false)], await OnUi(() => States(sent)));
    }

    /// <summary>Package 125: not connected, the key only stops sending, which the self test plays back.</summary>
    [Fact]
    public async Task PushToMute_NotConnected_OnlyStopsSending()
    {
        await ui.InvokeAsync<object?>(async () =>
        {
            vm.ApplySettings(new ClientSettings
            {
                Mode = TransmitMode.VoiceActivation,
                VadThresholdDb = -50,
                KeyBindings = [new(KeyAction.PushToMute, new KeyChord(MuteKey))],
            });
            vm.Audio.SetTone(440); // -13.5 dBFS: well above the threshold
            await vm.SetSelfTestAsync(true);
            return null;
        });
        Assert.True(await OnUiUntil(() => vm.Audio.LastOutputLevelDb, l => l > -20) > -20);

        await HoldMuteKey(true);
        Assert.True(await OnUiUntil(() => vm.Audio.LastOutputLevelDb, l => l < -100) < -100); // the loopback speaker times out

        await HoldMuteKey(false);
        Assert.True(await OnUiUntil(() => vm.Audio.LastOutputLevelDb, l => l > -20) > -20);
        Assert.Null(await OnUi(() => vm.Server));
        Assert.Empty(await OnUi(() => vm.Notices.ToList()));
    }

    // ---- Package 124: low CPU and RAM while idle ----

    /// <summary>Counts the microphones and speakers the engine opens and closes, without hardware.</summary>
    sealed class FakeAudioDevices : IAudioDeviceFactory
    {
        readonly List<CapturePipeline> captures = [];
        readonly List<FakeOutput> outputs = [];
        readonly List<string?> captureIds = [];

        public CapturePipeline OpenCapture(string? deviceId, List<string> warnings)
        {
            var capture = CapturePipeline.Manual();
            lock (captures)
            {
                captures.Add(capture);
                captureIds.Add(deviceId);
            }
            return capture;
        }

        public (BufferedWaveProvider Buffer, IDisposable Device) OpenOutput(string? deviceId, List<string> warnings)
        {
            var output = new FakeOutput();
            lock (outputs) outputs.Add(output);
            // Nothing plays it: one frame long, so it never holds the engine's 40 ms and the tones run through at once.
            var buffer = new BufferedWaveProvider(WaveFormat.CreateIeeeFloatWaveFormat(AudioFormat.SampleRate, 1))
            {
                BufferDuration = AudioFormat.FrameDuration,
                DiscardOnBufferOverflow = true,
            };
            return (buffer, output);
        }

        /// <summary>Opened so far and open now, for microphones and speakers.</summary>
        public (int Mics, int MicsOpen, int Speakers, int SpeakersOpen) Counts
        {
            get
            {
                lock (captures)
                lock (outputs)
                    return (captures.Count, captures.Count(c => !c.IsDisposed), outputs.Count, outputs.Count(o => !o.Disposed));
            }
        }

        public string? LastMicId
        {
            get { lock (captures) return captureIds[^1]; }
        }

        sealed class FakeOutput : IDisposable
        {
            public volatile bool Disposed;
            public void Dispose() => Disposed = true;
        }
    }

    /// <summary>
    /// Package 124: the microphone opens on a pool thread; one still queued when the engine goes away (closing the
    /// client with the settings page open) must not open it afterwards. Many engines, so the race shows.
    /// </summary>
    [Fact]
    public async Task AudioEngine_DisposedWhileMicOpening_StaysClosed()
    {
        using var keys = new KeyPoller();
        var devices = new FakeAudioDevices();
        for (int i = 0; i < 50; i++)
        {
            var engine = new AudioEngine(keys, useDevices: false, devices: devices);
            engine.LevelMeter = true;
            engine.Dispose();
        }
        await Task.Delay(500); // the queued opens have run by now
        Assert.Equal(0, devices.Counts.MicsOpen);
    }

    /// <summary>
    /// Package 124: a slider moved on the settings page before the level meter's microphone is open (it opens in the
    /// background) still counts once it is; new saved settings replace the live ones.
    /// </summary>
    [Fact]
    public async Task AudioEngine_LiveSettingsBeforeMicOpen_Applied()
    {
        using var keys = new KeyPoller();
        using var engine = new AudioEngine(keys, useDevices: false, devices: new FakeAudioDevices());
        engine.ApplyLive(new ClientSettings { InputGain = 0.5f, VadThresholdDb = -25, NoiseSuppression = false, AutoGain = true });
        engine.LevelMeter = true;
        var watch = System.Diagnostics.Stopwatch.StartNew();
        while (engine.Capture is null && watch.ElapsedMilliseconds < 5000) await Task.Delay(10);
        var capture = engine.Capture!;
        Assert.Equal((0.5f, -25f, false, true), (capture.Gain, capture.Vad.ThresholdDb, capture.NoiseSuppression, capture.AutoGain));

        engine.Configure(new ClientSettings { InputGain = 0.8f });
        Assert.Equal(0.8f, engine.Capture!.Gain);
    }

    /// <summary>
    /// Package 124 (A132): after the start nothing is open and no playback thread runs; the settings page opens the
    /// microphone while it shows, the self test and a connection open both devices, and they close again. A device
    /// changed in the settings while connected is switched at once.
    /// </summary>
    [Fact]
    public async Task AudioDevices_OpenOnlyWhenNeeded()
    {
        var devices = new FakeAudioDevices();
        var own = Directory.CreateTempSubdirectory("ovs-devices-").FullName;
        var main = await ui.InvokeAsync(() => Task.FromResult(new MainViewModel(own, ui.Post, useAudioDevices: false, keyState: keys.IsDown, audioDevices: devices)));
        main.ConfirmTofu = _ => Task.FromResult(true);
        Task Do(Func<Task> work) => ui.InvokeAsync<object?>(async () =>
        {
            await work();
            return null;
        });
        // The counts once they match (at most 10 s): the devices open and close off the UI thread.
        async Task<(int, int, int, int, bool)> Settled((int, int, int, int, bool) expected)
        {
            var watch = System.Diagnostics.Stopwatch.StartNew();
            while (true)
            {
                var (mics, micsOpen, speakers, speakersOpen) = devices.Counts;
                var now = (mics, micsOpen, speakers, speakersOpen, main.Audio.PlaybackRunning);
                if (now == expected || watch.ElapsedMilliseconds > 10_000) return now;
                await Task.Delay(20);
            }
        }
        try
        {
            Assert.Equal((0, 0, 0, 0, false), await Settled((0, 0, 0, 0, false)));

            await Do(() =>
            {
                main.OpenSettings();
                return Task.CompletedTask;
            });
            Assert.Equal((1, 1, 0, 0, false), await Settled((1, 1, 0, 0, false))); // the level meter
            await Do(() =>
            {
                main.ClosePage();
                return Task.CompletedTask;
            });
            Assert.Equal((1, 0, 0, 0, false), await Settled((1, 0, 0, 0, false)));

            await Do(() => main.SetSelfTestAsync(true));
            Assert.Equal((2, 1, 1, 1, true), await Settled((2, 1, 1, 1, true)));
            await Do(() => main.SetSelfTestAsync(false));
            Assert.Equal((2, 0, 1, 0, false), await Settled((2, 0, 1, 0, false)));

            await Do(() => main.ConnectAsync(new ConnectChoice("127.0.0.1", server.Port, "anna", null, false)));
            Assert.True(await OnUi(() => main.IsConnected));
            Assert.Equal((3, 1, 2, 1, true), await Settled((3, 1, 2, 1, true)));
            await Do(() => main.ApplySettingsAsync(new ClientSettings { InputDeviceId = "mic-2" }));
            Assert.Equal((4, 1, 3, 1, true), await Settled((4, 1, 3, 1, true)));
            Assert.Equal("mic-2", devices.LastMicId);

            await Do(main.DisconnectAsync);
            Assert.Equal((4, 0, 3, 0, false), await Settled((4, 0, 3, 0, false)));
        }
        finally
        {
            await Do(() => main.DisposeAsync().AsTask());
            Directory.Delete(own, true);
        }
    }

    /// <summary>
    /// Package 124: every server message is logged off the UI thread (one queue, written in batches), in order, and
    /// what is queued at the end is written before the client is gone.
    /// </summary>
    [Fact]
    public async Task MessageLog_OffUiThread_InOrder()
    {
        var own = Directory.CreateTempSubdirectory("ovs-log-").FullName;
        var main = await ui.InvokeAsync(() => Task.FromResult(new MainViewModel(own, ui.Post, useAudioDevices: false, keyState: keys.IsDown)));
        main.ConfirmTofu = _ => Task.FromResult(true);
        try
        {
            int uiThread = await OnUi(() => Environment.CurrentManagedThreadId);
            var writers = new ConcurrentQueue<int>();
            main.Log.Writing += () => writers.Enqueue(Environment.CurrentManagedThreadId);
            await ui.InvokeAsync<object?>(async () =>
            {
                await main.ConnectAsync(new ConnectChoice("127.0.0.1", server.Port, "anna", null, false));
                return null;
            });
            for (int i = 0; i < 5; i++)
            {
                var guest = await TestClient.ConnectAsync(server, $"gast{i}");
                await OnUiUntil(() => main.Server!.Mirror.Users.Count, n => n == 2);
                await guest.DisposeAsync();
                await OnUiUntil(() => main.Server!.Mirror.Users.Count, n => n == 1);
            }
            var whileRunning = writers.ToArray();
            await ui.InvokeAsync<object?>(async () =>
            {
                await main.DisposeAsync();
                return null;
            });

            Assert.NotEmpty(whileRunning);
            Assert.DoesNotContain(uiThread, whileRunning);
            var log = string.Join(Environment.NewLine, Directory.GetFiles(Path.Combine(own, "logs"), "client-*.log").Select(File.ReadAllText));
            var order = new List<string>();
            for (int i = 0; i < 5; i++) order.AddRange([$"gast{i} verbunden", $"gast{i} getrennt"]);
            order.Add("Verbindung getrennt (eigene Aktion)"); // written while closing
            var positions = order.Select(line => log.IndexOf(line, StringComparison.Ordinal)).ToList();
            Assert.DoesNotContain(-1, positions);
            Assert.Equal(positions.Order(), positions);
        }
        finally
        {
            Directory.Delete(own, true);
        }
    }
}
