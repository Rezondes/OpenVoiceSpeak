using System.ComponentModel;
using System.Net;
using System.Net.Sockets;
using System.Security.Authentication;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using OVS.Client.Audio.Sounds;
using OVS.Client.Dialogs;
using OVS.Client.Exceptions;
using OVS.Client.Localization;
using OVS.Client.Logging;
using OVS.Client.Models;
using OVS.Client.Net;
using OVS.Client.Services;
using OVS.Client.Settings;
using OVS.Shared.Exceptions;
using OVS.Shared.Protocol;
using OVS.Shared.Protocol.Messages.ServerInfo;

namespace OVS.Client.ViewModels;

/// <summary>
/// The connection of the application: connecting, the bookmark click, the session a welcomed connection becomes, the
/// disconnect and the reconnect series after a lost connection (Packages 40, 114 and 139). It owns the current
/// <see cref="ClientConnection"/> and the voice link, shows the server through <c>show</c> and knows the rest of the main view model
/// through delegates.
/// </summary>
internal sealed partial class ConnectionViewModel : ObservableObject
{
    readonly TimeProvider time;
    readonly Action<Action> post;
    readonly ILogger log;
    readonly OpenConnection openConnection;
    readonly Action<SoundEvent> play;
    readonly EngineSync engineSync;
    readonly SelfTest selfTest;
    readonly BookmarkList bookmarks;
    readonly ServerIconCache icons;
    readonly ConnectionStatsViewModel stats;
    readonly Func<string, Task<PasswordAnswer?>> askPassword;
    readonly Func<ServerDialogs> serverDialogs;
    readonly Func<ServerViewModel?> shownServer;
    readonly Action<ServerViewModel?> show;
    readonly Action<string> setStatus;
    readonly Func<string> readStatus;
    readonly Action<string, NoticeKind> addNotice;
    readonly Func<Task<bool>> confirmLeave;
    readonly Action closeAdmin;
    ClientConnection? connection;
    /// <summary>The server of the current session and its logo tracking (Package 30); both null between sessions.</summary>
    ServerViewModel? trackedServer;
    ServerIconTracker? iconTracker;
    /// <summary>Why the last connect attempt was refused, e.g. WrongPassword (Package 40).</summary>
    string? lastRejection;

    [ObservableProperty] bool isConnecting;
    [ObservableProperty] string pingText = "";

    /// <param name="time">The client's one clock: the reconnect series, and every server view model's.</param>
    /// <param name="post">Brings a call back to the UI thread.</param>
    /// <param name="log">Where the connection's lines are written.</param>
    /// <param name="openConnection">Opens the connection of every connect and every reconnect attempt.</param>
    /// <param name="play">Plays a tone: the connection's own and the ones the server view model asks for.</param>
    /// <param name="voiceLink">The voice client of the current connection, the log of whether UDP gets through and the loss adapter's feed.</param>
    /// <param name="engineSync">Follows the shown server's state and the user changes.</param>
    /// <param name="selfTest">Abandoned at a disconnect and asked for the state from before at a reconnect.</param>
    /// <param name="bookmarks">Remembers the server that was connected to.</param>
    /// <param name="icons">The logo cache the icon tracking fills.</param>
    /// <param name="stats">Starts over with every connection.</param>
    /// <param name="askPassword">Asks for the password of a bookmark the server refused; null is cancelled (also without a dialog).</param>
    /// <param name="serverDialogs">What each server view model asks, read at its attach.</param>
    /// <param name="shownServer">The server view model now shown.</param>
    /// <param name="show">Shows a server view model, or none.</param>
    /// <param name="setStatus">Sets the status text of the start card.</param>
    /// <param name="readStatus">Reads that text.</param>
    /// <param name="addNotice">Adds a notice to the list and the chat.</param>
    /// <param name="confirmLeave">Asks before a disconnect or a connect elsewhere that would lose changes in the administration; false keeps the connection.</param>
    /// <param name="closeAdmin">Closes the administration when it is open: it needs a server.</param>
    internal ConnectionViewModel(TimeProvider time, Action<Action> post, ILogger log, OpenConnection openConnection, Action<SoundEvent> play,
        VoiceLink voiceLink, EngineSync engineSync, SelfTest selfTest, BookmarkList bookmarks, ServerIconCache icons, ConnectionStatsViewModel stats,
        Func<string, Task<PasswordAnswer?>> askPassword, Func<ServerDialogs> serverDialogs, Func<ServerViewModel?> shownServer, Action<ServerViewModel?> show, Action<string> setStatus,
        Func<string> readStatus, Action<string, NoticeKind> addNotice, Func<Task<bool>> confirmLeave, Action closeAdmin)
    {
        this.time = time;
        this.post = post;
        this.log = log;
        this.openConnection = openConnection;
        this.play = play;
        VoiceLink = voiceLink;
        this.engineSync = engineSync;
        this.selfTest = selfTest;
        this.bookmarks = bookmarks;
        this.icons = icons;
        this.stats = stats;
        this.askPassword = askPassword;
        this.serverDialogs = serverDialogs;
        this.shownServer = shownServer;
        this.show = show;
        this.setStatus = setStatus;
        this.readStatus = readStatus;
        this.addNotice = addNotice;
        this.confirmLeave = confirmLeave;
        this.closeAdmin = closeAdmin;
    }

