using System.Globalization;
using OVS.Client.Audio;
using OVS.Client.Audio.Sounds;
using OVS.Client.Dialogs;
using OVS.Client.Exceptions;
using OVS.Client.Input;
using OVS.Client.Localization;
using OVS.Client.Net;
using OVS.Client.ViewModels;
using OVS.Shared.Protocol;
using OVS.Shared.Protocol.Messages.Channels;
using OVS.Shared.Protocol.Messages.Connection;
using OVS.Tests.TestSupport;

namespace OVS.Tests.Client.ViewModels;

/// <summary>
/// Package 139 (A148): the automatic reconnect after a lost connection, through the main view model against a real server,
/// on a manual clock and the connect seam (moved here from <c>MainViewModelTests</c> in 25g5b). The connection's own
/// facts, without a main view model, are in <see cref="ConnectionViewModelTests"/>.
/// </summary>
#pragma warning disable CA1001 // its dispatcher is disposed in DisposeAsync (IAsyncLifetime)
public sealed class ReconnectTests : IAsyncLifetime
#pragma warning restore CA1001
{
    readonly TestDispatcher ui = new();
    readonly string dir = Directory.CreateTempSubdirectory("ovs-reconnect-").FullName;
    readonly FakeKeys keys = new(); // Package 125: a real key pressed while the tests run does not count
    readonly ManualTimeProvider reconnectTime = new();
    TestServer server = null!;
    ConnectSeam seam = null!;
    MainViewModel viewModel = null!;

    static string Retrying(int seconds) => TextFormat.Of(Strings.Status_Reconnecting, seconds);

    const string Reconnected = "Verbindung wiederhergestellt";
    const string OutdatedAttemptLine = "outdated attempt dropped";

    public async Task InitializeAsync()
    {
        // the welcome text is there so that "one notice after a reconnect" shows that a reconnect brings no welcome text again
        server = await TestServer.StartAsync(serverData => serverData.Settings.WelcomeText = "Willkommen auf dem Testserver!");
        seam = new ConnectSeam(dir, reconnectTime);
        viewModel = await ui.InvokeAsync(() => Task.FromResult(MainViewModelSetup.Create(dir, ui.Post, keyState: keys.IsDown, trustServers: true, time: reconnectTime,
            connect: seam.Connect)));
    }

    public async Task DisposeAsync()
    {
        await ui.RunAsync(() => viewModel.DisposeAsync().AsTask());
        ui.Dispose();
        await server.DisposeAsync();
        Directory.Delete(dir, true);
    }

    Task<T> OnUiAsync<T>(Func<T> read) => ui.ReadAsync(read);

    Task<T> OnUiUntilAsync<T>(Func<T> read, Func<T, bool> done) => ui.ReadUntilAsync(read, done);

    string ClientLogText() => MainViewModelSetup.ReadLogText(viewModel, dir);

    /// <summary>Connects anna on the manual reconnect clock, through <see cref="seam"/>.</summary>
    async Task ConnectToAsync(TestServer to, string? password = null)
    {
        await ui.RunAsync(() => viewModel.Connection.ConnectAsync(new ConnectChoice("127.0.0.1", to.Port, "anna", password, SaveBookmark: false)));
        Assert.True(await OnUiAsync(() => viewModel.IsConnected));
    }

    /// <summary>The server drops anna's connection without a word, like a broken network.</summary>
    async Task DropAsync(TestServer on)
    {
        var id = await OnUiAsync(() => viewModel.Server!.Mirror.SelfId);
        on.State.FindSession(id)!.Outbox.Abort("Test");
        Assert.True(await OnUiUntilAsync(() => viewModel.Connection.IsReconnecting, value => value));
    }

    Task<string> TickStatusAsync() => OnUiAsync(() =>
    {
        viewModel.Tick();
        return viewModel.Status;
    });

    /// <summary>Ticks and reads the status until <paramref name="done"/> accepts it; returns the last one.</summary>
    Task<string> TickStatusUntilAsync(Func<string, bool> done) => OnUiUntilAsync(() =>
    {
        viewModel.Tick();
        return viewModel.Status;
    }, done);

    /// <summary>Presses "Abbrechen" of a running series.</summary>
    Task<int> CancelReconnectAsync() => OnUiAsync(() =>
    {
        viewModel.Connection.CancelReconnectCommand.Execute(null);
        return 0;
    });

    /// <summary>The notices added since <paramref name="before"/> of them were there, oldest first.</summary>
    Task<List<string>> NoticesSinceAsync(int before) => OnUiAsync(() => viewModel.Notices.Take(viewModel.Notices.Count - before).Reverse().Select(notice => notice.Text).ToList());

    static Action<OVS.Server.Data.Records.ServerData> SecretChannel(Guid id) => serverData => serverData.Channels.Add(new OVS.Server.Data.Records.ChannelRecord
    {
        Id = id, Name = "Geheim", Order = 1, StoredPassword = OVS.Server.Security.StoredPassword.Create("pw"),
    });

    /// <summary>Back into the password channel, deafened as before, with one notice; the server sees the state too.</summary>
    [Fact]
    public async Task ConnectionLost_Reconnects_RestoresChannelAndMute()
    {
        var secret = Guid.NewGuid();
        await using var own = await TestServer.StartAsync(SecretChannel(secret));
        int asked = 0;
        viewModel.Dialogs = new DialogCallbacks { Server = new() { AskChannelPassword = _ => { asked++; return Task.FromResult<string?>("pw"); } } };
        await ConnectToAsync(own);
        await OnUiAsync(() => viewModel.Server!.JoinAsync(secret));
        Assert.Equal(secret, await OnUiUntilAsync(() => viewModel.Server!.Mirror.Self!.ChannelId, id => id == secret));
        await OnUiAsync(() => viewModel.Server!.SelfState.ToggleDeafenCommand.ExecuteAsync(null));
        var before = await OnUiAsync(() => (viewModel.Server!.Mirror.SelfId, viewModel.Notices.Count));

        await DropAsync(own);
        reconnectTime.Advance(TimeSpan.FromSeconds(1));
        Assert.True(await OnUiUntilAsync(() => viewModel.Server?.Mirror.Self is { SelfDeafened: true } self && self.ChannelId == secret, ok => ok));

        var (session, muted, deafened, reconnecting, status) = await OnUiAsync(() =>
            (viewModel.Server!.Mirror.SelfId, viewModel.Server.SelfState.SelfMuted, viewModel.Server.SelfState.SelfDeafened, viewModel.Connection.IsReconnecting, viewModel.Status));
        Assert.NotEqual(before.SelfId, session); // a new session
        Assert.True(muted && deafened);
        Assert.False(reconnecting);
        Assert.Equal("Verbunden mit Testserver", status);
        Assert.Equal(1, asked); // the password from before, no dialog
        Assert.Equal([Reconnected], await NoticesSinceAsync(before.Count));
        Assert.DoesNotContain("pw", ClientLogText(), StringComparison.Ordinal);
    }

    Task OnUiDoAsync(Func<Task> action) => ui.RunAsync(action);

    /// <summary>Drops the connection and lets the first attempt bring a new session.</summary>
    async Task ReconnectAsync(TestServer on)
    {
        var before = await OnUiAsync(() => viewModel.Server!.Mirror.SelfId);
        await DropAsync(on);
        reconnectTime.Advance(TimeSpan.FromSeconds(1));
        Assert.True(await OnUiUntilAsync(() => viewModel.Server is { } server && server.Mirror.SelfId != before && !viewModel.Connection.IsReconnecting, ok => ok));
    }

    [Fact]
    public async Task Reconnect_AfterALostConnection_TheOldServerAsksForNoLogo()
    {
        await ConnectToAsync(server);
        var oldServer = (await OnUiAsync(() => viewModel.Server))!;
        await ReconnectAsync(server);
        int requestsBefore = await OnUiAsync(() => CountLogoRequests());

        await OnUiAsync(() =>
        {
            oldServer.Apply(new OVS.Shared.Protocol.Messages.ServerInfo.ServerSettingsChanged(oldServer.Mirror.Settings with { IconHash = "hash" }));
            return 0;
        });

        Assert.Equal(requestsBefore, await OnUiAsync(() => CountLogoRequests()));
    }

    int CountLogoRequests() => MainViewModelSetup.ReadLogText(viewModel, dir).Split("Request server logo").Length - 1;

    /// <summary>Review: muted, then deafened, stays muted when undeafening after a reconnect; muted alone stays muted.</summary>
    [Theory]
    [InlineData("muted")]
    [InlineData("muted, deafened")]
    public async Task Reconnect_MutedWithOrWithoutDeafen_StaysMutedWhenUndeafening(string state)
    {
        bool deafen = state.Contains("deafened", StringComparison.Ordinal);
        await ConnectToAsync(server);
        await OnUiDoAsync(() => viewModel.Server!.SelfState.ToggleMuteCommand.ExecuteAsync(null));
        if (deafen) await OnUiDoAsync(() => viewModel.Server!.SelfState.ToggleDeafenCommand.ExecuteAsync(null));

        await ReconnectAsync(server);
        Assert.True(await OnUiUntilAsync(() => viewModel.Server!.Mirror.Self is { SelfMuted: true } me && me.SelfDeafened == deafen, ok => ok));
        if (deafen) await OnUiDoAsync(() => viewModel.Server!.SelfState.ToggleDeafenCommand.ExecuteAsync(null)); // sound on again
        Assert.Equal((true, false), await OnUiAsync(() => (viewModel.Server!.SelfState.SelfMuted, viewModel.Server.SelfState.SelfDeafened)));
        Assert.True(await OnUiUntilAsync(() => viewModel.Server!.Mirror.Self is { SelfMuted: true, SelfDeafened: false }, ok => ok)); // the server too
    }

    /// <summary>Review: the mute of a held push-to-mute key is not kept; let go during the outage or after it, the microphone is on again.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Reconnect_PushToMuteHeld_NotKept(bool heldThrough)
    {
        await ConnectToAsync(server);
        viewModel.Keys.Simulate(KeyAction.PushToMute, true);
        Assert.True(await OnUiUntilAsync(() => viewModel.Server!.SelfState.SelfMuted, value => value));
        var before = await OnUiAsync(() => viewModel.Server!.Mirror.SelfId);
        await DropAsync(server);
        if (!heldThrough)
        {
            viewModel.Keys.Simulate(KeyAction.PushToMute, false);
            await OnUiUntilAsync(() => viewModel.Keys.MuteHeld, value => !value);
        }
        reconnectTime.Advance(TimeSpan.FromSeconds(1));
        Assert.True(await OnUiUntilAsync(() => viewModel.Server is { } server && server.Mirror.SelfId != before && !viewModel.Connection.IsReconnecting, ok => ok));
        if (heldThrough)
        {
            Assert.True(await OnUiAsync(() => viewModel.Server!.SelfState.SelfMuted)); // still held: muted
            viewModel.Keys.Simulate(KeyAction.PushToMute, false);
        }
        Assert.False(await OnUiUntilAsync(() => viewModel.Server!.SelfState.SelfMuted, value => !value));
        Assert.True(await OnUiUntilAsync(() => viewModel.Server!.Mirror.Self is { SelfMuted: false }, ok => ok));
    }

    /// <summary>The self test mutes and deafens at the server; a reconnect brings back the state from before it, not the test's.</summary>
    [Fact]
    public async Task Reconnect_SelfTestRunningOnAMutedUser_BringsBackTheMuteFromBeforeIt()
    {
        await ConnectToAsync(server);
        await OnUiDoAsync(() => viewModel.Server!.SelfState.ToggleMuteCommand.ExecuteAsync(null));
        await OnUiDoAsync(() => viewModel.SelfTest.SetAsync(true));
        Assert.True(await OnUiUntilAsync(() => viewModel.Server!.Mirror.Self is { SelfMuted: true, SelfDeafened: true }, ok => ok));

        await ReconnectAsync(server);

        Assert.Equal((true, false), await OnUiAsync(() => (viewModel.Server!.SelfState.SelfMuted, viewModel.Server.SelfState.SelfDeafened)));
    }

    /// <summary>The self test feeds the mixer without a connection; frames of it during the wait do not count for the new connection.</summary>
    [Fact]
    public async Task Reconnect_SelfTestFramesDuringTheWait_StatsCountFromTheNewConnection()
    {
        await ConnectToAsync(server);
        var before = await OnUiAsync(() => viewModel.Server!.Mirror.SelfId);
        await DropAsync(server);
        var fed = await OnUiAsync(() =>
        {
            var frame = EngineProbe.SilentFrame();
            foreach (uint number in new uint[] { 0, 1, 2, 5, 6, 7, 8, 9 })
            {
                viewModel.Audio.Mixer.Push(AudioEngine.SelfTestSpeaker, number, frame, viaLink: false, arrivalMs: 20.0 * number);
                viewModel.Audio.Mixer.Tick();
            }
            for (int tick = 0; tick < 20; tick++) viewModel.Audio.Mixer.Tick();
            var counts = viewModel.Audio.Mixer.Stats();
            return counts.Concealed + counts.Rebuilt + counts.Recovered;
        });
        Assert.True(fed > 0);
        reconnectTime.Advance(TimeSpan.FromSeconds(1));
        Assert.True(await OnUiUntilAsync(() => viewModel.Server is { } server && server.Mirror.SelfId != before && !viewModel.Connection.IsReconnecting, ok => ok));

        var shown = await OnUiAsync(() =>
        {
            viewModel.Tick();
            return viewModel.Stats.ConcealedText;
        });

        Assert.Equal(TextFormat.Of(Strings.ConnDetails_ConcealedValue, 0, 0, 0), shown);
    }

    /// <summary>Re-review: muted by choice (and deafened), push-to-mute held through the reconnect and then let go: still muted.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Reconnect_ChosenMuteAndHeldKeyReleased_StaysMuted(bool deafen)
    {
        await ConnectToAsync(server);
        await OnUiDoAsync(() => viewModel.Server!.SelfState.ToggleMuteCommand.ExecuteAsync(null));
        if (deafen) await OnUiDoAsync(() => viewModel.Server!.SelfState.ToggleDeafenCommand.ExecuteAsync(null));
        viewModel.Keys.Simulate(KeyAction.PushToMute, true);
        await OnUiUntilAsync(() => viewModel.Keys.MuteHeld, value => value);

        await ReconnectAsync(server);
        viewModel.Keys.Simulate(KeyAction.PushToMute, false);
        await OnUiUntilAsync(() => viewModel.Keys.MuteHeld, value => !value);
        await AsyncWait.AssertStaysFalseAsync(() => OnUiAsync(() => !viewModel.Server!.SelfState.SelfMuted), TimeSpan.FromMilliseconds(200),
            "the release, which reaches the view model through the UI queue, keeps the own mute");
        Assert.Equal((true, deafen), await OnUiAsync(() => (viewModel.Server!.SelfState.SelfMuted, viewModel.Server.SelfState.SelfDeafened)));
        Assert.True(await OnUiUntilAsync(() => viewModel.Server!.Mirror.Self is { SelfMuted: true } me && me.SelfDeafened == deafen, ok => ok));
    }

    /// <summary>A reconnect brings no welcome text again: the server has one and the only notice is the reconnected one.</summary>
    [Fact]
    public async Task Reconnect_ServerHasWelcomeText_AddsOnlyTheReconnectedNotice()
    {
        await ConnectToAsync(server);
        int before = await OnUiAsync(() => viewModel.Notices.Count);

        await ReconnectAsync(server);

        Assert.Equal([Reconnected], await NoticesSinceAsync(before));
    }

    /// <summary>Review: a server restart reconnects like a shutdown.</summary>
    [Fact]
    public async Task Reconnect_ServerRestarts_ConnectsAgainLikeAfterAShutdown()
    {
        var own = await TestServer.StartAsync();
        await ConnectToAsync(own);
        await own.Control.StopAsync(restart: true);
        Assert.True(await OnUiUntilAsync(() => viewModel.Connection.IsReconnecting, value => value));
        await using var back = await own.RestartAsync(samePort: true);
        reconnectTime.Advance(TimeSpan.FromSeconds(1));
        Assert.True(await OnUiUntilAsync(() => viewModel.IsConnected && !viewModel.Connection.IsReconnecting, ok => ok));
    }

    /// <summary>Review: an unexpected exception in an attempt counts as a failed attempt; the series goes on.</summary>
    [Fact]
    public async Task Reconnect_UnexpectedFailure_CountsAsFailedAttempt()
    {
        var first = await TestServer.StartAsync();
        int port = first.Port;
        await ConnectToAsync(first);
        seam.FailNext(new InvalidOperationException("Testfehler"));
        await first.DisposeAsync();
        Assert.True(await OnUiUntilAsync(() => viewModel.Connection.IsReconnecting, value => value));
        await using var back = await TestServer.StartAsync(port: port);
        reconnectTime.Advance(TimeSpan.FromSeconds(1));
        Assert.Equal(Retrying(2), await TickStatusUntilAsync(status => status.EndsWith(" 2 s", StringComparison.Ordinal)));
        reconnectTime.Advance(TimeSpan.FromSeconds(2));
        Assert.True(await OnUiUntilAsync(() => viewModel.IsConnected && !viewModel.Connection.IsReconnecting, ok => ok));
        Assert.Contains("Testfehler", ClientLogText(), StringComparison.Ordinal);
    }

    /// <summary>None of these start a series; the session ends as before.</summary>
    [Theory]
    [InlineData(ReasonCodes.Kicked)]
    [InlineData(ReasonCodes.Banned)]
    [InlineData(ReasonCodes.ReplacedByNewConnection)]
    [InlineData(ReasonCodes.VersionMismatch)]
    [InlineData(ReasonCodes.WrongPassword)]
    [InlineData("own")]
    public async Task Reconnect_DeliberateEndOfTheSession_StartsNoSeries(string reason)
    {
        await using var locked = reason == ReasonCodes.WrongPassword ? await TestServer.StartAsync(password: "alt") : null;
        if (locked is not null)
        {
            // the server comes back with another password: the attempt is refused and the series ends there
            await ConnectToAsync(locked, "alt");
            seam.FailNext(new ConnectionRejectedException(ReasonCodes.WrongPassword, null));
            await locked.DisposeAsync();
            Assert.True(await OnUiUntilAsync(() => viewModel.Connection.IsReconnecting, value => value)); // ServerShutdown retries
            reconnectTime.Advance(TimeSpan.FromSeconds(1));
            Assert.StartsWith("Abgelehnt", await OnUiUntilAsync(() => viewModel.Status, text => text.StartsWith("Abgelehnt", StringComparison.Ordinal)), StringComparison.Ordinal);
            Assert.Contains(SoundEvent.Disconnected, await OnUiAsync(() => viewModel.Audio.Sounds.Recent)); // review: the series ended, so the tone
        }
        else
        {
            await ConnectToAsync(server);
            if (reason == "own")
                await ui.RunAsync(viewModel.Connection.DisconnectAsync);
            else
            {
                var id = await OnUiAsync(() => viewModel.Server!.Mirror.SelfId);
                server.State.FindSession(id)!.Outbox.Close(new Disconnected(reason));
            }
            Assert.False(await OnUiUntilAsync(() => viewModel.IsConnected, value => !value));
        }

        Assert.False(await OnUiAsync(() => viewModel.Connection.IsReconnecting));
        int attemptsBefore = seam.Attempts.Count;
        reconnectTime.Advance(TimeSpan.FromSeconds(60));
        await OnUiAsync(() => 0); // what the clock posted has run
        Assert.Equal(attemptsBefore, seam.Attempts.Count);
        Assert.False(await OnUiAsync(() => viewModel.IsConnected));
        Assert.False(await OnUiAsync(() => viewModel.Connection.IsReconnecting));
        if (locked is null) Assert.Equal(0, await OnUiUntilAsync(() => server.State.SessionCount, count => count == 0));
    }

    /// <summary>"Abbrechen" ends the series at once; connecting elsewhere ends it too and an old attempt's late success is dropped.</summary>
    [Fact]
    public async Task Reconnect_CancelledOrConnectedElsewhere_EndsTheSeriesAndDropsTheOldAttempt()
    {
        await ConnectToAsync(server);
        await DropAsync(server);
        await CancelReconnectAsync();
        Assert.False(await OnUiAsync(() => viewModel.Connection.IsReconnecting));
        Assert.StartsWith("Getrennt", await TickStatusAsync(), StringComparison.Ordinal);
        int attemptsBefore = seam.Attempts.Count;
        reconnectTime.Advance(TimeSpan.FromSeconds(60));
        await OnUiAsync(() => 0); // what the clock posted has run
        Assert.Equal(attemptsBefore, seam.Attempts.Count);
        Assert.False(await OnUiAsync(() => viewModel.IsConnected));
        Assert.Equal(0, await OnUiUntilAsync(() => server.State.SessionCount, count => count == 0));

        // an attempt is held while the server is back
        var first = await TestServer.StartAsync();
        int port = first.Port;
        await ConnectToAsync(first);
        var oldAttempt = seam.HoldNext();
        await first.DisposeAsync();
        Assert.True(await OnUiUntilAsync(() => viewModel.Connection.IsReconnecting, value => value));
        await using var back = await TestServer.StartAsync(serverData => serverData.Settings.Name = "Zurück", port: port);
        reconnectTime.Advance(TimeSpan.FromSeconds(1));
        var held = await HangGuard.BoundAsync(oldAttempt.Reached);
        Assert.Equal(seam.Attempts.Count, held.Number);

        await ConnectToAsync(server); // meanwhile the user connects elsewhere
        Assert.False(await OnUiAsync(() => viewModel.Connection.IsReconnecting));
        oldAttempt.Release(); // the old attempt ends late
        Assert.Contains(OutdatedAttemptLine, await OnUiUntilAsync(ClientLogText, log => log.Contains(OutdatedAttemptLine, StringComparison.Ordinal)), StringComparison.Ordinal);
        Assert.Equal(0, await OnUiUntilAsync(() => back.State.SessionCount, count => count == 0));
        var (name, connected) = await OnUiAsync(() => (viewModel.Server?.ServerName, viewModel.IsConnected));
        Assert.True(connected);
        Assert.Equal("Testserver", name);
    }

    /// <summary>
    /// While retrying the status counts down, no "Getrennt" tone plays and the administration
    /// is closed; after the fifth failure the usual notice and tone come.
    /// </summary>
    [Fact]
    public async Task Reconnect_AttemptsRunning_StatusCountsDownWithoutToneUntilTheFifthFails()
    {
        using var identity = IdentityFile.LoadOrCreate(dir); // anna is an admin there
        await using var own = await TestServer.StartAsync(TestServer.Grant(identity, "Admin"));
        await ConnectToAsync(own);
        seam.FailEvery(ConnectSeam.CreateClosedPortError); // every attempt is refused at once, no real connect to wait for
        await OnUiAsync(() => viewModel.PageFlow.OpenAdminAsync());
        Assert.True(await OnUiAsync(() => viewModel.PageFlow.IsAdminPage));
        var before = await OnUiAsync(() => viewModel.Notices.Count);
        Assert.Contains(SoundEvent.Connected, await OnUiAsync(() => viewModel.Audio.Sounds.Recent)); // the tones are counted at all

        await own.DisposeAsync(); // ServerShutdown, and nobody answers any more
        Assert.True(await OnUiUntilAsync(() => viewModel.Connection.IsReconnecting, value => value));
        Assert.True(await OnUiAsync(() => viewModel.PageFlow.IsHomePage && viewModel.PageFlow.AdminPage is null));
        Assert.Equal(Retrying(1), await TickStatusAsync());
        Assert.Equal("", await OnUiAsync(() => viewModel.Connection.PingText));
        reconnectTime.Advance(TimeSpan.FromMilliseconds(400));
        Assert.Equal(Retrying(1), await TickStatusAsync());

        foreach (int next in new[] { 2, 4, 8, 16 })
        {
            reconnectTime.Advance(TimeSpan.FromMilliseconds(600));
            Assert.Equal(Retrying(next),
                await TickStatusUntilAsync(status => status.EndsWith($" {next} s", StringComparison.Ordinal)));
            reconnectTime.Advance(TimeSpan.FromSeconds(next) - TimeSpan.FromMilliseconds(600));
            Assert.Equal(Retrying(1), await TickStatusAsync());
        }
        Assert.DoesNotContain(SoundEvent.Disconnected, await OnUiAsync(() => viewModel.Audio.Sounds.Recent));
        Assert.Empty(await NoticesSinceAsync(before));

        reconnectTime.Advance(TimeSpan.FromMilliseconds(600)); // the fifth attempt fails too
        Assert.False(await OnUiUntilAsync(() => viewModel.Connection.IsReconnecting, value => !value));
        Assert.Equal("Getrennt: Der Server wurde heruntergefahren.", await TickStatusAsync());
        Assert.Equal(["Getrennt: Der Server wurde heruntergefahren."], await NoticesSinceAsync(before));
        Assert.Contains(SoundEvent.Disconnected, await OnUiAsync(() => viewModel.Audio.Sounds.Recent));
    }

    /// <summary>Review: a refusal that says "not now" (shutdown, restart, rate limit) is a failed attempt; the series goes on.</summary>
    [Theory]
    [InlineData(ReasonCodes.ServerShutdown)]
    [InlineData(ReasonCodes.ServerRestart)]
    [InlineData(ReasonCodes.RateLimited)]
    public async Task Reconnect_RefusedWithANotNowCode_SchedulesTheNextAttempt(string code)
    {
        await ConnectToAsync(server);
        seam.FailNext(new ConnectionRejectedException(code, null));
        await DropAsync(server);

        reconnectTime.Advance(TimeSpan.FromSeconds(1));

        Assert.Equal(Retrying(2), await TickStatusUntilAsync(status => status.EndsWith(" 2 s", StringComparison.Ordinal)));
        Assert.True(await OnUiAsync(() => viewModel.Connection.IsReconnecting));
    }

    /// <summary>Review: a certificate the user turns down ends the series at once, with its status, the disconnected notice and the tone.</summary>
    [Fact]
    public async Task Reconnect_TofuRejected_EndsTheSeriesWithStatusNoticeAndTone()
    {
        await ConnectToAsync(server);
        seam.FailNext(new TofuRejectedException());
        int noticesBefore = await OnUiAsync(() => viewModel.Notices.Count);
        await DropAsync(server);

        reconnectTime.Advance(TimeSpan.FromSeconds(1));

        Assert.Equal(Strings.Status_TofuRejected, await OnUiUntilAsync(() => viewModel.Status, status => status == Strings.Status_TofuRejected));
        Assert.False(await OnUiAsync(() => viewModel.Connection.IsReconnecting));
        var notices = await NoticesSinceAsync(noticesBefore);
        Assert.StartsWith("Getrennt", Assert.Single(notices), StringComparison.Ordinal);
        Assert.Contains(SoundEvent.Disconnected, await OnUiAsync(() => viewModel.Audio.Sounds.Recent));
    }

    /// <summary>An attempt that gets in after "Abbrechen" is dropped and its connection closed.</summary>
    [Fact]
    public async Task Reconnect_AttemptAfterCancel_IsDroppedAndDisposed()
    {
        await ConnectToAsync(server);
        var oldAttempt = seam.HoldNext();
        await DropAsync(server);
        reconnectTime.Advance(TimeSpan.FromSeconds(1));
        var held = await HangGuard.BoundAsync(oldAttempt.Reached);
        Assert.Equal(seam.Attempts.Count, held.Number);
        await CancelReconnectAsync();

        oldAttempt.Release();

        Assert.Contains(OutdatedAttemptLine, await OnUiUntilAsync(ClientLogText, log => log.Contains(OutdatedAttemptLine, StringComparison.Ordinal)), StringComparison.Ordinal);
        Assert.Equal(0, await OnUiUntilAsync(() => server.State.SessionCount, count => count == 0));
        Assert.False(await OnUiAsync(() => viewModel.IsConnected));
    }

    /// <summary>An attempt that fails after "Abbrechen" is only logged: the status, the notices and the tones stay as the cancel left them.</summary>
    [Fact]
    public async Task Reconnect_AttemptFailingAfterCancel_IsOnlyLogged()
    {
        await ConnectToAsync(server);
        var oldAttempt = seam.HoldNext(failWith: new ConnectionRejectedException(ReasonCodes.WrongPassword, null));
        await DropAsync(server);
        reconnectTime.Advance(TimeSpan.FromSeconds(1));
        await HangGuard.BoundAsync(oldAttempt.Reached);
        await CancelReconnectAsync();
        var statusAfterCancel = await OnUiAsync(() => viewModel.Status);
        int noticesAfterCancel = await OnUiAsync(() => viewModel.Notices.Count);

        oldAttempt.Release();

        Assert.Contains(OutdatedAttemptLine + " (", await OnUiUntilAsync(ClientLogText, log => log.Contains(OutdatedAttemptLine + " (", StringComparison.Ordinal)), StringComparison.Ordinal);
        Assert.Equal(statusAfterCancel, await OnUiAsync(() => viewModel.Status));
        Assert.Empty(await NoticesSinceAsync(noticesAfterCancel));
        Assert.DoesNotContain(SoundEvent.Disconnected, await OnUiAsync(() => viewModel.Audio.Sounds.Recent));
    }

    /// <summary>An attempt asks the seam for the server, nickname and password of the connect, and never for a cancellation.</summary>
    [Fact]
    public async Task Reconnect_AfterADrop_ReusesTheChoiceOfTheConnectWithoutCancellation()
    {
        await ConnectToAsync(server, "geheim");
        var connect = seam.Attempts.Single();
        await DropAsync(server);

        reconnectTime.Advance(TimeSpan.FromSeconds(1));

        var attempt = await HangGuard.BoundAsync(seam.WaitForAttemptAsync(connect.Number + 1));
        Assert.Equal(connect.Choice, attempt.Choice);
        Assert.Equal(CancellationToken.None, attempt.Token);
    }

    /// <summary>The last channel is gone or no longer lets me in: I stay in the default channel, with one notice about it.</summary>
    [Theory]
    [InlineData("deleted")]
    [InlineData("password changed")]
    public async Task Reconnect_ChannelGone_StaysInDefault(string change)
    {
        var secret = Guid.NewGuid();
        using var adminId = OVS.Shared.Identity.ClientIdentity.Create();
        await using var own = await TestServer.StartAsync(serverData =>
        {
            SecretChannel(secret)(serverData);
            TestServer.Grant(adminId, "Admin")(serverData);
        });
        await using var admin = await TestClient.ConnectAsync(own, "admin", adminId);
        viewModel.Dialogs = new DialogCallbacks { Server = new() { AskChannelPassword = _ => Task.FromResult<string?>("pw") } };
        await ConnectToAsync(own);
        await OnUiAsync(() => viewModel.Server!.JoinAsync(secret));
        await OnUiUntilAsync(() => viewModel.Server!.Mirror.Self!.ChannelId, id => id == secret);
        var before = await OnUiAsync(() => viewModel.Notices.Count);

        await DropAsync(own);
        if (change == "deleted")
        {
            await admin.SendAsync(new DeleteChannel(secret) { RequestId = "x" });
            await admin.WaitForAsync<ChannelRemoved>(channelRemoved => channelRemoved.ChannelId == secret);
        }
        else
        {
            await admin.SendAsync(new EditChannel(secret, "Geheim", "", 1, Password: "neu") { RequestId = "x" });
            await admin.WaitForAsync<ChannelUpdated>(channelUpdated => channelUpdated.Channel.Id == secret);
        }
        reconnectTime.Advance(TimeSpan.FromSeconds(1));
        var notices = await OnUiUntilAsync(() => viewModel.Notices.Take(viewModel.Notices.Count - before).Select(notice => notice.Text).ToList(), list => list.Count >= 2);
        int shown = notices.Count;
        await AsyncWait.AssertStaysFalseAsync(async () => (await NoticesSinceAsync(before)).Count > shown, TimeSpan.FromMilliseconds(300), "nothing else follows");
        notices = await NoticesSinceAsync(before);

        var (channel, defaultChannel) = await OnUiAsync(() => (viewModel.Server!.Mirror.Self!.ChannelId, viewModel.Server.Mirror.DefaultChannelId));
        Assert.Equal(defaultChannel, channel);
        Assert.Equal(2, notices.Count);
        Assert.Contains(Reconnected, notices);
        Assert.Contains(change == "deleted" ? "Der Channel \"Geheim\" ist nicht mehr da, du bist im Standard-Channel." : "Falsches Channel-Passwort.",
            notices);
    }
}
