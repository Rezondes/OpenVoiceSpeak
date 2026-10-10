using OVS.Client.Collections;
using OVS.Client.Dialogs;
using OVS.Client.Localization;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Net;
using System.Net.Sockets;
using System.Security.Authentication;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OVS.Client.Audio;
using OVS.Client.Audio.Devices;
using OVS.Client.Audio.Processing;
using OVS.Client.Audio.Sounds;
using OVS.Client.Audio.Transmit;
using OVS.Client.Exceptions;
using OVS.Client.Input;
using OVS.Client.Logging;
using OVS.Client.Models;
using OVS.Client.Net;
using OVS.Client.Services;
using OVS.Client.Settings;
using OVS.Shared.Exceptions;
using OVS.Shared.Identity;
using OVS.Shared.Protocol;
using OVS.Shared.Protocol.Messages;
using OVS.Shared.Protocol.Messages.Connection;
using OVS.Shared.Protocol.Messages.ServerInfo;

namespace OVS.Client.ViewModels;

/// <summary>Application shell: settings, audio, and the current connection.</summary>
public sealed partial class MainViewModel : ObservableObject, IAsyncDisposable
{
    readonly string storageDir;
    readonly Action<Action> post;
    readonly KnownServers known;
    readonly ServerIconCache icons;
    readonly SoundLibrary soundLibrary;
    readonly VoiceHints hints; // Packages 34 and 131: why nobody hears the user, shown as VoiceHint on every tick
    ClientConnection? connection;
    VoiceClient? voice;
    long connectedAt; // a timestamp of Time
    bool? udpLogged;
    bool udpWasReachable; // Package 126
    /// <summary>Why the last connect attempt was refused, e.g. WrongPassword (Package 40).</summary>
    string? lastRejection;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsConnected))]
    ServerViewModel? server;

    /// <summary>The chat of the current connection (Package 32), replaces the activity feed.</summary>
    [ObservableProperty] ChatViewModel? chat;

    [ObservableProperty] string status = Strings.Status_NotConnected;
    [ObservableProperty] bool isConnecting;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsHomePage), nameof(IsSettingsPage), nameof(IsAdminPage))]
    Page page = Page.Home;
    [ObservableProperty] SettingsViewModel? settingsPage;
    [ObservableProperty] AdminViewModel? adminPage;
    [ObservableProperty] string pingText = "";
    [ObservableProperty] string transmitText = "";
    [ObservableProperty] string voiceHint = "";

    /// <summary>
    /// A main view model on the profile folder <paramref name="storageDir"/> with the parts it runs on: the key poller,
    /// the audio engine, the trusted fingerprints, the logo cache, the own tones and the leave timer, all on
    /// <paramref name="time"/>. The composition root (App) builds it through here.
    /// </summary>
    /// <param name="hardware">
    /// The microphones and speakers for the engine and the settings page: the system's in the app, fakes in tests; null
    /// runs without them (--no-audio, tests, debug API with test tone).
    /// </param>
    /// <param name="keyState">The state of a virtual key, the keyboard by default (tests pass a fake one).</param>
    public static MainViewModel Create(string storageDir, Action<Action> post, TimeProvider time, ClientLog log, ClientSettings settings,
        string? settingsWarning, IAudioHardware? hardware, Func<int, bool>? keyState = null)
    {
        var keys = new KeyPoller(time, keyState);
        var soundLibrary = new SoundLibrary(storageDir, log.Write); // holds no handle; the engine plays its tones
        AudioEngine? audio = null;
        try
        {
            audio = new AudioEngine(keys, time, hardware, new NativeCaptureFilters(log.Write), soundLibrary.Samples);
            return new MainViewModel(storageDir, post, time, log, settings, settingsWarning, new KnownServers(storageDir),
                new ServerIconCache(storageDir), soundLibrary, new LeaveTimer(time, () => post), keys, audio,
                hardware is null ? null : hardware.ListActive);
        }
        catch
        {
            audio?.Dispose(); // nobody else owns them yet
            keys.Dispose();
            throw;
        }
    }

    /// <summary>Runs on the given parts and builds none of them (<see cref="Create"/> builds them).</summary>
    /// <param name="time">The client's one clock: <see cref="Time"/>, handed on to everything built here.</param>
    /// <param name="settings">The settings as loaded from <paramref name="storageDir"/>.</param>
    /// <param name="settingsWarning">Why the settings file could not be read, shown as a warning notice; null when it could.</param>
    /// <param name="keys">Disposed with this view model, after <paramref name="audio"/>.</param>
    /// <param name="audio">
    /// Disposed with this view model, after the disconnect. When it opens devices, their list loads at the start and a
    /// device restart runs off the UI thread.
    /// </param>
    /// <param name="soundLibrary">The own tones; cleaned up after each save (the engine plays them, <see cref="Create"/> hands it the same library).</param>
    /// <param name="listDevices">The active devices of a direction, for the settings page; null: none, an empty list.</param>
    public MainViewModel(string storageDir, Action<Action> post, TimeProvider time, ClientLog log, ClientSettings settings, string? settingsWarning,
        KnownServers knownServers, ServerIconCache icons, SoundLibrary soundLibrary, LeaveTimer leave, KeyPoller keys, AudioEngine audio,
        Func<AudioDirection, List<AudioDevice>>? listDevices)
    {
        this.storageDir = storageDir;
        this.post = post;
        Time = time;
        Log = log;
        known = knownServers;
        this.icons = icons;
        Settings = settings;
        if (settingsWarning is not null) AddNotice(settingsWarning, NoticeKind.Warning);
        Appearance = BackgroundAppearance.From(Settings); // the saved look from the start, not only after the next save
        Leave = leave;
        Bookmarks = new BookmarkList(() => Settings.Bookmarks, icons.Load, Leave, () => Dialogs, () => Settings.Save(storageDir), Log.Write);
        Update = new UpdateFlow(() => Settings.CheckForUpdates, offer => Dialogs.OfferUpdate?.Invoke(offer) ?? Task.FromResult(false), Log.Write, text => Status = text,
            text => AddNotice(text, NoticeKind.Warning), PendingViewModel.Timeout);

        Devices = new DeviceList(post, listDevices, (inputs, outputs) => SettingsPage?.ShowDevices(inputs, outputs));
        EngineSync = new EngineSync(audio,
            connectionState: () => Server is { } connected
                ? new EngineConnectionState(true, connected.SelfMuted, connected.SelfDeafened, connected.HasSpeakLinked, connected.InMutedChannel,
                    connected.CurrentChannel?.Id, connected.CurrentChannel?.Bitrate)
                : EngineConnectionState.Disconnected,
            mirror: () => Server?.Mirror,
            volumeFor: fingerprint => Settings.VolumeFor(fingerprint));
        if (audio.DeviceLifecycle.OpensDevices) Devices.Refresh();
        hints = new VoiceHints(
            state: () => Server is { } connected
                ? new VoiceHintState(ChannelMuted: connected.InMutedChannel, HasSpeakLinked: connected.HasSpeakLinked,
                    ServerMuted: connected.Mirror.Self?.ServerMuted == true, SelfDeafened: connected.SelfDeafened, SelfMuted: connected.SelfMuted)
                : null,
            linkKeyDown: () => keys.LinkPttDown,
            muteKeyHeld: () => keys.MuteHeld,
            talkKeyHeld: () => audio.TalkKeyHeld,
            inVoiceActivation: () => audio.Mode == TransmitMode.VoiceActivation,
            voiceDetected: () => audio.VoiceDetected,
            time: time);
        Keys = keys;
        Audio = audio;
        Audio.Warning += text => post(() => AddNotice(text, NoticeKind.Warning)); // Package 124: devices open when needed
        this.soundLibrary = soundLibrary;
        Audio.TransmitChanged += target => post(() =>
        {
            Log.Write(target switch
            {
                null => "Senden beendet",
                Shared.Voice.VoiceHeader.TargetLinked => "Senden beginnt: eigener Channel und Links",
                _ => "Senden beginnt: eigener Channel",
            });
            TransmitText = target switch
            {
                null => "",
                Shared.Voice.VoiceHeader.TargetLinked => Strings.Transmit_Linked,
                _ => Strings.Transmit_Channel,
            };
            Server?.SetSelfTransmitting(target);
        });
        Audio.SpeakersChanged += active => post(() => Server?.OnSpeakers(active));
        Keys.Pressed += action => post(() => OnKeyAction(action));
        // Package 125: push-to-mute shows and reports the mute like the button. Read on the key thread, so a quick
        // press and release arrive in order; only its own edges count, as Changed also fires for the talk keys.
        bool muteHeld = false;
        Keys.Changed += () =>
        {
            bool held = Keys.MuteHeld;
            if (held == muteHeld) return;
            muteHeld = held;
            post(() => Server?.HoldMute(held));
        };
        ApplySettings(Settings);
    }

    public ClientSettings Settings { get; private set; }
    public ClientLog Log { get; }
    /// <summary>The client's one clock: the reconnect, the hints and the statistics, and each connection's view model and voice.</summary>
    public TimeProvider Time { get; }
    public KeyPoller Keys { get; }
    public AudioEngine Audio { get; }
    public ObservableCollection<Notice> Notices { get; } = [];
    public bool IsConnected => Server is not null;
    public bool IsHomePage => Page == Page.Home;
    public bool IsSettingsPage => Page == Page.Settings;
    public bool IsAdminPage => Page == Page.Admin;
    /// <summary>Package 40: the saved servers, shown as tiles in the sidebar.</summary>
    internal BookmarkList Bookmarks { get; }
    /// <summary>Packages 43 and 62: the update check, the install and the card over the window.</summary>
    internal UpdateFlow Update { get; }

    /// <summary>Keeps the audio engine in step: the connection's state and gains, and the saved settings.</summary>
    internal EngineSync EngineSync { get; }

    /// <summary>Package 52: the audio devices the settings page offers, loaded in the background.</summary>
    internal DeviceList Devices { get; }

    /// <summary>Package 102: how long a removed bookmark stays to fold away (the window sets it).</summary>
    public LeaveTimer Leave { get; }

    /// <summary>How to talk right now, shown under the own name while not sending.</summary>
    public string TalkHint => Settings.Mode == TransmitMode.VoiceActivation
        ? Strings.TalkHint_VoiceActivation
        : Settings.KeyBindings.Where(b => b.Action == KeyAction.PushToTalk).Select(b => b.Chord.Name).ToList() is { Count: > 0 } ptt
            ? $"PTT: {string.Join(", ", ptt)}"
            : Strings.TalkHint_NoPttKey;

    /// <summary>Push-to-talk without a key: nobody can talk. The hint then opens the settings.</summary>
    public bool HasNoPttBinding => Settings.Mode == TransmitMode.PushToTalk && Settings.ChordFor(KeyAction.PushToTalk) is null;
    public DialogCallbacks Dialogs { get; set; } = new();
    public Func<TofuPrompt, Task<bool>> ConfirmTofu { get; set; } = _ => Task.FromResult(false);

    /// <summary>Package 61: how see-through the backgrounds are right now (saved, or previewed on the settings page).</summary>
    [ObservableProperty] BackgroundAppearance appearance = new(1f, false);

    public void ApplySettings(ClientSettings settings) => Applied(EngineSync.Configure(Prepare(settings)));

    /// <summary>
    /// Package 98: the same, with the audio devices restarted off the UI thread (see <see cref="EngineSync.ConfigureAsync"/>), so the
    /// settings page can show "Speichern" busy meanwhile.
    /// </summary>
    public async Task ApplySettingsAsync(ClientSettings settings)
    {
        Applied(await EngineSync.ConfigureAsync(Prepare(settings)));
    }

    /// <summary>Everything but the audio devices; returns the settings now in use, for the engine.</summary>
    ClientSettings Prepare(ClientSettings settings)
    {
        Settings = settings.Clamp();
        Appearance = BackgroundAppearance.From(Settings);
        Settings.Save(storageDir);
        soundLibrary.CleanUp(Settings); // own tones nobody points to any more
        Keys.Bindings = Settings.KeyBindings.ToList();
        var keysText = Settings.KeyBindings.Count == 0 ? "keine" : string.Join(", ", Settings.KeyBindings.Select(b => $"{b.Action} {b.Chord.Name}"));
        Log.Write($"Einstellungen: Modus {Settings.Mode}, Tasten {keysText}, " +
                  $"Eingang {Settings.InputDeviceId ?? "Standard"}, Ausgang {Settings.OutputDeviceId ?? "Standard"}, " +
                  $"Verstärkung {Settings.InputGain:0.00}, Lautstärke {Settings.OutputVolume:0.00}, VAD-Schwelle {Settings.VadThresholdDb:0} dB, " +
                  $"Rauschunterdrückung {(Settings.NoiseSuppression ? "an" : "aus")}, automatische Verstärkung {(Settings.AutoGain ? "an" : "aus")}, " +
                  $"Echounterdrückung {(Settings.EchoCancellation ? "an" : "aus")}, Übertragungsart {Settings.AudioContent}");
        return Settings;
    }

    void Applied(string? warning)
    {
        if (warning is not null) AddNotice(warning, NoticeKind.Warning);
        OnPropertyChanged(nameof(Settings));
        OnPropertyChanged(nameof(TalkHint));
        OnPropertyChanged(nameof(HasNoPttBinding));
        Bookmarks.Refresh();
    }

    public async Task ConnectAsync(ConnectChoice choice)
    {
        if (Server is not null && !await ConfirmLeaveAsync(adminOnly: true)) return; // Package 114: the administration goes with the server
        await DisconnectAsync();
        lastRejection = null;
        if (choice.SaveBookmark) Bookmarks.Remember(choice, passwordConfirmed: false);

        IsConnecting = true;
        try
        {
            Attach(await OpenAsync(choice), choice);
            if (choice.SaveBookmark) Bookmarks.Remember(choice, passwordConfirmed: true);
        }
        catch (ConnectionRejectedException e)
        {
            Refused(e);
        }
        catch (TofuRejectedException)
        {
            Status = Strings.Status_TofuRejected;
        }
        catch (Exception e) when (IsNetworkFailure(e))
        {
            Status = string.Format(Strings.Status_Failed, e.Message);
        }
        finally
        {
            IsConnecting = false;
        }
        if (connection is null) Log.Write(Status);
    }

    static bool IsNetworkFailure(Exception e) =>
        e is SocketException or IOException or AuthenticationException or OperationCanceledException or ProtocolException;

    void Refused(ConnectionRejectedException e)
    {
        lastRejection = e.Code;
        Status = string.Format(Strings.Status_Refused, ErrorTexts.For(e.Code, e.Detail));
        Log.Write($"Abgelehnt vom Server: {e.Code}{(string.IsNullOrEmpty(e.Detail) ? "" : $" ({e.Detail})")}");
    }

    async Task<ClientConnection> OpenAsync(ConnectChoice choice)
    {
        Status = string.Format(Strings.Status_Connecting, choice.Host, choice.Port);
        Log.Write($"Verbinde mit {choice.Host}:{choice.Port} als {choice.Nickname}{(string.IsNullOrEmpty(choice.Password) ? "" : " mit Passwort")}");
        using var identity = ClientStorage.LoadOrCreateIdentity(storageDir);
        return await ClientConnection.ConnectAsync(choice.Host, choice.Port, identity, choice.Nickname,
            string.IsNullOrEmpty(choice.Password) ? null : choice.Password, known, ConfirmTofuLogged, Time);
    }

    /// <summary>A connection the server welcomed becomes the current session.</summary>
    /// <param name="restored">Package 139: a reconnect, which brings back no welcome text.</param>
    void Attach(ClientConnection conn, ConnectChoice choice, bool restored = false)
    {
        var mirror = new StateMirror(conn.Welcome);
        var vm = new ServerViewModel(mirror, request =>
        {
            Log.Write(ProtocolLogText.Describe(request, mirror));
            return conn.SendAsync(request);
        }, Time, Dialogs) { Post = post }; // Package 97: the waiting marks' timers come back to the UI thread
        vm.Notice += text => AddNotice(text, NoticeKind.Error);
        TrackServerIcon(vm, choice.Host, choice.Port);
        vm.PropertyChanged += OnServerPropertyChanged;
        vm.SoundRequested += Audio.Sounds.Play; // Package 47
        conn.MessageReceived += m => post(() =>
        {
            if (ProtocolLogText.Describe(m, mirror) is { } line) Log.Write(line);
            vm.Apply(m);
        });
        conn.Disconnected += (reason, detail) => post(() => OnDisconnected(conn, reason, detail));

        var udp = new VoiceClient(VoiceRelay ?? new IPEndPoint(conn.RemoteAddress, choice.Port), conn.Welcome.SessionId,
            Convert.FromBase64String(conn.Welcome.VoiceKey), Time, conn); // Package 141: falls back to the TLS connection
        // Package 139 (review): owned from here on, so a failure below is torn down with them (DisconnectAsync)
        connection = conn;
        voice = udp;
        udp.VoiceReceived += Audio.OnVoice;
        Audio.Send = udp.SendVoice;
        udp.Start();

        ResetStats();
        connectedAt = Time.GetTimestamp();
        udpLogged = null;
        udpWasReachable = false;
        Server = vm;
        EngineSync.SyncConnection();
        Status = string.Format(Strings.Status_Connected, vm.ServerName);
        Log.Write($"Verbunden mit '{vm.ServerName}' als {mirror.Self?.Nickname} (Session {mirror.SelfId}), " +
                  $"Channel '{mirror.Channels.GetValueOrDefault(mirror.Self?.ChannelId ?? Guid.Empty)?.Name}', {mirror.Users.Count} Nutzer online");
        Audio.Sounds.Play(SoundEvent.Connected);
        if (!restored && vm.WelcomeText.Length > 0) AddNotice(vm.WelcomeText, NoticeKind.Welcome);
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
            if (lastRejection != ReasonCodes.WrongPassword || Dialogs.AskPassword is not { } ask) return;
            if (await ask(bookmark.Name) is not { } answer) return;
            (password, save) = (answer.Password, answer.Save);
        }
    }

    async Task<bool> ConfirmTofuLogged(TofuPrompt prompt)
    {
        bool accepted = await ConfirmTofu(prompt);
        Log.Write($"Serverzertifikat {(prompt.Result == TofuResult.Mismatch ? "GEÄNDERT" : "neu")} für {prompt.Host}:{prompt.Port}, " +
                  $"Fingerprint {prompt.Fingerprint}: {(accepted ? "akzeptiert" : "abgelehnt")}");
        return accepted;
    }

    /// <summary>Package 114: the "Trennen" button asks first when the administration has unsaved changes.</summary>
    [RelayCommand]
    async Task Disconnect()
    {
        if (!await ConfirmLeaveAsync(adminOnly: true)) return;
        await DisconnectAsync();
    }

    public Task DisconnectAsync()
    {
        if (connection is not null) Log.Write("Verbindung getrennt (eigene Aktion)");
        StopReconnect(); // Package 139: also connecting elsewhere and closing the app
        lastChoice = null; // its password goes with the session
        return DisconnectAsync(Strings.Status_NotConnected);
    }

    /// <param name="status">Set before tearing down, so nobody ever sees "disconnected" with a stale status.</param>
    /// <param name="sound">Package 139 (A148): no "Getrennt" tone while a reconnect may still bring the session back.</param>
    async Task DisconnectAsync(string status, bool sound = true)
    {
        if (Page == Page.Admin) CloseAdmin(); // administration needs a server
        var conn = connection;
        connection = null;
        if (conn is not null && sound) Audio.Sounds.Play(SoundEvent.Disconnected);
        if (conn is not null) Status = status;
        if (Server is { } vm) vm.PropertyChanged -= OnServerPropertyChanged;
        stateBeforeSelfTest = null; // nothing to restore at a server that is gone
        await SetSelfTestAsync(false);
        Server = null;
        Audio.Send = null;
        voice?.Dispose();
        voice = null;
        EngineSync.SyncConnection();
        Audio.Mixer.Clear();
        ResetStats();
        if (conn is not null) await conn.DisposeAsync();
    }

    void OnDisconnected(ClientConnection conn, string reason, string? detail)
    {
        if (conn != connection) return;
        var text = string.Format(Strings.Status_Disconnected, ErrorTexts.For(reason, detail));
        if (!string.IsNullOrEmpty(detail)) Log.Write($"Getrennt vom Server: {reason} ({detail})");
        if (reason is ReasonCodes.ConnectionLost or ReasonCodes.ServerShutdown or ReasonCodes.ServerRestart && lastChoice is { } choice && Server is { } s)
        {
            StartReconnect(choice, text, s);
            return;
        }
        lastChoice = null; // no reconnect: its password goes with the session
        AddNotice(text, NoticeKind.Warning);
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

    /// <summary>
    /// What comes back after a reconnect: the channel with the password that worked there, the own mute and deafen, and
    /// whether undeafening unmutes (review: muted, then deafened, stays muted).
    /// </summary>
    sealed record Restore(Guid Channel, string ChannelName, string? Password, bool Muted, bool Deafened, bool MutedBeforeDeafen);

    void StartReconnect(ConnectChoice choice, string text, ServerViewModel s)
    {
        var self = s.Mirror.Self;
        // the self test mutes and deafens at the server; what counts is the state from before it. A mute that only holds
        // while push-to-mute is held is no choice to keep: the key may be let go meanwhile (Package 125).
        var (muted, deafened) = stateBeforeSelfTest ?? (s.SelfMuted && !s.MutedByHold, s.SelfDeafened);
        var restore = new Restore(self?.ChannelId ?? Guid.Empty, s.CurrentChannel?.Name ?? "", self is null ? null : s.PasswordFor(self.ChannelId),
            muted, deafened, s.MutedBeforeDeafen);
        Log.Write($"Verbindung verloren, neue Versuche nach {string.Join(", ", Reconnector.Delays.Select(d => $"{d.TotalSeconds:0}"))} s");
        lostText = text;
        _ = DisconnectAsync(text, sound: false);
        reconnector ??= new Reconnector(Time, post);
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
                Log.Write("Wiederverbinden: veralteter Versuch verworfen");
                await conn.DisposeAsync();
                return false;
            }
            Attach(conn, choice, restored: true);
            var vm = Server!;
            if (restore.Channel != Guid.Empty && vm.Mirror.Self?.ChannelId != restore.Channel)
            {
                if (vm.Mirror.Channels.ContainsKey(restore.Channel)) _ = vm.RejoinAsync(restore.Channel, restore.Password);
                else AddNotice(string.Format(Strings.Status_ReconnectChannelGone, restore.ChannelName), NoticeKind.Warning);
            }
            // server mute comes from the server (Package 85)
            if (restore.Muted || restore.Deafened) _ = vm.RestoreSelfStateAsync(restore.Muted, restore.Deafened, restore.MutedBeforeDeafen);
            IsReconnecting = false;
            AddNotice(Strings.Status_Reconnected);
            return true;
        }
        catch (Exception e) when (reconnector?.IsCurrent(generation) != true)
        {
            Log.Write($"Wiederverbinden: veralteter Versuch verworfen ({e.Message})");
            if (conn is not null) await conn.DisposeAsync();
            return false;
        }
        catch (ConnectionRejectedException e) when (!IsPassing(e.Code))
        {
            // the server said no (e.g. a new password): asking again would not change that
            EndSeries(null);
            Refused(e);
            return false;
        }
        catch (TofuRejectedException)
        {
            EndSeries(Strings.Status_TofuRejected);
            return false;
        }
        catch (Exception e)
        {
            // review: also the unexpected, so the series goes on instead of hanging at "Verbinde mit ..."
            Log.Write($"Wiederverbinden fehlgeschlagen: {e.GetType().Name}: {e.Message}");
            if (conn is not null && conn == connection) await DisconnectAsync(lostText, sound: false); // Attach got that far
            else if (conn is not null) await conn.DisposeAsync();
            return false;
        }
    }

    /// <summary>The fifth attempt failed too: the disconnect as it was before Package 139.</summary>
    void GiveUp()
    {
        Log.Write("Wiederverbinden aufgegeben");
        EndSeries(lostText);
    }

    /// <summary>"Abbrechen" on the start screen: the series ends at once, an attempt still running is ignored.</summary>
    [RelayCommand]
    void CancelReconnect()
    {
        if (!IsReconnecting) return;
        Log.Write("Wiederverbinden abgebrochen");
        EndSeries(lostText, sound: false);
    }

    /// <summary>The series ends without a session: the disconnected notice, the status, the tone unless the user chose it.</summary>
    /// <param name="status">Null when the caller sets it.</param>
    void EndSeries(string? status, bool sound = true)
    {
        StopReconnect();
        lastChoice = null; // review: the server password stays no longer than needed
        if (status is not null) Status = status;
        AddNotice(lostText, NoticeKind.Warning);
        if (sound) Audio.Sounds.Play(SoundEvent.Disconnected);
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
        var left = Math.Max(1, (int)Math.Ceiling((at - Time.GetUtcNow()).TotalSeconds));
        Status = string.Format(Strings.Status_Reconnecting, left);
    }

    /// <summary>Every connection starts a fresh chat (A27), with the earlier notices so none get lost.</summary>
    partial void OnServerChanged(ServerViewModel? oldValue, ServerViewModel? newValue)
    {
        if (oldValue is not null)
        {
            oldValue.StateChanged -= OnServerStateChanged;
            oldValue.VolumeChanged -= OnUserVolumeChanged;
        }
        if (newValue is not null)
        {
            newValue.StateChanged += OnServerStateChanged; // e.g. the own channel was muted (Package 34)
            newValue.VolumeOf = fingerprint => Settings.VolumeFor(fingerprint);
            newValue.VolumeChanged += OnUserVolumeChanged;
            newValue.HoldMute(Keys.MuteHeld); // Package 125: connected with the key held; its release then unmutes
        }
        Chat = newValue is null ? null : new ChatViewModel(newValue, Notices.Reverse());
        EngineSync.SyncConnection();
    }

    void OnServerPropertyChanged(object? sender, PropertyChangedEventArgs e) => EngineSync.SyncConnection();

    /// <summary>Package 122: the gain table only follows the users who come and go (volumes change in <see cref="OnUserVolumeChanged"/>).</summary>
    void OnServerStateChanged()
    {
        EngineSync.SyncConnection();
        if (Server?.LastChange.HasFlag(StateChange.Members) != true) return;
        EngineSync.SyncGains();
        // Package 124: a speaker's decoder stays over talk pauses, so it goes when they leave
        var users = Server.Mirror.Users;
        Audio.Mixer.RemoveLeft(id => id == AudioEngine.SelfTestSpeaker || users.ContainsKey(id));
    }

    /// <summary>
    /// Package 51: straight to the mixer and saved, without ApplySettings, which would restart the audio devices on
    /// every step of the slider.
    /// </summary>
    void OnUserVolumeChanged(string fingerprint, float volume)
    {
        Settings.SetVolume(fingerprint, volume);
        Settings.Save(storageDir);
        EngineSync.SyncGains();
    }

    /// <summary>Package 126: tests send UDP through a lossy relay instead of to the server.</summary>
    internal IPEndPoint? VoiceRelay { get; set; }

    /// <summary>Package 135: tests wait for its ping answers.</summary>
    internal VoiceClient? Voice => voice;
    /// <summary>The clock of the current connection's ping round trip (tests check it is <see cref="Time"/>).</summary>
    internal TimeProvider? ConnectionTime => connection?.Time;

    /// <summary>UI timer, every 100 ms.</summary>
    public void Tick()
    {
        Server?.RefreshSpeaking();
        LogUdpReachability();
        UpdateStats();
        UpdateReconnectStatus(); // Package 139
        PingText = connection?.LastRoundTrip is { } rtt
            ? $"Ping {rtt.TotalMilliseconds:0} ms" + (voice?.OverTcp == true ? Strings.Ping_VoiceOverTcp // Package 141
                : voice?.Reachable == false ? Strings.Ping_UdpUnreachable : "")
            : "";
        VoiceHint = hints.Evaluate();
    }

    // ---- Package 133 (A142): the signal icon and the connection details ----

    /// <summary>The voice connection's figures, behind the signal icon next to the ping.</summary>
    public ConnectionStatsViewModel Stats { get; } = new();
    static readonly TimeSpan StatsInterval = TimeSpan.FromSeconds(2);
    DateTimeOffset statsAt;
    int roundTripsSeen; // a round trip enters the baseline once, not again on every refresh while no answer comes

    void ResetStats()
    {
        Stats.Reset(Audio.Mixer.Stats());
        Audio.Loss.EndStreaks(); // Package 135: a reconnect into the same channel keeps no streak from before
        statsAt = DateTimeOffset.MinValue; // the first tick shows what there is
        roundTripsSeen = 0;
    }

    /// <summary>The crossing out on every tick, the figures every 2 s, like Fluxer's stats.</summary>
    void UpdateStats()
    {
        if (voice is not { } v) return;
        Stats.Unreachable = !v.Reachable;
        var now = Time.GetUtcNow();
        if (now - statsAt < StatsInterval) return;
        statsAt = now;
        var (roundTrip, count) = v.RoundTripSample;
        if (count != roundTripsSeen) Audio.Loss.Report(v.UplinkLoss); // Package 135: each answer's figure once, none while they stay away
        Stats.Update(count != roundTripsSeen ? roundTrip : null, v.UplinkLoss, v.DownlinkLoss, Audio.Mixer.Stats(), Audio.EncoderBitrate);
        roundTripsSeen = count;
    }

    [RelayCommand]
    Task ShowConnectionDetails() => Dialogs.ConnectionDetails?.Invoke(Stats) ?? Task.CompletedTask;

    /// <summary>
    /// Logs "reachable" once, or "not reachable" when nothing came back within 5 s (then "reachable" if it recovers).
    /// Package 126: UDP lost after it worked logs "not reachable" and, when it is back, "reachable again".
    /// </summary>
    void LogUdpReachability()
    {
        if (voice is not { } v) return;
        if (v.Reachable)
        {
            if (udpLogged == true) return;
            Log.Write(udpWasReachable ? "UDP wieder erreichbar" : "UDP erreichbar");
            udpLogged = udpWasReachable = true;
        }
        else if (udpLogged == true || (udpLogged is null && Time.GetElapsedTime(connectedAt) > TimeSpan.FromSeconds(5)))
        {
            Log.Write("UDP nicht erreichbar: Sprache kommt nicht an (Firewall oder NAT?)");
            udpLogged = false;
        }
    }

    /// <summary>Toggle keys work like the buttons, including the report to the server.</summary>
    void OnKeyAction(KeyAction action)
    {
        if (Server is not { } server) return;
        Log.Write($"Taste: {KeyActionLabels.Localize(action)}");
        switch (action)
        {
            case KeyAction.ToggleMute: server.ToggleMuteCommand.Execute(null); break;
            case KeyAction.ToggleDeafen: server.ToggleDeafenCommand.Execute(null); break;
        }
    }

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
            isCurrent: () => Server == serverViewModel,
            post: post,
            logoCached: Bookmarks.Refresh);
        serverViewModel.StateChanged += tracker.Sync;
        serverViewModel.IconReceived += tracker.Receive;
        tracker.Sync();
        return tracker;
    }

    // ---- Pages ----

    /// <summary>Package 114: the settings button and the hint ask first when the administration has unsaved changes.</summary>
    [RelayCommand]
    async Task OpenSettingsAsync()
    {
        if (Page == Page.Settings || !await ConfirmLeaveAsync()) return;
        OpenSettings();
    }

    public void OpenSettings()
    {
        if (Page == Page.Settings) return;
        ClosePage();
        var vm = new SettingsViewModel(Settings, Devices.Known?.Inputs, Devices.Known?.Outputs, Time, Keys.CaptureNextChordAsync)
        {
            EditKeyBinding = Dialogs.EditKeyBinding,
            CheckNow = Update.CheckAsync,
            ImportSound = (path, sound) => SoundImport.Prepare(path, storageDir, sound),
            PreviewSound = Audio.Sounds.Preview,
            LivePreview = live => // Package 53, 61, 121
            {
                Audio.ApplyLive(live);
                Keys.Bindings = live.KeyBindings;
                Appearance = BackgroundAppearance.From(live);
            },
            SetSelfTest = SetSelfTestAsync,
        };
        vm.ApplySaved = () => ApplySettingsAsync(vm.ToSettings(Settings)); // Package 98: before the page closes
        vm.CloseRequested += _ => CloseSettings();
        vm.LeaveRequested += () => _ = ClosePageAsync(); // Package 114: the X asks, "Abbrechen" does not
        Audio.InputLevel += OnInputLevel;
        Audio.LevelMeter = true; // Package 124 (A132): the microphone opens for the level meter
        SettingsPage = vm;
        Page = Page.Settings;
        Devices.Refresh(); // a device may have been plugged in since
    }

    void OnInputLevel(float db)
    {
        float threshold = Audio.InputThresholdDb; // Package 129: read with the level, on the capture thread
        post(() =>
        {
            if (SettingsPage is not { } vm) return;
            vm.InputLevelDb = db;
            vm.ThresholdInUseDb = threshold;
        });
    }

    (bool Muted, bool Deafened)? stateBeforeSelfTest;

    /// <summary>
    /// Package 53: during the self test one is muted and deafened, at the server too (others see "Ton aus"), and
    /// hears only the own voice. Afterwards the state from before comes back.
    /// </summary>
    public async Task SetSelfTestAsync(bool on)
    {
        if (on == Audio.SelfTest) return;
        Audio.SelfTest = on;
        if (SettingsPage is { } page) page.IsSelfTesting = on;
        Log.Write(on ? "Selbsttest gestartet" : "Selbsttest beendet");
        if (on)
        {
            stateBeforeSelfTest = Server is { } s ? (s.SelfMuted, s.SelfDeafened) : null;
            if (Server is { } server) await server.SetSelfStateAsync(true, true);
        }
        else
        {
            var before = stateBeforeSelfTest;
            stateBeforeSelfTest = null;
            if (Server is { } server && before is { } state) await server.SetSelfStateAsync(state.Muted, state.Deafened);
        }
    }

    /// <summary>
    /// Every way out of the settings ("Verwerfen", Esc, opening the administration) ends the live preview: what is
    /// saved counts again. After "Speichern" that is the new settings.
    /// </summary>
    void CloseSettings()
    {
        Audio.ApplyLive(Settings);
        Keys.Bindings = Settings.KeyBindings.ToList();
        Appearance = BackgroundAppearance.From(Settings);
        _ = SetSelfTestAsync(false);
        Audio.InputLevel -= OnInputLevel;
        Audio.LevelMeter = false;
        SettingsPage = null;
        if (Page == Page.Settings) Page = Page.Home;
    }

    [RelayCommand]
    public async Task OpenAdminAsync()
    {
        if (Server is not { CanAdminister: true } server || Page == Page.Admin) return;
        if (!await ConfirmLeaveAsync()) return; // Package 114
        ClosePage();
        var vm = new AdminViewModel(server);
        vm.CloseRequested += () => _ = ClosePageAsync(); // Package 114
        AdminPage = vm;
        Page = Page.Admin;
        await vm.RequestListsAsync();
    }

    void CloseAdmin()
    {
        AdminPage?.Detach();
        AdminPage = null;
        if (Page == Page.Admin) Page = Page.Home;
    }

    // ---- Package 114: unsaved changes ----

    /// <summary>Settings or administration hold changes a way out would lose; adminOnly for disconnecting (settings stay).</summary>
    public bool HasUnsavedChanges(bool adminOnly = false) =>
        (!adminOnly && SettingsPage?.HasChanges == true) || AdminPage?.HasAnyChanges == true;

    /// <summary>
    /// Asks before a way out that would lose changes: keep editing (false), save and go once the server took it, or go
    /// without saving (closing the page throws the edits away, as before). Without the dialog it goes without saving.
    /// </summary>
    public async Task<bool> ConfirmLeaveAsync(bool adminOnly = false)
    {
        if (!HasUnsavedChanges(adminOnly)) return true;
        var choice = Dialogs.AskLeave is { } ask ? await ask() : LeaveChoice.Discard;
        if (choice == LeaveChoice.Stay) return false;
        if (choice == LeaveChoice.Discard) return true;
        if (!adminOnly && SettingsPage is { HasChanges: true } settings && !await settings.SaveAsync()) return false;
        if (AdminPage is { } admin)
            foreach (var place in admin.Places.Where(p => p.HasChanges()))
                if (!await place.SaveAsync()) return false; // its error shows on the page, which stays
        return true;
    }

    /// <summary>Esc, the X and the administration's close button: ask first, then leave the page.</summary>
    public async Task ClosePageAsync()
    {
        if (!IsHomePage && await ConfirmLeaveAsync()) ClosePage();
    }

    /// <summary>Esc: leaves settings without saving, or the administration.</summary>
    public void ClosePage()
    {
        if (Page == Page.Settings) CloseSettings();
        else if (Page == Page.Admin) CloseAdmin();
    }

    public void AddNotice(string text, NoticeKind kind = NoticeKind.Info)
    {
        Log.Write("Meldung: " + text);
        var notice = new Notice(DateTime.SpecifyKind(Time.GetLocalNow().DateTime, DateTimeKind.Local), text, kind);
        Notices.Insert(0, notice);
        Chat?.AddNotice(notice);
        while (Notices.Count > 200) Notices.RemoveAt(Notices.Count - 1);
    }

    public async ValueTask DisposeAsync()
    {
        await DisconnectAsync();
        Audio.Dispose();
        Keys.Dispose();
        Log.Flush(); // Package 124: the lines still queued
    }
}