    /// <summary>The voice client of the current connection and the log of whether UDP gets through (Packages 126 and 135).</summary>
    internal VoiceLink VoiceLink { get; }

    /// <summary>Package 126: tests send UDP through a lossy relay instead of to the server.</summary>
    internal IPEndPoint? VoiceRelay { get; set; }

    /// <summary>The clock of the current connection's ping round trip (tests check it is the client's one clock).</summary>
    internal TimeProvider? ConnectionTime => connection?.Time;

    public async Task ConnectAsync(ConnectChoice choice)
    {
        if (shownServer() is not null && !await confirmLeave()) return; // Package 114: the administration goes with the server
        await DisconnectAsync();
        lastRejection = null;
        if (choice.SaveBookmark) bookmarks.Remember(choice, passwordConfirmed: false);

        IsConnecting = true;
        try
        {
            Attach(await OpenAsync(choice), choice);
            if (choice.SaveBookmark) bookmarks.Remember(choice, passwordConfirmed: true);
        }
        catch (ConnectionRejectedException exception)
        {
            Refused(exception);
        }
        catch (TofuRejectedException)
        {
            setStatus(Strings.Status_TofuRejected);
        }
        catch (Exception exception) when (IsNetworkFailure(exception))
        {
            setStatus(string.Format(Strings.Status_Failed, exception.Message));
        }
        finally
        {
            IsConnecting = false;
        }
        if (connection is null) log.ConnectFailed(readStatus());
    }

    static bool IsNetworkFailure(Exception exception) =>
        exception is SocketException or IOException or AuthenticationException or OperationCanceledException or ProtocolException;

    void Refused(ConnectionRejectedException exception)
    {
        lastRejection = exception.Code;
        setStatus(string.Format(Strings.Status_Refused, ErrorTexts.For(exception.Code, exception.Detail)));
        log.Refused(exception.Code, string.IsNullOrEmpty(exception.Detail) ? "" : $" ({exception.Detail})");
    }

    async Task<ClientConnection> OpenAsync(ConnectChoice choice)
    {
        setStatus(string.Format(Strings.Status_Connecting, choice.Host, choice.Port));
        log.Connecting(choice.Host, choice.Port, choice.Nickname, choice.PasswordOrNull is null ? "" : " with password");
        return await openConnection(choice, CancellationToken.None);
    }

