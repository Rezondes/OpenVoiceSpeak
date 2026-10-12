using System.Security.Authentication;
using OVS.Client.Audio.Sounds;
using OVS.Client.Dialogs;
using OVS.Client.Exceptions;
using OVS.Client.Localization;
using OVS.Client.Models;
using OVS.Client.Settings;
using OVS.Client.ViewModels;
using OVS.Shared.Exceptions;
using OVS.Shared.Protocol;
using OVS.Shared.Protocol.Messages;
using OVS.Shared.Protocol.Messages.Connection;
using OVS.Shared.Protocol.Messages.ServerInfo;
using OVS.Tests.TestSupport;

namespace OVS.Tests.Client.ViewModels;

/// <summary>
/// Packages 40, 114 and 139: the connection's own behaviour (connect, bookmark connect, disconnect, the reconnect series, the
/// ping text), without a main view model. The refusals and failures of a connect are scripted on the connect seam; a connected
/// session needs a real server. The series through the main view model is in <see cref="ReconnectTests"/>.
/// </summary>
public sealed class ConnectionViewModelTests() : TempDirectoryTest("ovs-connvm-", quiet: false), IAsyncLifetime
{
    static readonly ConnectChoice Offline = new("127.0.0.1", 1, "anna", null, SaveBookmark: false);

    ConnectionRig rig = null!;

    public Task InitializeAsync()
    {
        rig = new ConnectionRig(dir);
        return Task.CompletedTask;
    }

    public async Task DisposeAsync() => await rig.DisposeAsync();

    static ConnectChoice ChoiceFor(TestServer server) => new("127.0.0.1", server.Port, "anna", null, SaveBookmark: false);

    static string ConnectingStatus(ConnectChoice choice) => string.Format(Strings.Status_Connecting, choice.Host, choice.Port);

    Task<List<string>> LogAsync() => rig.ReadAsync(() => rig.LogLines.ToList());

    Task<string> StatusAsync() => rig.ReadAsync(() => rig.Status);

    Task<bool> IsConnectingAsync() => rig.ReadAsync(() => rig.Connection.IsConnecting);

