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

    string ClientLogText() => string.Join(Environment.NewLine, Directory.GetFiles(Path.Combine(dir, "logs"), "client-*.log").Select(path =>
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        return new StreamReader(stream).ReadToEnd();
    }));

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
        async Task<int> AfterRefusal()
        {
            var id = LastId();
            await OnUi(() =>
            {
                server.Apply(new Error(id, Codes.RateLimited));
                return 0;
            });
            var before = Requests();
            time.Advance(TimeSpan.FromSeconds(11));
            for (int i = 0; i < 20 && Requests() == before; i++)
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
        Assert.Equal(2, await AfterRefusal());
        Assert.Equal(2, await AfterRefusal()); // only once
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

    /// <summary>Package 33: an incoming whisper opens a tab in the background, the reply goes back only to bert.</summary>
    [Fact]
    public async Task Private_EndToEnd()
    {
        await ConnectAsync(saveBookmark: false);
        await using var bert = await TestClient.ConnectAsync(server, "bert");
        await using var carla = await TestClient.ConnectAsync(server, "carla");
        var annaId = await OnUi(() => vm.Server!.Mirror.SelfId);

        await bert.SendAsync(new SendChat(ChatTarget.Private, annaId, "psst"));
        ChatTab? tab = null;
        for (int i = 0; i < 60 && tab is null; i++)
        {
            await Task.Delay(50);
            tab = await OnUi(() => vm.Chat!.Tabs.FirstOrDefault(t => t.IsPrivate));
        }
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
        Assert.True(watch.ElapsedMilliseconds < 1000, $"{watch.ElapsedMilliseconds} ms"); // not blocked by the gate
        Assert.Equal(Page.Settings, page);
        Assert.Equal(["Standardgerät", "Geräte werden geladen ..."], inputs);
        Assert.Null(hint);

        gate.Set();
        for (int i = 0; i < 100 && await OnUi(() => main.SettingsPage!.Inputs.Count) != 3; i++) await Task.Delay(20);
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
            for (int i = 0; i < 100 && await OnUi(() => vm.Server!.Self!.IsDeafened) != deafened; i++) await Task.Delay(20);
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
