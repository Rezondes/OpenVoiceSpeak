using System.Collections.Concurrent;
using OVS.Client.Audio;
using OVS.Client.Audio.Codec;
using OVS.Client.Audio.Devices;
using OVS.Client.Audio.Sounds;
using OVS.Client.Audio.Transmit;
using OVS.Client.Collections;
using OVS.Client.Dialogs;
using OVS.Client.Exceptions;
using OVS.Client.Input;
using OVS.Client.Localization;
using OVS.Client.Models;
using OVS.Client.Net;
using OVS.Client.Services;
using OVS.Client.Settings;
using OVS.Client.ViewModels;
using OVS.Shared.Permissions;
using OVS.Shared.Protocol;
using OVS.Shared.Protocol.Messages;
using OVS.Shared.Protocol.Messages.Channels;
using OVS.Shared.Protocol.Messages.Chat;
using OVS.Shared.Protocol.Messages.Connection;
using OVS.Shared.Protocol.Messages.ServerInfo;
using OVS.Shared.Protocol.Messages.Users;
using OVS.Shared.Voice;
using OVS.Tests.TestSupport;

namespace OVS.Tests.Client.ViewModels;

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
        vm = await ui.InvokeAsync(() => Task.FromResult(MainViewModelSetup.Create(dir, ui.Post, keyState: keys.IsDown, trustServers: true)));
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

    Task<T> OnUi<T>(Func<T> read) => ui.ReadAsync(read);

    /// <summary>
    /// Replaces the view model by one on <paramref name="time"/> (same folder, keys and trust; the dialogs set so far carry over).
    /// </summary>
    async Task RunOnAsync(TimeProvider time)
    {
        var dialogs = vm.Dialogs;
        await ui.InvokeAsync<object?>(async () =>
        {
            await vm.DisposeAsync();
            return null;
        });
        vm = await ui.InvokeAsync(() => Task.FromResult(MainViewModelSetup.Create(dir, ui.Post, keyState: keys.IsDown, dialogs: dialogs, trustServers: true, time: time)));
    }

    Task<T> OnUiUntil<T>(Func<T> read, Func<T, bool> done) => ui.ReadUntilAsync(read, done);

    /// <summary>Presses "Abbrechen" of a running reconnect series.</summary>
    Task CancelReconnectAsync() => OnUi(() =>
    {
        vm.Connection.CancelReconnectCommand.Execute(null);
        return 0;
    });

    Task ConnectAsync(bool saveBookmark) =>
        ui.InvokeAsync<object?>(async () =>
        {
            await vm.Connection.ConnectAsync(new ConnectChoice("127.0.0.1", server.Port, "anna", null, saveBookmark));
            return null;
        });

    /// <summary>Runs the view model on <paramref name="time"/> with its voice sent through <paramref name="relay"/>, then connects.</summary>
    async Task ConnectThroughRelayAsync(LossyUdpRelay relay, TimeProvider time)
    {
        await RunOnAsync(time);
        await OnUi(() => vm.Connection.VoiceRelay = relay.EndPoint);
        await ConnectAsync(saveBookmark: false);
    }

    /// <summary>The profile's file names never change: the first connection writes the key pair and the trusted fingerprint.</summary>
    [Fact]
    public async Task Connect_NewProfile_WritesIdentityKeyAndKnownServersFile()
    {
        await ConnectAsync(saveBookmark: false);

        Assert.True(await OnUi(() => vm.IsConnected));
        Assert.True(File.Exists(Path.Combine(dir, "identity.key")));
        Assert.True(File.Exists(Path.Combine(dir, "known_servers.json")));
    }

    [Fact]
    public async Task ConnectAsync_ServerWelcomes_EngineIsConnectedWhenTheServerIsShown()
    {
        var connectedWhenShown = new List<bool>();
        await ui.InvokeAsync<object?>(async () =>
        {
            vm.PropertyChanged += (_, args) =>
            {
                if (args.PropertyName == nameof(MainViewModel.Server)) connectedWhenShown.Add(EngineProbe.ReadEngineSwitches(vm.Audio).Connected);
            };
            await vm.Connection.ConnectAsync(new ConnectChoice("127.0.0.1", server.Port, "anna", null, SaveBookmark: false));
            return null;
        });

        Assert.Equal([true], connectedWhenShown);
    }

    [Fact]
    public async Task DisconnectAsync_Connected_PlaysTheDisconnectedTone()
    {
        await ConnectAsync(saveBookmark: false);

        await OnUi(() => vm.Connection.DisconnectAsync());

        Assert.Contains(SoundEvent.Disconnected, await OnUi(() => vm.Audio.Sounds.Recent));
    }

    [Fact]
    public async Task ConnectAsync_AnotherUserTalks_EngineHearsTheirFrame()
    {
        await ConnectAsync(saveBookmark: false);
        await OnUiUntil(() => vm.Connection.VoiceLink.Reachable, reachable => reachable == true);
        await using var bert = await TestClient.ConnectAsync(server, "bert");
        using var bertVoice = new TestVoice(bert, server.VoiceEndPoint);
        await bertVoice.HelloAsync();

        await bertVoice.SendAsync(PacketType.Voice, EngineProbe.SilentFrame());

        Assert.Contains(bert.Id, await OnUiUntil(() => vm.Audio.FramesReceived.Keys.ToList(), speakers => speakers.Contains(bert.Id)));
    }

    [Fact]
    public async Task DisconnectAsync_Connected_ClearsTheEngineSendAndTheVoiceClient()
    {
        await ConnectAsync(saveBookmark: false);

        await OnUi(() => vm.Connection.DisconnectAsync());

        Assert.Null(await OnUi(() => vm.Audio.Send));
        Assert.Null(await OnUi(() => vm.Connection.VoiceLink.Client));
    }

    /// <summary>Package 34: in a muted channel the client warns and does not send.</summary>
    [Fact]
    public async Task ChannelMuted_HintAndNoSending()
    {
        var (hint, muted) = await OnUi(() =>
        {
            MainViewModelSetup.ShowServer(vm, FakeServers.Admin());
            vm.Server!.Apply(new ChannelUpdated(new ChannelInfo(FakeServers.Lobby, "Lobby", "Start", 0, IsMuted: true)));
            vm.Tick();
            return (vm.VoiceHint, vm.Audio.ChannelMuted);
        });
        Assert.Equal("Stummer Channel: niemand hört dich.", hint);
        Assert.True(muted);
        await ui.InvokeAsync<object?>(async () =>
        {
            await vm.Connection.DisconnectAsync();
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
            MainViewModelSetup.ShowServer(vm, FakeServers.Admin());
            seen.Add(vm.Audio.ChannelBitrate); // the Lobby at its default
            vm.Server!.Apply(new ChannelUpdated(new ChannelInfo(FakeServers.Raid, "Raid", "", 1, Bitrate: 96)));
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
            await vm.Connection.DisconnectAsync();
            return null;
        });
        Assert.Equal(64, await OnUi(() => vm.Audio.ChannelBitrate));
    }

    /// <summary>
    /// Package 135 (AC3, A144): a bitrate lowered by loss stays over state changes that are no move, and a move starts at
    /// the new channel's bitrate, also when it is the same as the old one's.
    /// </summary>
    [Fact]
    public async Task Bitrate_Adapted_ResetOnMove()
    {
        var steps = await OnUi(() =>
        {
            var seen = new List<int>();
            var time = new ManualTimeProvider();
            vm.Audio.Loss = new LossAdapter(time, 64);
            MainViewModelSetup.ShowServer(vm, FakeServers.Admin()); // in the Lobby at 64
            EngineProbe.LowerByLoss(vm.Audio.Loss, time); // 6 s above 10 %
            seen.Add(vm.Audio.EncoderBitrate);
            vm.Server!.Apply(new ChannelUpdated(new ChannelInfo(FakeServers.Raid, "Raid", "", 1)));
            seen.Add(vm.Audio.EncoderBitrate); // another channel edited: no move
            vm.Server.Apply(new UserUpdated(new UserInfo(1, "fp1", "ich", FakeServers.Raid, false, false, false,
                OVS.Shared.Permissions.Permission.All, [OVS.Shared.Permissions.WellKnownGroups.Admin])));
            seen.Add(vm.Audio.EncoderBitrate); // moved into a channel at 64 too
            return seen;
        });
        Assert.Equal([32, 32, 64], steps);
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
        await RunOnAsync(time);
        await OnUi(() =>
        {
            MainViewModelSetup.ShowServer(vm, FakeServers.Admin());
            return vm.Server!.SetSelfStateAsync(new SelfState(Muted: true, Deafened: false));
        });
        Assert.Equal(SelfMutedHint, await HoldKey(KeyAction.PushToTalk, true));
        await HoldKey(KeyAction.PushToTalk, false);
        time.Advance(TimeSpan.FromMilliseconds(2100)); // the hold's edges are in VoiceHintsTests; this pins the clock
        Assert.Equal("", await TickHint());
    }

    [Theory]
    [InlineData("deafened", "Du bist taub geschaltet: niemand hört dich.")]
    [InlineData("server", "Du bist vom Server stummgeschaltet: niemand hört dich.")]
    public async Task TalkWhileDeafenedOrServerMuted_ShowsHint(string state, string hint)
    {
        await OnUi(() =>
        {
            MainViewModelSetup.ShowServer(vm, FakeServers.Admin());
            if (state == "deafened") return vm.Server!.SetSelfStateAsync(new SelfState(Muted: true, Deafened: true));
            vm.Server!.Apply(new UserUpdated(vm.Server.Mirror.Self! with { ServerMuted = true }));
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
            MainViewModelSetup.ShowServer(vm, FakeServers.Admin());
            return vm.Server!.SetSelfStateAsync(new SelfState(Muted: true, Deafened: false));
        });
        vm.Audio.ChooseTarget(true); // the voice gate hears voice, the capture thread asks
        Assert.Equal("", await TickHint()); // push-to-talk mode: voice alone is no attempt to talk
        await OnUi(() =>
        {
            vm.ApplySettings(new ClientSettings { Mode = TransmitMode.VoiceActivation });
            return vm.Audio.ChooseTarget(false);
        });
        Assert.Equal("", await TickHint());
        vm.Audio.ChooseTarget(true);
        Assert.Equal(SelfMutedHint, await TickHint());
    }

    [Fact]
    public async Task NotMuted_NoHint()
    {
        await OnUi(() => MainViewModelSetup.ShowServer(vm, FakeServers.Admin()));
        Assert.Equal("", await HoldKey(KeyAction.PushToTalk, true));
        await HoldKey(KeyAction.PushToTalk, false);

        // Package 125: push-to-mute is muting on purpose, so no hint
        vm.Keys.Simulate(KeyAction.PushToMute, true);
        Assert.True(await OnUiUntil(() => vm.Server!.SelfMuted, m => m));
        Assert.Equal("", await HoldKey(KeyAction.PushToTalk, true));
        await HoldKey(KeyAction.PushToTalk, false);
        await HoldKey(KeyAction.PushToMute, false);
    }

    /// <summary>A self-muted user in a muted channel gets the muted-channel hint: the channel's state is not cut by the own mute (the priority is in <c>VoiceHintsTests</c>).</summary>
    [Fact]
    public async Task Tick_ChannelMutedAndSelfMuted_ShowsTheMutedChannelHint()
    {
        await OnUi(() =>
        {
            MainViewModelSetup.ShowServer(vm, FakeServers.Admin());
            vm.Server!.Apply(new ChannelUpdated(new ChannelInfo(FakeServers.Lobby, "Lobby", "Start", 0, IsMuted: true)));
            return vm.Server.SetSelfStateAsync(new SelfState(Muted: true, Deafened: false));
        });

        Assert.Equal(Strings.VoiceHint_MutedChannel, await HoldKey(KeyAction.PushToTalk, true));
        await HoldKey(KeyAction.PushToTalk, false);
    }

    /// <summary>Link push-to-talk without the right to speak into links says so (the rules are in <c>VoiceHintsTests</c>).</summary>
    [Fact]
    public async Task Tick_LinkKeyHeldWithoutLinkRight_ShowsTheNoLinkRightHint()
    {
        await ShowServerWithoutLinkRight();

        Assert.Equal(Strings.VoiceHint_NoLinkRight, await HoldKey(KeyAction.LinkPushToTalk, true));
        await HoldKey(KeyAction.LinkPushToTalk, false);
    }

    [Fact]
    public async Task Tick_LinkKeyUpWithoutLinkRight_ShowsNothing()
    {
        await ShowServerWithoutLinkRight();

        Assert.Equal("", await TickHint());
    }

    /// <summary>Connected but in no channel (the own user's channel is not in the list), self-muted, talk key held: only the muted-microphone hint fits.</summary>
    [Fact]
    public async Task Tick_InNoChannelSelfMutedAndTalkKeyHeld_ShowsTheSelfMutedHint()
    {
        await OnUi(() =>
        {
            var shown = MainViewModelSetup.ShowServer(vm, FakeServers.Admin());
            shown!.Apply(new UserUpdated(shown.Mirror.Self! with { ChannelId = Guid.Empty }));
            return shown.SetSelfStateAsync(new SelfState(Muted: true, Deafened: false));
        });

        Assert.Equal(SelfMutedHint, await HoldKey(KeyAction.PushToTalk, true));
        await HoldKey(KeyAction.PushToTalk, false);
    }

    Task<ServerViewModel?> ShowServerWithoutLinkRight() => OnUi(() =>
    {
        var shown = MainViewModelSetup.ShowServer(vm, FakeServers.Admin());
        FakeServers.SetOwnRightsAndGroups(shown!, Permission.Speak, [WellKnownGroups.Guest]);
        return shown;
    });

    string ClientLogText() => MainViewModelSetup.ReadLogText(vm, dir);

    /// <summary>Package 39: a wrong password is never stored, the right one only when asked for, and never logged.</summary>
    [Fact]
    public async Task Connect_SavePassword_OnlyAfterSuccess()
    {
        await using var locked = await TestServer.StartAsync(password: "richtig-geheim");
        Task Connect(string password, bool save) => ui.InvokeAsync<object?>(async () =>
        {
            await vm.Connection.ConnectAsync(new ConnectChoice("127.0.0.1", locked.Port, "anna", password, SaveBookmark: true, SavePassword: save));
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
        Assert.Contains("password stored encrypted", log);
    }

    // ---- Package 40: bookmarks in the sidebar ----

    async Task<TestServer> LockedServerAsync() => await TestServer.StartAsync(password: "richtig");

    /// <summary>Like a click in the sidebar: the bookmark is one of the saved ones.</summary>
    Task ConnectBookmark(Bookmark bookmark) => ui.InvokeAsync<object?>(async () =>
    {
        vm.Settings.Bookmarks.Add(bookmark);
        await vm.Connection.ConnectBookmarkAsync(bookmark);
        return null;
    });

    [Fact]
    public async Task Bookmark_Connect_UsesSavedPassword()
    {
        await using var locked = await LockedServerAsync();
        int asked = 0;
        vm.Dialogs = new DialogCallbacks { AskPassword = _ => { asked++; return Task.FromResult<PasswordAnswer?>(null); } };
        await ConnectBookmark(new Bookmark("Gilde", "127.0.0.1", locked.Port, "anna", PasswordProtector.Protect("richtig")));
        Assert.True(await OnUi(() => vm.IsConnected));
        Assert.Equal(0, asked);
    }

    [Fact]
    public async Task Bookmark_WrongSavedPassword_AsksAndSavesNew()
    {
        await using var locked = await LockedServerAsync();
        var answers = new Queue<PasswordAnswer?>([new PasswordAnswer("auch falsch", true), new PasswordAnswer("richtig", true)]);
        vm.Dialogs = new DialogCallbacks { AskPassword = _ => Task.FromResult(answers.Dequeue()) };
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
        vm.Dialogs = new DialogCallbacks { AskPassword = _ => Task.FromResult<PasswordAnswer?>(null) };
        await ConnectBookmark(new Bookmark("Gilde", "127.0.0.1", locked.Port, "anna"));
        Assert.False(await OnUi(() => vm.IsConnected));
    }

    [Fact]
    public async Task Bookmark_Edited_PersistsToTheProfileFolder()
    {
        var original = new Bookmark("Alt", "alt.example.org", 7000, "anna");
        await OnUi(() =>
        {
            vm.Settings.Bookmarks.Add(original);
            vm.Settings.Save(dir);
            return 0;
        });
        vm.Dialogs = new DialogCallbacks
        {
            BookmarkList = new() { EditBookmark = _ => Task.FromResult<BookmarkEdit?>(new BookmarkEdit("Neu", "neu.example.org", 7100, "berta", "pw", SavePassword: true)) },
        };
        await ui.InvokeAsync<object?>(async () =>
        {
            await vm.Bookmarks.EditAsync(original);
            return null;
        });
        Assert.Equal("Neu", ClientSettings.Load(dir, out _).Bookmarks.Single().Name); // wiring only: the list, dialogs and save reach the profile folder; the fields are in BookmarkListTests
    }

    /// <summary>
    /// The window sets the dialogs after the view model is built: the bookmarks and the page flow ask through the set of
    /// the moment (the connection's password question and server dialogs: Bookmark_WrongSavedPassword_AsksAndSavesNew and
    /// ConnectionViewModelTests).
    /// </summary>
    [Fact]
    public async Task Dialogs_SetAfterConstruction_BookmarksAndPageFlowAskThroughTheNewSet()
    {
        var asked = new List<string>();
        Func<KeyBinding?, Func<KeyAction, Task<KeyChord?>>, Task<KeyBinding?>> editor = (_, _) => Task.FromResult<KeyBinding?>(null);
        vm.Dialogs = new DialogCallbacks
        {
            BookmarkList = new() { Confirm = question => { asked.Add(question); return Task.FromResult(false); } },
            PageFlow = new() { EditKeyBinding = editor },
        };
        var bookmark = new Bookmark("Alt", "alt.example.org", 7000, "anna");

        var editorOfPage = await ui.InvokeAsync(async () =>
        {
            vm.Settings.Bookmarks.Add(bookmark);
            await vm.Bookmarks.DeleteAsync(bookmark);
            vm.PageFlow.OpenSettings();
            return vm.PageFlow.SettingsPage!.EditKeyBinding;
        });

        Assert.Single(asked);
        Assert.Same(editor, editorOfPage);
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
            vm.Update.Use(new OVS.Client.Net.Updates.UpdateChecker(new HttpClient(http), running),
                new OVS.Client.Net.Updates.UpdateInstaller(new HttpClient(http), Path.Combine(dir, "gibt-es-nicht.exe"), _ => offered.Add("neu gestartet")));
            vm.Dialogs = new DialogCallbacks { OfferUpdate = offer => { offered.Add(offer.Version); return Task.FromResult(false); } };
            return 0;
        });
        await ui.InvokeAsync<object?>(async () =>
        {
            await vm.Update.CheckAtStartAsync();
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
            await vm.Update.CheckAtStartAsync();
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
        var seen = new List<OVS.Client.Net.Updates.UpdateProgress?>();
        await OnUi(() =>
        {
            File.WriteAllText(Path.Combine(dir, "OVS.Client.exe"), "alt");
            vm.Update.Use(new OVS.Client.Net.Updates.UpdateChecker(new HttpClient(http), running),
                new OVS.Client.Net.Updates.UpdateInstaller(new HttpClient(http), Path.Combine(dir, "OVS.Client.exe"), _ => { }));
            vm.Dialogs = new DialogCallbacks { OfferUpdate = _ => Task.FromResult(true) };
            vm.Update.PropertyChanged += (_, args) =>
            {
                if (args.PropertyName == nameof(UpdateFlow.Progress)) seen.Add(vm.Update.Progress);
            };
            return 0;
        });
        var result = await ui.InvokeAsync(() => vm.Update.CheckAsync());

        Assert.Equal(OVS.Client.Localization.Strings.Update_Damaged, result);
        Assert.Contains(seen, p => p is { Phase: OVS.Client.Net.Updates.UpdatePhase.Downloading, Bytes: 200_000, Total: 200_000 });
        Assert.All(seen.OfType<OVS.Client.Net.Updates.UpdateProgress>(), p => Assert.Equal("280926.0b2c", p.Version));
        Assert.Null(seen[^1]);
        Assert.False(await OnUi(() => vm.Update.IsUpdating));
        Assert.Equal(OVS.Client.Localization.Strings.Update_Damaged, await OnUi(() => vm.Status));
        var notice = Assert.Single(await OnUi(() => vm.Notices.ToList())); // the download status is no notice
        Assert.Equal(NoticeKind.Warning, notice.Kind);
        Assert.Equal(OVS.Client.Localization.Strings.Update_Damaged, notice.Text);
    }

    /// <summary>Package 43: the settings page's "Nach Updates suchen" asks the update flow; a build that asks nobody says so.</summary>
    [Fact]
    public async Task SettingsPage_CheckNowWithoutLookUp_SaysLocalBuild()
    {
        await OnUi(() =>
        {
            vm.PageFlow.OpenSettings();
            return 0;
        });

        var answer = await ui.InvokeAsync(() => vm.PageFlow.SettingsPage!.CheckNow!());

        Assert.Equal(OVS.Client.Localization.Strings.Update_LocalBuild, answer);
    }

    [Fact]
    public async Task OpenSettings_SoundWithMissingFilePreviewed_EngineFallsBackAndLogsTheMissingFile()
    {
        const string missingFile = "preview-missing.wav";
        string missingLine = $"Sound-Datei {missingFile} für {SoundEvent.UserJoined} fehlt";
        await OnUi(() =>
        {
            vm.ApplySettings(new ClientSettings { Sounds = { [SoundEvent.UserJoined] = new SoundSetting(File: missingFile) } });
            vm.PageFlow.OpenSettings();
            return 0;
        });
        Assert.DoesNotContain(missingLine, ClientLogText());

        await OnUi(() =>
        {
            vm.PageFlow.SettingsPage!.SoundRows.Single(row => row.Event == SoundEvent.UserJoined).PlayCommand.Execute(null);
            return 0;
        });

        Assert.Contains(missingLine, ClientLogText()); // the engine's sound library looked the row's own file up
    }

    [Fact]
    public async Task OpenSettings_SoundImported_LandsInTheProfileFolder()
    {
        string source = Path.Combine(dir, "own-tone.wav");
        using (var writer = new NAudio.Wave.WaveFileWriter(source, NAudio.Wave.WaveFormat.CreateIeeeFloatWaveFormat(48000, 1)))
            writer.WriteSamples(Enumerable.Repeat(0.2f, 4800).ToArray(), 0, 4800);

        string? imported = await OnUi(() =>
        {
            vm.PageFlow.OpenSettings();
            var row = vm.PageFlow.SettingsPage!.SoundRows.Single(candidate => candidate.Event == SoundEvent.UserJoined);
            row.Import(source);
            return row.File;
        });

        Assert.NotNull(imported);
        Assert.True(File.Exists(Path.Combine(dir, SoundLibrary.Folder, imported)));
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
            s.KeyBindings = [new KeyBinding(KeyAction.PushToTalk, new KeyChord(VirtualKeys.XButton1))];
            vm.ApplySettings(s);
            return 0;
        });
        Assert.False(await OnUi(() => vm.HasNoPttBinding));
        Assert.Equal("PTT: Maustaste 4", await OnUi(() => vm.TalkHint));

        await OnUi(() => // Package 41: several PTT keys are all listed
        {
            var s = vm.Settings;
            s.KeyBindings = [new KeyBinding(KeyAction.PushToTalk, new KeyChord(VirtualKeys.XButton1)), new KeyBinding(KeyAction.PushToTalk, new KeyChord(VirtualKeys.XButton2))];
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
            await vm.Connection.ConnectAsync(new ConnectChoice("127.0.0.1", logoServer.Port, "anna", null, SaveBookmark: true));
            return null;
        });

        await Connect();
        var shown = await OnUiUntil(() => vm.Server?.IconPng, s => s is not null);
        Assert.Equal(png, shown);

        await Connect(); // second time straight from the cache
        Assert.Equal(png, await OnUi(() => vm.Server!.IconPng));
        Assert.Equal(png, (await OnUi(() => vm.Bookmarks.Items)).Single().Icon);

        var log = ClientLogText();
        Assert.Equal(1, log.Split(Environment.NewLine).Count(l => l.Contains("Request server logo")));
    }

    [Fact]
    public async Task Connect_ShowsWelcomeText_AndPing()
    {
        await ConnectAsync(saveBookmark: false);
        Assert.Contains(await OnUi(() => vm.Notices.ToList()), n => n.Kind == NoticeKind.Welcome && n.Text == "Willkommen auf dem Testserver!");

        var ping = await OnUiUntil(() =>
        {
            vm.Tick();
            return vm.Connection.PingText;
        }, p => p.Length > 0);
        Assert.StartsWith("Ping ", ping);
    }

    /// <summary>Package 126: an outage after UDP worked is logged once when lost and once when back.</summary>
    [Fact]
    public async Task UdpOutage_LoggedOnceEachWay()
    {
        using var relay = new LossyUdpRelay(server.VoiceEndPoint);
        var time = new ManualTimeProvider();
        await ConnectThroughRelayAsync(relay, time);
        string Logged()
        {
            vm.Tick();
            return ClientLogText();
        }
        Assert.Contains("UDP now reachable", await OnUiUntil(Logged, l => l.Contains("UDP now reachable")));

        relay.DropAll = true;
        for (int i = 0; i < 40; i++) time.Advance(TimeSpan.FromMilliseconds(500)); // 20 s
        Assert.Contains("UDP not reachable", await OnUi(Logged));
        relay.DropAll = false;
        for (int i = 0; i < 10; i++) time.Advance(TimeSpan.FromMilliseconds(500));
        await OnUiUntil(Logged, l => l.Contains("UDP reachable again"));

        var log = await OnUi(Logged); // one more tick adds nothing
        foreach (var line in new[] { "UDP now reachable", "UDP not reachable", "UDP reachable again" })
            Assert.Single(log.Split(Environment.NewLine), l => l.Contains(line));
    }

    /// <summary>
    /// Package 133 (AC5): the signal icon rates the voice round trip, crossed out while UDP is unreachable; a click shows
    /// the connection details, refreshed every 2 s.
    /// </summary>
    [Fact]
    public async Task SignalIcon_AndDetails()
    {
        // Package 135: the server on its own clock, moved only while sending, so its uplink window never rolls here and
        // every answer carries the frames sent (a slow machine cannot turn them into an empty or partial window)
        var serverTime = new ManualTimeProvider();
        await server.DisposeAsync();
        server = await TestServer.StartAsync(d => d.Settings.WelcomeText = "Willkommen auf dem Testserver!", time: serverTime);
        using var relay = new LossyUdpRelay(server.VoiceEndPoint);
        var time = new ManualTimeProvider(); // the voice client's and the refresh's clock
        ConnectionStatsViewModel? shown = null;
        vm.Dialogs = new DialogCallbacks
        {
            ConnectionDetails = card =>
            {
                shown = card;
                return Task.CompletedTask;
            },
        };
        await ConnectThroughRelayAsync(relay, time);
        await OnUi(() => vm.Audio.Loss = new LossAdapter(time, 64)); // Package 135: on the same clock
        Assert.Equal((0, false), await OnUi(() => (vm.Stats.Bars, vm.Stats.Unreachable)));

        // the answer to the Hello: 0 ms on the manual clock, as good as the baseline
        Assert.Equal(4, await OnUiUntil(() =>
        {
            time.Advance(TimeSpan.FromSeconds(2));
            vm.Tick();
            return vm.Stats.Bars;
        }, b => b == 4));
        var stats = await OnUi(() => vm.Stats);
        Assert.Equal((SignalRating.Good, false), (stats.Rating, stats.Unreachable));
        Assert.Contains("0 ms", stats.RoundTripText);
        Assert.Equal("64 kbps", stats.BitrateText);
        foreach (var text in new[] { stats.UplinkText, stats.DownlinkText, stats.JitterText, stats.DelayText, stats.ConcealedText })
            Assert.False(string.IsNullOrEmpty(text));

        await OnUi(() => vm.ShowConnectionDetailsCommand.ExecuteAsync(null));
        Assert.Same(stats, shown);

        // refreshed after 2 s on the view model's clock (the interval's edges are in ConnectionStatsViewModelTests)
        Assert.Equal("96 kbps", await OnUi(() =>
        {
            vm.Audio.ChannelBitrate = 96;
            time.Advance(ConnectionStatsViewModel.RefreshInterval);
            vm.Tick();
            return stats.BitrateText;
        }));

        // Package 135 (A144): every fifth voice frame lost on the way up: the server's figure in the ping answers raises
        // the loss hint and, after 5 s of them (on the client's clock), halves the bitrate, which the card shows
        relay.DropUpVoicePercent = 20;
        var frame = new VoiceEncoder().Encode(new float[AudioFormat.FrameSamples]);
        for (int i = 0; i < 40; i++) // 0.8 s on the server's clock: within one window and its 1 s fresh start
        {
            serverTime.Advance(AudioFormat.FrameDuration); // its voice rate limit (60/s, burst 10) refills
            await OnUi(() =>
            {
                vm.Audio.Send!(frame, OVS.Shared.Voice.VoiceHeader.TargetChannel);
                return 0;
            });
            await Task.Delay(5); // lets the server take each frame before the next advance, so they do not drain the bucket at once
        }
        for (int step = 0; step < 4; step++) // a figure every 2 s
        {
            serverTime.Advance(TimeSpan.FromMilliseconds(200)); // tokens for the ping; still far from the 5 s window
            int answers = await OnUi(() =>
            {
                time.Advance(TimeSpan.FromSeconds(2)); // the ping goes out
                return vm.Connection.VoiceLink.Client!.RoundTripSample.Count;
            });
            await OnUiUntil(() => vm.Connection.VoiceLink.Client!.RoundTripSample.Count, c => c > answers);
            await OnUi(() =>
            {
                vm.Tick();
                return 0;
            });
        }
        // the halving lands in the last loop tick only (StepDownAfter 5 s, 2 s steps), so this pin needs the feed before the card's refresh in Tick
        var (hint, adapted, shownBitrate) = await OnUi(() => (vm.Audio.Loss.LossHint, vm.Audio.EncoderBitrate, stats.BitrateText));
        Assert.True(hint > 10, $"loss hint {hint}");
        Assert.Equal(48, adapted);
        Assert.Equal(96, await OnUi(() => vm.Audio.ChannelBitrate));
        Assert.Equal("48 kbps", shownBitrate);
        relay.DropUpVoicePercent = 0;

        // 16 s without an answer: crossed out, and the last round trip does not enter the baseline again on every refresh
        relay.DropAll = true;
        int samples = await OnUi(() => stats.Samples);
        var (unreachable, bars, after) = await OnUi(() =>
        {
            for (int i = 0; i < 32; i++)
            {
                time.Advance(TimeSpan.FromMilliseconds(500));
                vm.Tick();
            }
            return (stats.Unreachable, stats.Bars, stats.Samples);
        });
        Assert.Equal((true, 0), (unreachable, bars));
        Assert.InRange(after, samples, samples + 1); // an answer already on its way may still come in
        relay.DropAll = false;
        Assert.False(await OnUiUntil(() =>
        {
            time.Advance(TimeSpan.FromMilliseconds(500));
            vm.Tick();
            return stats.Unreachable;
        }, u => !u));

        await OnUi(() => vm.Connection.DisconnectAsync());
        Assert.Equal((0, false, "-"), await OnUi(() => (stats.Bars, stats.Unreachable, stats.RoundTripText)));
    }

    /// <summary>Step 25g7: the loss adapter hears a new answer at the next tick (100 ms), not at the card's next 2 s refresh.</summary>
    [Fact]
    public async Task Tick_AnswerArrivesBetweenTwoRefreshes_LossAdapterHearsItAtTheNextTick()
    {
        var serverTime = new ManualTimeProvider(); // moved only while sending, so its uplink window never rolls
        await server.DisposeAsync();
        server = await TestServer.StartAsync(time: serverTime);
        using var relay = new LossyUdpRelay(server.VoiceEndPoint);
        var time = new ManualTimeProvider();
        await ConnectThroughRelayAsync(relay, time);
        await OnUi(() => vm.Audio.Loss = new LossAdapter(time, 64));
        await OnUiUntil(() =>
        {
            time.Advance(TimeSpan.FromSeconds(2));
            vm.Tick();
            return vm.Stats.Bars;
        }, bars => bars == 4); // the answer to the Hello is taken, the card has refreshed
        relay.DropUpVoicePercent = 20;
        var frame = new VoiceEncoder().Encode(new float[AudioFormat.FrameSamples]);
        for (int i = 0; i < 10; i++) // the voice burst of the server's rate limit
        {
            serverTime.Advance(AudioFormat.FrameDuration);
            await OnUi(() =>
            {
                vm.Audio.Send!(frame, OVS.Shared.Voice.VoiceHeader.TargetChannel);
                return 0;
            });
        }
        await AsyncWait.Until(() => relay.PassedUpVoice == 8);
        serverTime.Advance(TimeSpan.FromMilliseconds(200)); // tokens for the ping
        int answers = await OnUi(() =>
        {
            time.Advance(TimeSpan.FromSeconds(2)); // the ping goes out
            vm.Tick(); // the card refreshes here, before the answer is back
            return vm.Connection.VoiceLink.Client!.RoundTripSample.Count;
        });
        await OnUiUntil(() => vm.Connection.VoiceLink.Client!.RoundTripSample.Count, count => count > answers);
        Assert.Equal(LossAdapter.InitialHint, await OnUi(() => vm.Audio.Loss.LossHint)); // nobody has fed the answer yet

        int hint = await OnUi(() =>
        {
            time.Advance(TimeSpan.FromMilliseconds(100)); // far from the next 2 s refresh
            vm.Tick();
            return vm.Audio.Loss.LossHint;
        });

        Assert.True(hint > LossAdapter.InitialHint, $"loss hint {hint}");
    }

    [Fact]
    public async Task Tick_MixerHasJitter_StatsShowTheMixersJitter()
    {
        await ConnectAsync(saveBookmark: false);
        double measured = await OnUi(() =>
        {
            var frame = EngineProbe.SilentFrame();
            for (uint number = 0; number < 6; number++)
            {
                vm.Audio.Mixer.Push(1, number, frame, viaLink: false, arrivalMs: 20.0 * number + (number == 3 ? 15 : 0));
                vm.Audio.Mixer.Tick();
            }
            return vm.Audio.Mixer.Stats().JitterMs;
        });

        var shown = await OnUi(() =>
        {
            vm.Tick();
            return vm.Stats.JitterText;
        });

        Assert.True(measured > 0);
        Assert.Equal($"{measured:0.0} ms", shown);
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
            vm.Server!.Tree.Channels.SelectMany(c => c.Users).Single(u => u.Nickname == "bert").MessageCommand.Execute(null);
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
        // Package 139: a shutdown first starts reconnecting; "Abbrechen" ends it with the disconnected notice
        Assert.True(await OnUiUntil(() => vm.Connection.IsReconnecting, r => r));
        await CancelReconnectAsync();
        var bye = await OnUiUntil(() => vm.Notices.FirstOrDefault(n => n.Kind == NoticeKind.Warning), n => n is not null);
        Assert.StartsWith("Getrennt: ", bye?.Text);
        Assert.Matches(@"^\d{2}:\d{2}:\d{2}  Getrennt: ", bye!.ToString()); // the debug API keeps its "time  text" form
    }

    [Fact]
    public async Task StartScreen_BookmarksAndConnectingState()
    {
        Assert.False(await OnUi(() => vm.Bookmarks.HasAny));
        Assert.Equal($"PTT: {DefaultPushToTalk.Choose(DefaultPushToTalk.CountMouseButtons()).Name}", await OnUi(() => vm.TalkHint)); // Package 59: new profiles get a PTT key
        var changed = new List<string?>();
        var changedBookmarks = new List<string?>();
        await OnUi(() =>
        {
            vm.Connection.PropertyChanged += (_, e) => changed.Add(e.PropertyName);
            vm.Bookmarks.PropertyChanged += (_, args) => changedBookmarks.Add(args.PropertyName);
            return 0;
        });

        await ConnectAsync(saveBookmark: true);
        Assert.True(await OnUi(() => vm.Bookmarks.HasAny));
        Assert.Equal("anna", (await OnUi(() => vm.Bookmarks.Items)).Single().Nickname);
        Assert.Contains(nameof(BookmarkList.Items), changedBookmarks);
        Assert.Contains(nameof(ConnectionViewModel.IsConnecting), changed);
        Assert.False(await OnUi(() => vm.Connection.IsConnecting));
    }

    /// <summary>A20: settings and administration are pages of the main window, not windows.</summary>
    [Fact]
    public async Task Pages_OpenAndClose_LevelMeterRuns()
    {
        await ui.InvokeAsync<object?>(async () =>
        {
            vm.PageFlow.OpenSettings();
            Assert.Equal(Page.Settings, vm.PageFlow.Page);
            vm.PageFlow.SettingsPage!.CancelCommand.Execute(null);
            Assert.Equal(Page.Home, vm.PageFlow.Page);
            Assert.Null(vm.PageFlow.SettingsPage);

            await vm.PageFlow.OpenAdminAsync(); // not connected: nothing to administer
            Assert.Equal(Page.Home, vm.PageFlow.Page);
            MainViewModelSetup.ShowServer(vm, FakeServers.Admin());
            await vm.PageFlow.OpenAdminAsync();
            Assert.Equal(Page.Admin, vm.PageFlow.Page);
            await vm.Connection.DisconnectAsync(); // the administration needs a server
            Assert.Equal(Page.Home, vm.PageFlow.Page);
            Assert.Null(vm.PageFlow.AdminPage);
            return null;
        });

        await OnUi(() =>
        {
            vm.PageFlow.OpenSettings();
            vm.Audio.DeviceLifecycle.SetTone(440);
            return 0;
        });
        var level = await OnUiUntil(() => vm.PageFlow.SettingsPage!.InputLevelDb, l => l >= -30);
        Assert.InRange(level, -15, -12); // 0.3 amplitude sine: -13.5 dBFS

        await OnUi(() =>
        {
            vm.PageFlow.SettingsPage!.SelectedTheme = SettingsViewModel.Themes.Single(t => t.Value == AppTheme.Dark);
            vm.PageFlow.SettingsPage.SaveCommand.Execute(null);
            return 0;
        });
        Assert.Equal(Page.Home, await OnUi(() => vm.PageFlow.Page));
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
        var hardware = new FakeAudioHardware
        {
            Listing = direction =>
            {
                gate.Wait(TimeSpan.FromSeconds(10));
                return direction == AudioDirection.Input
                    ? [new AudioDevice("mic-1", "Headset"), new AudioDevice("mic-2", "XLR Mic")]
                    : [new AudioDevice("out-1", "Kopfhörer")];
            },
        };
        hardware.Present.Add("mic-2", AudioDirection.Input); // the saved microphone is plugged in, as the list says
        var main = await ui.InvokeAsync(() => Task.FromResult(MainViewModelSetup.Create(dir, ui.Post, hardware: hardware)));

        var watch = System.Diagnostics.Stopwatch.StartNew();
        var (page, inputs, hint) = await OnUi(() =>
        {
            main.PageFlow.OpenSettings();
            return (main.PageFlow.Page, main.PageFlow.SettingsPage!.Inputs.Select(i => i.Name).ToList(), main.PageFlow.SettingsPage.DeviceHint);
        });
        // not blocked by the gate (which holds 10 s): the margin is for a slow runner, the UI round trip alone may take a while
        Assert.True(watch.ElapsedMilliseconds < 5000, $"{watch.ElapsedMilliseconds} ms");
        Assert.Equal(Page.Settings, page);
        Assert.Equal(["Standardgerät", "Geräte werden geladen ..."], inputs);
        Assert.Null(hint);

        gate.Set();
        await OnUiUntil(() => main.PageFlow.SettingsPage!.Inputs.Count, n => n == 3);
        Assert.Equal(["Standardgerät", "Headset", "XLR Mic"], await OnUi(() => main.PageFlow.SettingsPage!.Inputs.Select(i => i.Name).ToList()));
        Assert.Equal("mic-2", await OnUi(() => main.PageFlow.SettingsPage!.SelectedInput.Id));
        Assert.Null(await OnUi(() => main.PageFlow.SettingsPage!.DeviceHint));

        // second time: the last list is there at once, a fresh one follows
        gate.Reset();
        var again = await OnUi(() =>
        {
            main.PageFlow.ClosePage();
            main.PageFlow.OpenSettings();
            return main.PageFlow.SettingsPage!.Inputs.Select(i => i.Name).ToList();
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
            vm.PageFlow.OpenSettings();
            vm.PageFlow.SettingsPage!.BackgroundOpacityPercent = 30;
            vm.PageFlow.SettingsPage.BlurBackground = true;
            return vm.Appearance;
        });
        Assert.Equal(new BackgroundAppearance(0.3f, true), live with { Opacity = MathF.Round(live.Opacity, 3) });
        Assert.Equal(new BackgroundAppearance(1f, false), await OnUi(() =>
        {
            vm.PageFlow.SettingsPage!.CancelCommand.Execute(null);
            return vm.Appearance;
        }));
        var saved = await OnUi(() =>
        {
            vm.PageFlow.OpenSettings();
            vm.PageFlow.SettingsPage!.BackgroundOpacityPercent = 60;
            vm.PageFlow.SettingsPage.SaveCommand.Execute(null);
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
            vm.PageFlow.OpenSettings();
            vm.PageFlow.SettingsPage!.SelectedDisplay = simplified;
            return vm.Appearance.Display;
        }));
        Assert.Equal(DisplayMode.Animated, await OnUi(() =>
        {
            vm.PageFlow.SettingsPage!.CancelCommand.Execute(null);
            return vm.Appearance.Display;
        }));
        Assert.Equal(DisplayMode.Simplified, await OnUi(() =>
        {
            vm.PageFlow.OpenSettings();
            vm.PageFlow.SettingsPage!.SelectedDisplay = simplified;
            vm.PageFlow.SettingsPage.SaveCommand.Execute(null);
            return vm.Appearance.Display;
        }));
        Assert.Equal(DisplayMode.Simplified, ClientSettings.Load(dir, out _).Display);
    }

    /// <summary>The settings page captures keys through the key poller: a key pressed while its dialog captures is the chord it gets.</summary>
    [Fact]
    public async Task OpenSettings_KeyPressedDuringCapture_PageGetsThePollersChord()
    {
        const int F1 = 0x70;
        Task<KeyChord?>? captured = null;
        await ui.InvokeAsync<object?>(async () =>
        {
            vm.PageFlow.OpenSettings();
            vm.PageFlow.SettingsPage!.EditKeyBinding = (_, capture) =>
            {
                captured = capture(KeyAction.ToggleMute);
                return Task.FromResult<KeyBinding?>(null);
            };
            await vm.PageFlow.SettingsPage.AddKeyBindingCommand.ExecuteAsync(null);
            return null;
        });
        await keys.NextSweep(); // the first sweep of a capture only records what is already down
        keys.Set(true, F1);
        try
        {
            Assert.Equal(new KeyChord(F1), await captured!.WaitAsync(TimeSpan.FromSeconds(5)));
        }
        finally
        {
            keys.Set(false, F1);
        }
    }

    /// <summary>
    /// Package 121: a key set on the page works at once (so the self test hears push-to-talk), leaving without saving
    /// brings the saved keys back, saving keeps the new ones.
    /// </summary>
    [Fact]
    public async Task SettingsPage_KeyBindingLive_RevertedOnCancel()
    {
        var key = new KeyBinding(KeyAction.PushToTalk, new KeyChord(VirtualKeys.RControl));
        async Task<IReadOnlyList<KeyBinding>> SetKeyOnPage()
        {
            await ui.InvokeAsync<object?>(async () =>
            {
                vm.PageFlow.OpenSettings();
                vm.PageFlow.SettingsPage!.IsPushToTalk = true;
                vm.PageFlow.SettingsPage.EditKeyBinding = (_, _) => Task.FromResult<KeyBinding?>(key);
                await vm.PageFlow.SettingsPage.SetPttKeyCommand.ExecuteAsync(null);
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
            vm.PageFlow.SettingsPage!.CancelCommand.Execute(null);
            return vm.Keys.Bindings;
        }));

        Assert.Equal([key], await SetKeyOnPage());
        Assert.Empty(await OnUi(() =>
        {
            vm.PageFlow.SettingsPage!.KeyBindings[0].RemoveCommand.Execute(null);
            return vm.Keys.Bindings;
        }));
        Assert.Empty(await OnUi(() =>
        {
            vm.PageFlow.ClosePage(); // what Esc does
            return vm.Keys.Bindings;
        }));

        Assert.Equal([key], await SetKeyOnPage());
        Assert.Equal([key], await OnUi(() =>
        {
            vm.PageFlow.SettingsPage!.SaveCommand.Execute(null);
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
            vm.PageFlow.OpenSettings();
            vm.PageFlow.SettingsPage!.SelectedDisplay = SettingsViewModel.Displays.Single(d => d.Value == DisplayMode.Simplified);
            vm.PageFlow.SettingsPage.BlurBackground = true;
            vm.PageFlow.ClosePage(); // what Esc does
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

        // closing the settings ends it (the rules and the page's button are in SelfTestTests and SelfTestWiringTests); a slider moved meanwhile goes back on "Verwerfen"
        await ui.InvokeAsync<object?>(async () =>
        {
            vm.PageFlow.OpenSettings();
            await vm.PageFlow.SettingsPage!.ToggleSelfTestCommand.ExecuteAsync(null);
            vm.PageFlow.SettingsPage.OutputVolumePercent = 50;
            return null;
        });
        Assert.Equal(0.5f, await OnUi(() => vm.Audio.Mixer.Volume));
        Assert.Equal((true, true), await SeenByServer(true));
        await OnUi(() =>
        {
            vm.PageFlow.SettingsPage!.CancelCommand.Execute(null);
            return 0;
        });
        Assert.Equal((false, false), await SeenByServer(false));
        Assert.False(await OnUi(() => vm.Audio.SelfTest));
        Assert.Equal(1f, await OnUi(() => vm.Audio.Mixer.Volume));
    }

    /// <summary>Package 51: the slider acts on the mixer at once, is kept by fingerprint and follows a new session.</summary>
    [Fact]
    public async Task UserVolume_ChangesMixer_SavesByFingerprint()
    {
        var settingsBefore = await OnUi(() =>
        {
            var before = vm.Settings;
            MainViewModelSetup.ShowServer(vm, FakeServers.Admin());
            vm.Server!.Tree.Channels.SelectMany(c => c.Users).Single(u => u.Nickname == "anna").VolumePercent = 200;
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
        Assert.Equal(200, await OnUi(() => vm.Server!.Tree.Channels.SelectMany(c => c.Users).Single(u => u.SessionId == 9).VolumePercent));

        await OnUi(() =>
        {
            vm.Server!.Tree.Channels.SelectMany(c => c.Users).Single(u => u.SessionId == 9).ResetVolumeCommand.Execute(null);
            return 0;
        });
        Assert.Equal(1f, await OnUi(() => vm.Audio.Mixer.SpeakerGain(9)));
        Assert.DoesNotContain("fp2", File.ReadAllText(Path.Combine(dir, ClientSettings.FileName)));
    }

    [Fact]
    public void Deafened_Engine_IgnoresIncomingVoice()
    {
        using var keys = new KeyPoller(TimeProvider.System); // no bindings: only simulated keys count
        using var engine = new AudioEngine(keys, TimeProvider.System, hardware: null) { Deafened = true };
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
            vm.PageFlow.OpenSettings();
            vm.Audio.DeviceLifecycle.SetTone(440);
            return 0;
        });
        Assert.Equal(-28, await OnUiUntil(() => vm.PageFlow.SettingsPage!.ThresholdInUseDb, t => t == -28));
        Assert.Equal(-28, await OnUi(() => vm.PageFlow.SettingsPage!.MeterThresholdDb));
    }

    [Fact]
    public async Task ToneInput_ReportsInputLevel()
    {
        using var keys = new KeyPoller(TimeProvider.System); // no bindings: only simulated keys count
        using var engine = new AudioEngine(keys, TimeProvider.System, hardware: null);
        var level = new TaskCompletionSource<float>();
        engine.InputLevel += db => level.TrySetResult(db);
        engine.DeviceLifecycle.SetTone(440);
        // 0.3 amplitude sine: RMS 0.212 = -13.5 dBFS
        Assert.InRange(await level.Task.WaitAsync(TimeSpan.FromSeconds(10)), -15f, -12f);
    }

    /// <summary>Package 102: applying the settings brings the sidebar's tiles up to date (the folding away is in <c>BookmarkListTests</c>).</summary>
    [Fact]
    public async Task ApplySettings_BookmarkAdded_TileAppears()
    {
        var tiles = await OnUi(() =>
        {
            var settings = vm.Settings;
            settings.Bookmarks.Add(new Bookmark("A", "a.example.org", 7000, "ich"));
            vm.ApplySettings(settings);
            return vm.Bookmarks.Items.Select(tile => tile.Name).ToList();
        });
        Assert.Equal(["A"], tiles);
    }

    /// <summary>Package 99, 61: the saved look counts from the start (display, background opacity and blur).</summary>
    [Fact]
    public async Task SavedAppearance_AppliesAtStart()
    {
        var saved = Directory.CreateTempSubdirectory("ovs-look-").FullName;
        try
        {
            new ClientSettings { Display = DisplayMode.Simplified, BackgroundOpacity = 0.5f, BlurBackground = true }.Save(saved);
            var started = MainViewModelSetup.Create(saved, a => a());
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
            MainViewModelSetup.ShowServer(vm, FakeServers.Admin(sent));
            vm.Server!.SoundRequested += heard.Add;
            return 0;
        });
        return (sent, heard);
    }

    /// <summary>The mute states sent, without the request ids.</summary>

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
        Assert.Equal([new SelfState(true, false)], await OnUi(() => MainViewModelSetup.ReadSelfStatesSent(sent)));
        Assert.Equal([SoundEvent.MicOff], await OnUi(() => heard.ToList()));

        await HoldMuteKey(false);
        Assert.False(await OnUiUntil(() => vm.Server!.SelfMuted, m => !m));
        Assert.Equal([new SelfState(true, false), new SelfState(false, false)], await OnUi(() => MainViewModelSetup.ReadSelfStatesSent(sent)));
        Assert.Equal([SoundEvent.MicOff, SoundEvent.MicOn], await OnUi(() => heard.ToList()));
    }

    /// <summary>The mute button of a connected session reaches the engine at once, as the server's property changes (no state change comes with it).</summary>
    [Fact]
    public async Task ToggleMute_Connected_EngineIsMutedAtOnce()
    {
        await ConnectAsync(saveBookmark: false);

        Assert.True(await OnUi(() =>
        {
            vm.Server!.ToggleMuteCommand.Execute(null);
            return vm.Audio.SelfMuted;
        }));
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
        Assert.Equal([deafened ? new SelfState(false, false) : new SelfState(true, true)], await OnUi(() => MainViewModelSetup.ReadSelfStatesSent(sent)));
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
        Assert.Equal([new SelfState(true, false), new SelfState(false, false), new SelfState(true, true)], await OnUi(() => MainViewModelSetup.ReadSelfStatesSent(sent)));
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
        Assert.Equal([new SelfState(true, false), new SelfState(false, false), new SelfState(true, true)], await OnUi(() => MainViewModelSetup.ReadSelfStatesSent(sent)));
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
            MainViewModelSetup.ShowServer(vm, FakeServers.Admin(sent));
            return vm.Server!.SelfMuted;
        }));
        await HoldMuteKey(false);
        Assert.False(await OnUiUntil(() => vm.Server!.SelfMuted, m => !m));
        Assert.Equal([new SelfState(true, false), new SelfState(false, false)], await OnUi(() => MainViewModelSetup.ReadSelfStatesSent(sent)));
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
            vm.Audio.DeviceLifecycle.SetTone(440); // -13.5 dBFS: well above the threshold
            await vm.SelfTest.SetAsync(true);
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

    /// <summary>
    /// Package 124: the microphone opens on a pool thread; one still queued when the engine goes away (closing the
    /// client with the settings page open) must not open it afterwards. Many engines, so the race shows.
    /// </summary>
    [Fact]
    public async Task AudioEngine_DisposedWhileMicOpening_StaysClosed()
    {
        using var keys = new KeyPoller(TimeProvider.System);
        var hardware = new FakeAudioHardware();
        for (int i = 0; i < 50; i++)
        {
            var engine = new AudioEngine(keys, TimeProvider.System, hardware: hardware);
            engine.LevelMeter = true;
            engine.Dispose();
        }
        await Task.Delay(500); // the queued opens have run by now
        Assert.Equal(0, hardware.Counts.MicsOpen);
    }

    /// <summary>
    /// Package 124: a slider moved on the settings page before the level meter's microphone is open (it opens in the
    /// background) still counts once it is; new saved settings replace the live ones.
    /// </summary>
    [Fact]
    public async Task AudioEngine_LiveSettingsBeforeMicOpen_Applied()
    {
        using var keys = new KeyPoller(TimeProvider.System);
        using var engine = new AudioEngine(keys, TimeProvider.System, hardware: new FakeAudioHardware());
        engine.ApplyLive(new ClientSettings { InputGain = 0.5f, VadThresholdDb = -25, NoiseSuppression = false, AutoGain = true });
        engine.LevelMeter = true;
        var watch = System.Diagnostics.Stopwatch.StartNew();
        while (engine.DeviceLifecycle.CaptureSource.Capture is null && watch.ElapsedMilliseconds < 5000) await Task.Delay(10);
        var capture = engine.DeviceLifecycle.CaptureSource.Capture!;
        Assert.Equal((0.5f, -25f, false, true), (capture.Gain, capture.Vad.ThresholdDb, capture.NoiseSuppression, capture.AutoGain));

        engine.Configure(new ClientSettings { InputGain = 0.8f });
        Assert.Equal(0.8f, engine.DeviceLifecycle.CaptureSource.Capture!.Gain);
    }

    /// <summary>
    /// Package 124 (A132): after the start nothing is open and no playback thread runs; the settings page opens the
    /// microphone while it shows, the self test and a connection open both devices, and they close again. A device
    /// changed in the settings while connected is switched at once.
    /// </summary>
    [Fact]
    public async Task Devices_WhileNeeded_OpenAndCloseAgain()
    {
        var hardware = new FakeAudioHardware();
        var own = Directory.CreateTempSubdirectory("ovs-devices-").FullName;
        var main = await ui.InvokeAsync(() => Task.FromResult(MainViewModelSetup.Create(own, ui.Post, keyState: keys.IsDown, hardware: hardware, trustServers: true)));
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
                var (mics, micsOpen, speakers, speakersOpen) = hardware.Counts;
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
                main.PageFlow.OpenSettings();
                return Task.CompletedTask;
            });
            Assert.Equal((1, 1, 0, 0, false), await Settled((1, 1, 0, 0, false))); // the level meter
            await Do(() =>
            {
                main.PageFlow.ClosePage();
                return Task.CompletedTask;
            });
            Assert.Equal((1, 0, 0, 0, false), await Settled((1, 0, 0, 0, false)));

            await Do(() => main.SelfTest.SetAsync(true));
            Assert.Equal((2, 1, 1, 1, true), await Settled((2, 1, 1, 1, true)));
            await Do(() => main.SelfTest.SetAsync(false));
            Assert.Equal((2, 0, 1, 0, false), await Settled((2, 0, 1, 0, false)));

            await Do(() => main.Connection.ConnectAsync(new ConnectChoice("127.0.0.1", server.Port, "anna", null, false)));
            Assert.True(await OnUi(() => main.IsConnected));
            Assert.Equal((3, 1, 2, 1, true), await Settled((3, 1, 2, 1, true)));
            await Do(() => main.ApplySettingsAsync(new ClientSettings { InputDeviceId = "mic-2" }));
            Assert.Equal((4, 1, 3, 1, true), await Settled((4, 1, 3, 1, true)));
            Assert.Equal("mic-2", hardware.LastMicId);

            await Do(main.Connection.DisconnectAsync);
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
        var main = await ui.InvokeAsync(() => Task.FromResult(MainViewModelSetup.Create(own, ui.Post, keyState: keys.IsDown, trustServers: true)));
        try
        {
            int uiThread = await OnUi(() => Environment.CurrentManagedThreadId);
            var writers = new ConcurrentQueue<int>();
            main.Log.Writing += () => writers.Enqueue(Environment.CurrentManagedThreadId);
            await ui.InvokeAsync<object?>(async () =>
            {
                await main.Connection.ConnectAsync(new ConnectChoice("127.0.0.1", server.Port, "anna", null, false));
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
            for (int i = 0; i < 5; i++) order.AddRange([$"gast{i} connected", $"gast{i} disconnected"]);
            order.Add("Disconnected (own action)"); // written while closing
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