    static Exception CreateNetworkFailure(string kind) => kind switch
    {
        nameof(IOException) => new IOException("Testmeldung"),
        nameof(AuthenticationException) => new AuthenticationException("Testmeldung"),
        nameof(OperationCanceledException) => new OperationCanceledException("Testmeldung"),
        nameof(ProtocolException) => new ProtocolException("Testmeldung"),
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "not a failure of the network"),
    };

    static Exception CreateUnexpectedFailure(string kind) => kind switch
    {
        nameof(InvalidOperationException) => new InvalidOperationException("Testmeldung"),
        nameof(ArgumentOutOfRangeException) => new ArgumentOutOfRangeException("port", "Testmeldung"),
        nameof(NullReferenceException) => new NullReferenceException("Testmeldung"),
        nameof(TimeoutException) => new TimeoutException("Testmeldung"),
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "not an unexpected failure"),
    };

    // ---- ConnectAsync: what the end of an attempt says ----

    [Fact]
    public async Task ConnectAsync_CertificateRefused_StatusSaysTofuRejected()
    {
        rig.Seam.FailNext(new TofuRejectedException());

        await rig.ConnectAsync(Offline);

        Assert.Equal(Strings.Status_TofuRejected, await StatusAsync());
        Assert.Contains(Strings.Status_TofuRejected, await LogAsync());
        Assert.False(await IsConnectingAsync());
        Assert.Null(await rig.ReadAsync(() => rig.Shown));
    }

    [Fact]
    public async Task ConnectAsync_PortClosed_StatusSaysFailedWithTheMessage()
    {
        var refused = ConnectSeam.CreateClosedPortError();
        rig.Seam.FailNext(refused);

        await rig.ConnectAsync(Offline);

        var expected = string.Format(Strings.Status_Failed, refused.Message);
        Assert.Equal(expected, await StatusAsync());
        Assert.Contains(expected, await LogAsync());
        Assert.False(await IsConnectingAsync());
    }

    [Theory]
    [InlineData(nameof(IOException))]
    [InlineData(nameof(AuthenticationException))]
    [InlineData(nameof(OperationCanceledException))]
    [InlineData(nameof(ProtocolException))]
    public async Task ConnectAsync_NetworkFailure_StatusSaysFailedWithTheMessage(string kind)
    {
        rig.Seam.FailNext(CreateNetworkFailure(kind));

        await rig.ConnectAsync(Offline);

        Assert.Equal(string.Format(Strings.Status_Failed, "Testmeldung"), await StatusAsync());
        Assert.False(await IsConnectingAsync());
    }

    [Fact]
    public async Task ConnectAsync_OpenThrowsBeforeItReturnsATask_StatusSaysFailedWithTheMessage()
    {
        rig.OpenOverride = (_, _) => throw new IOException("Sofort");

        await rig.ConnectAsync(Offline);

        Assert.Equal(string.Format(Strings.Status_Failed, "Sofort"), await StatusAsync());
        Assert.False(await IsConnectingAsync());
    }

    [Theory]
    [InlineData(nameof(InvalidOperationException))]
    [InlineData(nameof(ArgumentOutOfRangeException))]
    [InlineData(nameof(NullReferenceException))]
    [InlineData(nameof(TimeoutException))]
    public async Task ConnectAsync_SeamThrowsUnexpected_PropagatesAndClearsIsConnecting(string kind)
    {
        var failure = CreateUnexpectedFailure(kind);
        rig.Seam.FailNext(failure);

        var thrown = await Assert.ThrowsAnyAsync<Exception>(() => rig.ConnectAsync(Offline));

        Assert.Same(failure, thrown);
        Assert.False(await IsConnectingAsync());
        Assert.Equal(ConnectingStatus(Offline), await StatusAsync());
    }

    [Theory]
    [InlineData(null, "")]
    [InlineData("", "")]
    [InlineData("Zu viele Versuche", " (Zu viele Versuche)")]
    public async Task ConnectAsync_Refused_LogsCodeAndDetail(string? detail, string loggedDetail)
    {
        rig.Seam.FailNext(new ConnectionRejectedException(ReasonCodes.WrongPassword, detail));

        await rig.ConnectAsync(Offline);

        Assert.Contains($"Refused by the server: {ReasonCodes.WrongPassword}{loggedDetail}", await LogAsync());
        Assert.Equal(string.Format(Strings.Status_Refused, ErrorTexts.For(ReasonCodes.WrongPassword, detail)), await StatusAsync());
        Assert.False(await IsConnectingAsync());
    }

    [Fact]
    public async Task ConnectAsync_AttemptStillRunning_IsConnectingUntilItEnds()
    {
        var running = rig.Seam.HoldNext(failWith: ConnectSeam.CreateClosedPortError());
        var connecting = rig.ConnectAsync(Offline);
        await HangGuard.Bound(running.Reached);

        Assert.True(await IsConnectingAsync());
        Assert.Equal(ConnectingStatus(Offline), await StatusAsync());

        running.Release();
        await HangGuard.Bound(connecting);
        Assert.False(await IsConnectingAsync());
    }

    // ---- ConnectAsync: what goes in ----

    [Theory]
    [InlineData("", 0)]
    [InlineData("", 65536)]
    [InlineData("  ", -1)]
    public async Task ConnectAsync_EmptyHostOrPortOutOfRange_HandsTheChoiceToTheSeamUnchanged(string host, int port)
    {
        rig.Seam.FailNext(new ArgumentException("Testmeldung"));
        var choice = new ConnectChoice(host, port, "anna", null, SaveBookmark: false);

        await Assert.ThrowsAnyAsync<ArgumentException>(() => rig.ConnectAsync(choice));

        Assert.Equal(choice, Assert.Single(rig.Seam.Attempts).Choice);
        Assert.False(await IsConnectingAsync());
    }

    [Theory]
    [InlineData(null, "")]
    [InlineData("", "")]
    [InlineData("geheim", " with password")]
    public async Task ConnectAsync_PasswordNullEmptyOrGiven_LogsWithPasswordOnlyForAGivenOneAndNeverIt(string? password, string loggedPassword)
    {
        rig.Seam.FailNext(ConnectSeam.CreateClosedPortError());

        await rig.ConnectAsync(new ConnectChoice("127.0.0.1", 1, "anna", password, SaveBookmark: false));

        var log = await LogAsync();
        Assert.Contains($"Connecting to 127.0.0.1:1 as anna{loggedPassword}", log);
        Assert.DoesNotContain(log, line => line.Contains("geheim"));
    }

    [Fact]
    public async Task ConnectAsync_SaveBookmarkAndTheAttemptFails_RemembersTheServerWithoutThePassword()
    {
        rig.Seam.FailNext(ConnectSeam.CreateClosedPortError());

        await rig.ConnectAsync(new ConnectChoice("127.0.0.1", 4711, "anna", "geheim", SaveBookmark: true, SavePassword: true));

        var saved = Assert.Single(await rig.ReadAsync(() => rig.Bookmarks.ToList()));
        Assert.Equal("127.0.0.1", saved.Host);
        Assert.Equal(4711, saved.Port);
        Assert.Null(saved.ProtectedPassword);
    }

    // ---- ConnectAsync: the question before leaving a server ----

    [Fact]
    public async Task ConnectAsync_NothingShown_AsksNothingAndMakesTheAttempt()
    {
        int asked = 0;
        rig.ConfirmLeave = () =>
        {
            asked++;
            return Task.FromResult(true);
        };
        rig.Seam.FailNext(ConnectSeam.CreateClosedPortError());

        await rig.ConnectAsync(Offline);

        Assert.Equal(0, asked);
        Assert.Single(rig.Seam.Attempts);
    }

    [Fact]
    public async Task ConnectAsync_ConfirmLeaveRefuses_StartsNoAttemptAndKeepsTheShownServer()
    {
        var shown = FakeServers.Admin();
        rig.Shown = shown;
        int asked = 0;
        rig.ConfirmLeave = () =>
        {
            asked++;
            return Task.FromResult(false);
        };

        await rig.ConnectAsync(Offline);

        Assert.Equal(1, asked);
        Assert.Empty(rig.Seam.Attempts);
        Assert.Same(shown, await rig.ReadAsync(() => rig.Shown));
        Assert.Equal(0, await rig.ReadAsync(() => rig.AdminClosedCount));
        Assert.False(await IsConnectingAsync());
    }

    [Fact]
    public async Task ConnectAsync_ConfirmLeaveThrows_PropagatesAndStartsNoAttempt()
    {
        var shown = FakeServers.Admin();
        rig.Shown = shown;
        rig.ConfirmLeave = () => throw new InvalidOperationException("Frage kaputt");

        await Assert.ThrowsAsync<InvalidOperationException>(() => rig.ConnectAsync(Offline));

        Assert.Empty(rig.Seam.Attempts);
        Assert.Same(shown, await rig.ReadAsync(() => rig.Shown));
        Assert.False(await IsConnectingAsync());
    }

    // ---- ConnectBookmarkAsync ----

    static Bookmark BookmarkWithSavedPassword(string? password) =>
        new("Gilde", "127.0.0.1", 4711, "anna", password is null ? null : PasswordProtector.Protect(password));

    Task ConnectBookmarkAsync(Bookmark bookmark) => rig.RunAsync(() => rig.Connection.ConnectBookmarkAsync(bookmark));

    [Fact]
    public async Task ConnectBookmarkAsync_WrongPasswordAndNoAskPasswordDialog_StopsAfterOneAttempt()
    {
        rig.Seam.FailNext(new ConnectionRejectedException(ReasonCodes.WrongPassword, null));

        await ConnectBookmarkAsync(BookmarkWithSavedPassword("veraltet"));

        Assert.Single(rig.Seam.Attempts);
        Assert.Equal(string.Format(Strings.Status_Refused, ErrorTexts.For(ReasonCodes.WrongPassword)), await StatusAsync());
    }

    [Fact]
    public async Task ConnectBookmarkAsync_WrongPasswordAndUserCancels_AsksOnceStopsAfterOneAttemptAndKeepsTheRefusedStatus()
    {
        var asked = new List<string>();
        rig.Dialogs = new DialogCallbacks
        {
            AskPassword = name =>
            {
                asked.Add(name);
                return Task.FromResult<PasswordAnswer?>(null);
            },
        };
        rig.Seam.FailNext(new ConnectionRejectedException(ReasonCodes.WrongPassword, null));

        await ConnectBookmarkAsync(BookmarkWithSavedPassword(null));

        Assert.Equal(["Gilde"], asked);
        Assert.Single(rig.Seam.Attempts);
        Assert.StartsWith("Abgelehnt", await StatusAsync());
        Assert.Null(await rig.ReadAsync(() => rig.Shown));
    }

    [Fact]
    public async Task ConnectBookmarkAsync_WrongPasswordAndUserAnswers_TriesAgainWithTheAnswerAndItsSaveChoice()
    {
        rig.Dialogs = new DialogCallbacks { AskPassword = _ => Task.FromResult<PasswordAnswer?>(new PasswordAnswer("neu", Save: true)) };
        rig.Seam.FailNext(new ConnectionRejectedException(ReasonCodes.WrongPassword, null));
        rig.Seam.FailNext(new TofuRejectedException());

        await HangGuard.Bound(ConnectBookmarkAsync(BookmarkWithSavedPassword("veraltet")));

        Assert.Equal(2, rig.Seam.Attempts.Count);
        var first = rig.Seam.Attempts[0].Choice;
        Assert.Equal(("veraltet", true, true), (first.Password, first.SavePassword, first.SaveBookmark));
        var second = rig.Seam.Attempts[1].Choice;
        Assert.Equal(("neu", true, true), (second.Password, second.SavePassword, second.SaveBookmark));
    }

    [Fact]
    public async Task ConnectBookmarkAsync_AskPasswordThrows_PropagatesAfterOneAttempt()
    {
        rig.Dialogs = new DialogCallbacks { AskPassword = _ => throw new InvalidOperationException("Dialog kaputt") };
        rig.Seam.FailNext(new ConnectionRejectedException(ReasonCodes.WrongPassword, null));

        await Assert.ThrowsAsync<InvalidOperationException>(() => ConnectBookmarkAsync(BookmarkWithSavedPassword(null)));

        Assert.Single(rig.Seam.Attempts);
        Assert.False(await IsConnectingAsync());
    }

    [Fact]
    public async Task ConnectBookmarkAsync_SavedPasswordUnreadable_ConnectsWithoutPasswordAndDoesNotSaveOne()
    {
        rig.Seam.FailNext(ConnectSeam.CreateClosedPortError());

        await ConnectBookmarkAsync(new Bookmark("Gilde", "127.0.0.1", 4711, "anna", "kein-gueltiger-wert"));

        var choice = Assert.Single(rig.Seam.Attempts).Choice;
        Assert.Null(choice.Password);
        Assert.False(choice.SavePassword);
    }

    // ---- Disconnect ----

    [Fact]
    public async Task DisconnectAsync_NothingConnected_LogsNothingPlaysNothingAndKeepsTheStatusButClosesTheAdministration()
    {
        rig.Seam.FailNext(new TofuRejectedException());
        await rig.ConnectAsync(Offline);
        int endedBefore = await rig.ReadAsync(() => rig.LossStreaksEndedCount);
        int closedBefore = await rig.ReadAsync(() => rig.AdminClosedCount);

        await rig.DisconnectAsync();

        Assert.DoesNotContain("Disconnected (own action)", await LogAsync());
        Assert.Empty(await rig.ReadAsync(() => rig.Played.ToList()));
        Assert.Equal(Strings.Status_TofuRejected, await StatusAsync());
        Assert.Equal(closedBefore + 1, await rig.ReadAsync(() => rig.AdminClosedCount));
        Assert.Equal(endedBefore + 1, await rig.ReadAsync(() => rig.LossStreaksEndedCount));
    }

    [Fact]
    public async Task DisconnectAsync_EndLossStreaksThrows_PropagatesAfterTheServerIsHiddenAndTheVoiceClientForgotten()
    {
        await using var server = await TestServer.StartAsync();
        await rig.ConnectAsync(ChoiceFor(server));
        int closedBefore = await rig.ReadAsync(() => rig.AdminClosedCount);
        rig.OnEndLossStreaks = () => throw new InvalidOperationException("Adapter broken");
        try
        {
            await Assert.ThrowsAsync<InvalidOperationException>(rig.DisconnectAsync);
            Assert.Equal((closedBefore + 1, true, true), (await rig.ReadAsync(() => rig.AdminClosedCount), await rig.ReadAsync(() => rig.Shown) is null,
                await rig.ReadAsync(() => rig.Connection.VoiceLink.Client) is null));
        }
        finally
        {
            rig.OnEndLossStreaks = null; // the rig's own disconnect at the end must not throw again
        }
    }

    [Fact]
    public async Task DisconnectCommand_ConfirmLeaveRefuses_KeepsTheConnection()
    {
        await using var server = await TestServer.StartAsync();
        await rig.ConnectAsync(ChoiceFor(server));
        rig.ConfirmLeave = () => Task.FromResult(false);

        await rig.RunAsync(() => rig.Connection.DisconnectCommand.ExecuteAsync(null));

        Assert.NotNull(await rig.ReadAsync(() => rig.Shown));
        Assert.NotNull(await rig.ReadAsync(() => rig.Connection.ConnectionTime));
        Assert.DoesNotContain(SoundEvent.Disconnected, await rig.ReadAsync(() => rig.Played.ToList()));
    }

    [Fact]
    public async Task DisconnectAsync_Connected_LogsPlaysTheToneSaysNotConnectedAndEndsTheSession()
    {
        await using var server = await TestServer.StartAsync();
        await rig.ConnectAsync(ChoiceFor(server));
        int closedBefore = await rig.ReadAsync(() => rig.AdminClosedCount);

        await rig.DisconnectAsync();

        Assert.Contains("Disconnected (own action)", await LogAsync());
        Assert.Contains(SoundEvent.Disconnected, await rig.ReadAsync(() => rig.Played.ToList()));
        Assert.Equal(Strings.Status_NotConnected, await StatusAsync());
        Assert.Equal(closedBefore + 1, await rig.ReadAsync(() => rig.AdminClosedCount));
        Assert.Null(await rig.ReadAsync(() => rig.Shown));
        Assert.Null(await rig.ReadAsync(() => rig.Connection.ConnectionTime));
        await AsyncWait.Until(() => server.State.SessionCount == 0);
    }

    // ---- The server logo tracking ends with the session ----

    Task<int> LogoRequestsAsync() => rig.ReadAsync(() => rig.LogLines.Count(line => line.Contains("Request server logo")));

    Task ApplyAsync(ServerViewModel server, Message message) => rig.InvokeAsync(() => server.Apply(message));

    static Message SettingsWithLogoHash(ServerViewModel server, string hash) => new ServerSettingsChanged(server.Mirror.Settings with { IconHash = hash });

    [Fact]
    public async Task ConnectAsync_ConnectedAgain_OnlyTheNewServerAsksForALogo()
    {
        await using var server = await TestServer.StartAsync();
        await rig.ConnectAsync(ChoiceFor(server));
        var oldServer = (await rig.ReadAsync(() => rig.Shown))!;
        await rig.ConnectAsync(ChoiceFor(server));
        var newServer = (await rig.ReadAsync(() => rig.Shown))!;
        int requestsBefore = await LogoRequestsAsync();

        await ApplyAsync(oldServer, SettingsWithLogoHash(oldServer, "old"));
        Assert.Equal(requestsBefore, await LogoRequestsAsync());
        await ApplyAsync(newServer, SettingsWithLogoHash(newServer, "new"));

        Assert.Equal(requestsBefore + 1, await LogoRequestsAsync());
    }

    // ---- 25h4b: a malformed challenge or voice key is a failed connect ----

    static ConnectChoice ChoiceFor(HandshakeStub stub) => new("127.0.0.1", stub.Port, "anna", null, SaveBookmark: false);

    [Fact]
    public async Task ConnectAsync_ServerSendsAMalformedChallenge_StatusSaysFailedLogsItAndThrowsNothing()
    {
        await using var stub = HandshakeStub.Start(dir, new Challenge("this is not base64"));

        await HangGuard.Bound(rig.ConnectAsync(ChoiceFor(stub)));

        var expected = string.Format(Strings.Status_Failed, "Server sent a malformed challenge");
        Assert.Equal(expected, await StatusAsync());
        Assert.Contains(expected, await LogAsync());
        Assert.False(await IsConnectingAsync());
    }

    [Fact]
    public async Task ConnectAsync_ServerSendsAMalformedVoiceKey_StatusSaysFailedLogsItAndThrowsNothing()
    {
        await using var stub = HandshakeStub.Start(dir, HandshakeStub.WelcomeWith("this is not base64"));

        await HangGuard.Bound(rig.ConnectAsync(ChoiceFor(stub)));

        var expected = string.Format(Strings.Status_Failed, "Server sent a malformed voice key");
        Assert.Equal(expected, await StatusAsync());
        Assert.Contains(expected, await LogAsync());
        Assert.False(await IsConnectingAsync());
        Assert.Null(await rig.ReadAsync(() => rig.Shown));
    }

    [Fact]
    public async Task ConnectAsync_ServerSendsAMalformedVoiceKey_ClosesTheConnection()
    {
        await using var stub = HandshakeStub.Start(dir, HandshakeStub.WelcomeWith("this is not base64"));

        await HangGuard.Bound(rig.ConnectAsync(ChoiceFor(stub)));

        await HangGuard.Bound(stub.ClientClosed);
        Assert.Null(await rig.ReadAsync(() => rig.Connection.ConnectionTime));
    }

    // ---- The reconnect series ----

    [Fact]
    public async Task Reconnect_AttemptGetsAMalformedVoiceKey_CountsAsAFailedAttemptAndTheNextConnects()
    {
        await using var server = await TestServer.StartAsync();
        await using var stub = HandshakeStub.Start(dir, HandshakeStub.WelcomeWith("this is not base64"));
        await rig.ConnectAsync(ChoiceFor(server));
        var session = await rig.ReadAsync(() => rig.Shown!.Mirror.SelfId);
        rig.OpenOverride = (choice, token) =>
        {
            rig.OpenOverride = null; // only the next attempt goes to the stub
            return rig.Seam.Connect(choice with { Port = stub.Port }, token);
        };
        server.State.FindSession(session)!.Outbox.Abort("Test");
        Assert.True(await rig.ReadUntilAsync(() => rig.Connection.IsReconnecting, reconnecting => reconnecting));

        rig.Time.Advance(TimeSpan.FromSeconds(1));
        await HangGuard.Bound(rig.Seam.WaitForAttemptAsync(2));

        Assert.True(await rig.ReadUntilAsync(() => rig.LogLines.Any(line => line.StartsWith("Reconnect failed: ")), logged => logged));
        await HangGuard.Bound(stub.ClientClosed);
        Assert.True(await rig.ReadAsync(() => rig.Connection.IsReconnecting));

        rig.Time.Advance(TimeSpan.FromSeconds(2));
        Assert.True(await rig.ReadUntilAsync(() => rig.Shown is not null && !rig.Connection.IsReconnecting, back => back));
        Assert.Equal(3, rig.Seam.Attempts.Count);
    }

    [Fact]
    public async Task CancelReconnect_NotReconnecting_DoesNothing()
    {
        await rig.InvokeAsync(() => rig.Connection.CancelReconnectCommand.Execute(null));

        Assert.DoesNotContain("Reconnect cancelled", await LogAsync());
        Assert.Empty(await rig.ReadAsync(() => rig.Notices.ToList()));
        Assert.Empty(await rig.ReadAsync(() => rig.Played.ToList()));
        Assert.Equal(Strings.Status_NotConnected, await StatusAsync());
    }

    [Fact]
    public async Task OnDisconnected_OldConnectionReportsLate_IsIgnored()
    {
        await using var server = await TestServer.StartAsync();
        await rig.ConnectAsync(ChoiceFor(server));
        var oldConnection = rig.Opened[0];
        var reportedByOld = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        oldConnection.Disconnected += (_, _) => reportedByOld.TrySetResult(); // after the view model's own handler, which posted its call
        var oldSession = await rig.ReadAsync(() => rig.Shown!.Mirror.SelfId);
        rig.HoldPosts();
        server.State.FindSession(oldSession)!.Outbox.Abort("Test");
        await HangGuard.Bound(reportedByOld.Task);
        await rig.ConnectAsync(ChoiceFor(server)); // the user connected again before the old report was handled
        var shownNow = await rig.ReadAsync(() => rig.Shown);
        var statusNow = await StatusAsync();
        int noticesNow = await rig.ReadAsync(() => rig.Notices.Count);

        await rig.ReleasePostsAsync();

        Assert.Same(shownNow, await rig.ReadAsync(() => rig.Shown));
        Assert.Equal(statusNow, await StatusAsync());
        Assert.False(await rig.ReadAsync(() => rig.Connection.IsReconnecting));
        Assert.NotNull(await rig.ReadAsync(() => rig.Connection.ConnectionTime));
        Assert.Equal(noticesNow, await rig.ReadAsync(() => rig.Notices.Count));
        Assert.DoesNotContain(NoticeKind.Warning, (await rig.ReadAsync(() => rig.Notices.ToList())).Select(notice => notice.Kind));
    }

    [Fact]
    public async Task Reconnect_ShownServerGoneBeforeTheDrop_NoSeries()
    {
        await using var server = await TestServer.StartAsync();
        await rig.ConnectAsync(ChoiceFor(server));
        var session = await rig.ReadAsync(() => rig.Shown!.Mirror.SelfId);
        await rig.ReadAsync(() => rig.Shown = null); // the owner took the server away already

        server.State.FindSession(session)!.Outbox.Abort("Test");

        var notices = await rig.ReadUntilAsync(() => rig.Notices.ToList(), found => found.Count > 0);
        Assert.Equal(NoticeKind.Warning, Assert.Single(notices).Kind);
        Assert.False(await rig.ReadAsync(() => rig.Connection.IsReconnecting));
        rig.Time.Advance(TimeSpan.FromSeconds(60));
        await rig.ReadAsync(() => 0); // what the clock posted has run
        Assert.Single(rig.Seam.Attempts);
    }

    [Fact]
    public async Task Attach_DialogsReplacedAfterTheAttach_TheAttachedServerKeepsTheRecordOfItsAttach()
    {
        await using var server = await TestServer.StartAsync();
        var first = new DialogCallbacks { Server = new() { Confirm = _ => Task.FromResult(true) } };
        rig.Dialogs = first;
        await rig.ConnectAsync(ChoiceFor(server));

        rig.Dialogs = new DialogCallbacks { Server = new() { Confirm = _ => Task.FromResult(false) } };

        Assert.Same(first.Server, await rig.ReadAsync(() => rig.Shown!.Dialogs));
    }

    [Fact]
    public async Task Reconnect_AttachFailsBeforeOwningTheConnection_ClosesItAndContinues()
    {
        await using var server = await TestServer.StartAsync();
        await rig.ConnectAsync(ChoiceFor(server));
        var session = await rig.ReadAsync(() => rig.Shown!.Mirror.SelfId);
        rig.OnDialogs = () => throw new InvalidOperationException("Dialoge kaputt"); // asked for first in the attach
        server.State.FindSession(session)!.Outbox.Abort("Test");
        Assert.True(await rig.ReadUntilAsync(() => rig.Connection.IsReconnecting, reconnecting => reconnecting));

        rig.Time.Advance(TimeSpan.FromSeconds(1));
        await HangGuard.Bound(rig.Seam.WaitForAttemptAsync(2));

        Assert.True(await rig.ReadUntilAsync(() => rig.LogLines.Contains("Reconnect failed: InvalidOperationException: Dialoge kaputt"), logged => logged));
        await AsyncWait.Until(() => server.State.SessionCount == 0); // the connection of the failed attempt was closed
        Assert.Null(await rig.ReadAsync(() => rig.Shown));
        Assert.True(await rig.ReadAsync(() => rig.Connection.IsReconnecting));

        rig.OnDialogs = null;
        rig.Time.Advance(TimeSpan.FromSeconds(2));
        Assert.True(await rig.ReadUntilAsync(() => rig.Shown is not null && !rig.Connection.IsReconnecting, back => back));
    }

    [Fact]
    public async Task Reconnect_AttachFailsAfterOwningTheConnection_TearsItDownAndContinues()
    {
        await using var server = await TestServer.StartAsync();
        await rig.ConnectAsync(ChoiceFor(server));
        var session = await rig.ReadAsync(() => rig.Shown!.Mirror.SelfId);
        rig.OnPlay = sound =>
        {
            if (sound == SoundEvent.Connected) throw new InvalidOperationException("Testfehler"); // the last step of the attach
        };
        server.State.FindSession(session)!.Outbox.Abort("Test");
        Assert.True(await rig.ReadUntilAsync(() => rig.Connection.IsReconnecting, reconnecting => reconnecting));

        rig.Time.Advance(TimeSpan.FromSeconds(1));
        await HangGuard.Bound(rig.Seam.WaitForAttemptAsync(2));

        Assert.True(await rig.ReadUntilAsync(() => rig.LogLines.Contains("Reconnect failed: InvalidOperationException: Testfehler"), logged => logged));
        Assert.True(await rig.ReadUntilAsync(() => rig.Shown is null && rig.Connection.ConnectionTime is null && rig.Connection.VoiceLink.Client is null,
            tornDown => tornDown));
        Assert.True(await rig.ReadAsync(() => rig.Connection.IsReconnecting));
        Assert.DoesNotContain(SoundEvent.Disconnected, await rig.ReadAsync(() => rig.Played.ToList()));

        rig.OnPlay = null;
        rig.Time.Advance(TimeSpan.FromSeconds(2));
        Assert.True(await rig.ReadUntilAsync(() => rig.Shown is not null && !rig.Connection.IsReconnecting, back => back));
        Assert.Equal(3, rig.Seam.Attempts.Count);
    }

    // ---- Tick: the ping text ----

    [Fact]
    public async Task Tick_NothingConnected_PingTextIsEmptyAndTheStatusStays()
    {
        await rig.InvokeAsync(rig.Connection.Tick);

        Assert.Equal("", await rig.ReadAsync(() => rig.Connection.PingText));
        Assert.Equal(Strings.Status_NotConnected, await StatusAsync());
    }

    [Fact]
    public async Task Tick_UdpUnansweredForLessThanFiveSeconds_PingTextSaysUdpNotReachable()
    {
        await using var server = await TestServer.StartAsync();
        using var relay = new LossyUdpRelay(server.VoiceEndPoint) { DropAll = true };
        rig.Connection.VoiceRelay = relay.EndPoint;
        await rig.ConnectAsync(ChoiceFor(server));

        var ping = await rig.ReadUntilAsync(() =>
        {
            rig.Connection.Tick();
            return rig.Connection.PingText;
        }, text => text.Length > 0);

        Assert.EndsWith(Strings.Ping_UdpUnreachable, ping);
    }

    /// <summary>The ping text names voice over TCP while it is in use, and drops it once UDP answers again.</summary>
    [Fact]
    public async Task Tick_VoiceOverTcpInUse_PingTextNamesItUntilUdpAnswersAgain()
    {
        await using var server = await TestServer.StartAsync();
        using var relay = new LossyUdpRelay(server.VoiceEndPoint) { DropAll = true };
        rig.Connection.VoiceRelay = relay.EndPoint;
        await rig.ConnectAsync(ChoiceFor(server));
        string Ping()
        {
            rig.Connection.Tick();
            return rig.Connection.PingText;
        }
        var overTcp = Strings.Ping_VoiceOverTcp;

        for (int halfSecond = 0; halfSecond < 10; halfSecond++) rig.Time.Advance(TimeSpan.FromMilliseconds(500)); // 5 s without an answer
        Assert.EndsWith(overTcp, await rig.ReadUntilAsync(Ping, text => text.Length > 0));
        relay.DropAll = false;
        for (int halfSecond = 0; halfSecond < 4; halfSecond++) rig.Time.Advance(TimeSpan.FromMilliseconds(500)); // the next Hello
        var ping = await rig.ReadUntilAsync(Ping, text => text.Length > 0 && !text.EndsWith(overTcp));
        Assert.DoesNotContain(Strings.Ping_UdpUnreachable, ping);
    }
}