    /// <summary>A connection the server welcomed becomes the current session.</summary>
    /// <param name="restored">Package 139: a reconnect, which brings back no welcome text.</param>
    /// <param name="conn">The welcomed connection.</param>
    /// <param name="choice">Where and as whom it connected.</param>
    void Attach(ClientConnection conn, ConnectChoice choice, bool restored = false)
    {
        var mirror = new StateMirror(conn.Welcome);
        var server = new ServerViewModel(mirror, request =>
        {
            log.Protocol(ProtocolLogText.Describe(request, mirror));
            return conn.SendAsync(request);
        }, time, serverDialogs()) { Post = post }; // Package 97: the waiting marks' timers come back to the UI thread
        server.Notice += text => addNotice(text, NoticeKind.Error);
        TrackServerIcon(server, choice.Host, choice.Port);
        server.PropertyChanged += OnServerPropertyChanged;
        server.SelfState.PropertyChanged += OnServerPropertyChanged; // the own mute and deafen live on the server's child (Step 27f)
        server.SoundRequested += play; // Package 47
        conn.MessageReceived += message => post(() =>
        {
            if (ProtocolLogText.Describe(message, mirror) is { } line) log.Protocol(line);
            server.Apply(message);
        });
        conn.Disconnected += (reason, detail) => post(() => OnDisconnected(conn, reason, detail));

        var udp = new VoiceClient(VoiceRelay ?? new IPEndPoint(conn.RemoteAddress, choice.Port), conn.Welcome.SessionId,
            conn.VoiceKey, time, conn); // Package 141: falls back to the TLS connection
        // Package 139 (review): owned from here on, so a failure below is torn down with them (DisconnectAsync)
        connection = conn;
        VoiceLink.Start(udp);

        ResetStats();
        show(server);
        setStatus(string.Format(Strings.Status_Connected, server.ServerName));
        log.Connected(server.ServerName, mirror.Self?.Nickname, mirror.SelfId, mirror.Channels.GetValueOrDefault(mirror.Self?.ChannelId ?? Guid.Empty)?.Name, mirror.Users.Count);
        play(SoundEvent.Connected);
        if (!restored && server.WelcomeText.Length > 0) addNotice(server.WelcomeText, NoticeKind.Welcome);
        lastChoice = choice with { SaveBookmark = false }; // Package 139: what a reconnect uses, the password in memory only
    }

    // ---- A click on a bookmark in the sidebar connects (Package 40); the list itself is Bookmarks ----

    /// <summary>One click connects; a missing or wrong password is asked for until it fits or the user gives up.</summary>
    public async Task ConnectBookmarkAsync(Bookmark bookmark)
    {
        var password = bookmark.SavedPassword();
        bool save = password is not null;
        while (true)
        {
            await ConnectAsync(new ConnectChoice(bookmark.Host, bookmark.Port, bookmark.Nickname, password, SaveBookmark: true, SavePassword: save));
            if (lastRejection != ReasonCodes.WrongPassword) return;
            if (await askPassword(bookmark.Name) is not { } answer) return;
            (password, save) = (answer.Password, answer.Save);
        }
    }

    /// <summary>Package 114: the "Trennen" button asks first when the administration has unsaved changes.</summary>
    [RelayCommand]
    async Task ConfirmDisconnectAsync()
    {
        if (!await confirmLeave()) return;
        await DisconnectAsync();
    }

    public Task DisconnectAsync()
    {
        if (connection is not null) log.DisconnectedOwnAction();
        StopReconnect(); // Package 139: also connecting elsewhere and closing the app
        lastChoice = null; // its password goes with the session
        return DisconnectAsync(Strings.Status_NotConnected);
    }

    /// <param name="status">Set before tearing down, so nobody ever sees "disconnected" with a stale status.</param>
    /// <param name="sound">Package 139 (A148): no "Getrennt" tone while a reconnect may still bring the session back.</param>
    async Task DisconnectAsync(string status, bool sound = true)
    {
        closeAdmin(); // administration needs a server
        var conn = connection;
        connection = null;
        if (conn is not null && sound) play(SoundEvent.Disconnected);
        if (conn is not null) setStatus(status);
        if (shownServer() is { } server)
        {
            server.PropertyChanged -= OnServerPropertyChanged;
            server.SelfState.PropertyChanged -= OnServerPropertyChanged;
        }
        StopIconTracking();
        await selfTest.AbandonAsync(); // nothing to restore at a server that is gone
        show(null);
        VoiceLink.Stop();
        engineSync.ClearSpeakers();
        ResetStats();
        if (conn is not null) await conn.DisposeAsync();
    }

    void OnDisconnected(ClientConnection conn, string reason, string? detail)
    {
        if (conn != connection) return;
        var text = string.Format(Strings.Status_Disconnected, ErrorTexts.For(reason, detail));
        if (!string.IsNullOrEmpty(detail)) log.DisconnectedByServer(reason, detail);
        if (reason is ReasonCodes.ConnectionLost or ReasonCodes.ServerShutdown or ReasonCodes.ServerRestart && lastChoice is { } choice && shownServer() is { } server)
        {
            StartReconnect(choice, text, server);
            return;
        }
        lastChoice = null; // no reconnect: its password goes with the session
        addNotice(text, NoticeKind.Warning);
        _ = DisconnectAsync(text);
    }

    // ---- Package 139 (A148): auto-reconnect ----

    /// <summary>The last connection's choice, its password in memory only; a reconnect uses it.</summary>
    ConnectChoice? lastChoice;
    Reconnector? reconnector;
    /// <summary>The disconnected text shown when the series ends without success.</summary>
    string lostText = "";

    /// <summary>A series of attempts runs; the start screen shows the countdown and "Abbrechen".</summary>
    [ObservableProperty] bool isReconnecting;

    void StartReconnect(ConnectChoice choice, string text, ServerViewModel server)
    {
        var self = server.Mirror.Self;
        // the self test mutes and deafens at the server; what counts is the state from before it. A mute that only holds
        // while push-to-mute is held is no choice to keep: the key may be let go meanwhile (Package 125).
        var selfState = selfTest.StateBefore ?? new SelfState(server.SelfState.SelfMuted && !server.SelfState.MutedByHold, server.SelfState.SelfDeafened);
        var restore = new Restore(self?.ChannelId ?? Guid.Empty, server.CurrentChannel?.Name ?? "", self is null ? null : server.PasswordFor(self.ChannelId),
            selfState, server.SelfState.MutedBeforeDeafen);
        log.ConnectionLost(string.Join(", ", Reconnector.Delays.Select(duration => $"{duration.TotalSeconds:0}")));
        lostText = text;
        _ = DisconnectAsync(text, sound: false);
        reconnector ??= new Reconnector(time, post);
        IsReconnecting = true;
        reconnector.Start(generation => ReconnectOnceAsync(generation, choice, restore), GiveUp);
        UpdateReconnectStatus();
    }

    /// <summary>Review: refusals that say "not now" rather than "no": the next attempt may get in.</summary>
    static bool IsPassing(string code) => code is ReasonCodes.ServerShutdown or ReasonCodes.ServerRestart or ReasonCodes.RateLimited;

    async Task<bool> ReconnectOnceAsync(int generation, ConnectChoice choice, Restore restore)
    {
        ClientConnection? conn = null;
        try
        {
            conn = await OpenAsync(choice);
            if (reconnector?.IsCurrent(generation) != true)
            {
                log.ReconnectOutdated();
                await conn.DisposeAsync();
                return false;
            }
            Attach(conn, choice, restored: true);
            var server = shownServer()!;
            if (restore.Channel != Guid.Empty && server.Mirror.Self?.ChannelId != restore.Channel)
            {
                if (server.Mirror.Channels.ContainsKey(restore.Channel)) _ = server.RejoinAsync(restore.Channel, restore.Password);
                else addNotice(string.Format(Strings.Status_ReconnectChannelGone, restore.ChannelName), NoticeKind.Warning);
            }
            // server mute comes from the server (Package 85)
            if (restore.State.IsQuiet) _ = server.SelfState.RestoreSelfStateAsync(restore.State, restore.MutedBeforeDeafen);
            IsReconnecting = false;
            addNotice(Strings.Status_Reconnected, NoticeKind.Info);
            return true;
        }
        catch (Exception exception) when (reconnector?.IsCurrent(generation) != true)
        {
            log.ReconnectOutdatedFailed(exception.Message);
            if (conn is not null) await conn.DisposeAsync();
            return false;
        }
        catch (ConnectionRejectedException exception) when (!IsPassing(exception.Code))
        {
            // the server said no (e.g. a new password): asking again would not change that
            EndSeries(null);
            Refused(exception);
            return false;
        }
        catch (TofuRejectedException)
        {
            EndSeries(Strings.Status_TofuRejected);
            return false;
        }
        catch (Exception exception)
        {
            // review: also the unexpected, so the series goes on instead of hanging at "Verbinde mit ..."
            log.ReconnectFailed(exception.GetType().Name, exception.Message);
            if (conn is not null && conn == connection) await DisconnectAsync(lostText, sound: false); // Attach got that far
            else if (conn is not null) await conn.DisposeAsync();
            return false;
        }
    }

    /// <summary>The fifth attempt failed too: the disconnect as it was before Package 139.</summary>
    void GiveUp()
    {
        log.ReconnectGivenUp();
        EndSeries(lostText);
    }

    /// <summary>"Abbrechen" on the start screen: the series ends at once, an attempt still running is ignored.</summary>
    [RelayCommand]
    void CancelReconnect()
    {
        if (!IsReconnecting) return;
        log.ReconnectCancelled();
        EndSeries(lostText, sound: false);
    }

    /// <summary>The series ends without a session: the disconnected notice, the status, the tone unless the user chose it.</summary>
    /// <param name="status">Null when the caller sets it.</param>
    /// <param name="sound">False keeps the disconnect tone quiet.</param>
    void EndSeries(string? status, bool sound = true)
    {
        StopReconnect();
        lastChoice = null; // review: the server password stays no longer than needed
        if (status is not null) setStatus(status);
        addNotice(lostText, NoticeKind.Warning);
        if (sound) play(SoundEvent.Disconnected);
    }

    void StopReconnect()
    {
        reconnector?.Stop();
        IsReconnecting = false;
    }

    /// <summary>The countdown to the next attempt; while one runs, its "Verbinde mit ..." stays.</summary>
    void UpdateReconnectStatus()
    {
        if (!IsReconnecting || reconnector?.NextAttemptAt is not { } at) return;
        var left = Math.Max(1, (int)Math.Ceiling((at - time.GetUtcNow()).TotalSeconds));
        setStatus(string.Format(Strings.Status_Reconnecting, left));
    }

    void OnServerPropertyChanged(object? sender, PropertyChangedEventArgs args) => engineSync.SyncConnection();

    /// <summary>The countdown of the reconnect series and the ping text, on every tick of the main view model.</summary>
    internal void Tick()
    {
        UpdateReconnectStatus(); // Package 139
        PingText = connection?.LastRoundTrip is { } rtt
            ? $"Ping {rtt.TotalMilliseconds:0} ms" + (VoiceLink.OverTcp ? Strings.Ping_VoiceOverTcp // Package 141
                : VoiceLink.Reachable == false ? Strings.Ping_UdpUnreachable : "")
            : "";
    }

    /// <summary>A new connection, or none: the figures start over.</summary>
    void ResetStats() => stats.Reset();

    /// <summary>
    /// Keeps the logo in step with the server's hash (Package 30): wires the server to its <see cref="ServerIconTracker"/>,
    /// which shows it from the cache when it matches, otherwise asks for it once and caches it for next time and for the bookmark tile.
    /// </summary>
    internal ServerIconTracker TrackServerIcon(ServerViewModel serverViewModel, string host, int port)
    {
        var tracker = new ServerIconTracker(
            cache: icons, host: host, port: port, time: serverViewModel.Time,
            serverHash: () => serverViewModel.IconHash,
            shownLogo: () => serverViewModel.IconPng,
            show: png => serverViewModel.IconPng = png,
            ask: route =>
            {
                serverViewModel.Routes.RemoveAll(route);
                _ = serverViewModel.SendAsync(new GetServerIcon(), route: route);
            },
            isCurrent: () => shownServer() == serverViewModel,
            post: post,
            logoCached: bookmarks.Refresh);
        StopIconTracking();
        (trackedServer, iconTracker) = (serverViewModel, tracker);
        serverViewModel.StateChanged += tracker.Sync;
        serverViewModel.IconReceived += tracker.Receive;
        tracker.Sync();
        return tracker;
    }

    /// <summary>The session's logo tracking ends with it: the old server's later changes and logos are no business of the next session.</summary>
    void StopIconTracking()
    {
        if (iconTracker is { } tracker && trackedServer is { } server)
        {
            server.StateChanged -= tracker.Sync;
            server.IconReceived -= tracker.Receive;
        }
        (trackedServer, iconTracker) = (null, null);
    }

    /// <summary>
    /// What comes back after a reconnect: the channel with the password that worked there, the own mute and deafen, and
    /// whether undeafening unmutes (review: muted, then deafened, stays muted).
    /// </summary>
    sealed record Restore(Guid Channel, string ChannelName, string? Password, SelfState State, bool MutedBeforeDeafen);
}
