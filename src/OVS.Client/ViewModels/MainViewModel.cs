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
using OVS.Client.Audio.Sounds;
using OVS.Client.Audio.Transmit;
using OVS.Client.Exceptions;
using OVS.Client.Input;
using OVS.Client.Logging;
using OVS.Client.Models;
using OVS.Client.Net;
using OVS.Client.Net.Updates;
using OVS.Client.Services;
using OVS.Client.Settings;
using OVS.Shared.Exceptions;
using OVS.Shared.Identity;
using OVS.Shared.Protocol;
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
    readonly SoundLibrary sounds;
    ClientConnection? connection;
    VoiceClient? voice;
    DateTime connectedAt;
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

    /// <summary>Package 62: the update being loaded, checked or started; null otherwise.</summary>
    [ObservableProperty, NotifyPropertyChangedFor(nameof(IsUpdating))] UpdateProgress? updateInProgress;
    public bool IsUpdating => UpdateInProgress is not null;

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
    /// <param name="useAudioDevices">False runs without microphone and speaker (tests, debug API with test tone).</param>
    /// <param name="keyState">The state of a virtual key, the keyboard by default (tests pass a fake one).</param>
    /// <param name="devices">Where microphone and speaker come from when not the system's (tests pass fakes).</param>
    public static MainViewModel Create(string storageDir, Action<Action> post, TimeProvider time, ClientLog log, ClientSettings settings,
        string? settingsWarning, bool useAudioDevices, Func<int, bool>? keyState = null, IAudioDeviceFactory? devices = null)
    {
        var keys = new KeyPoller(time, keyState);
        AudioEngine? audio = null;
        try
        {
            audio = new AudioEngine(keys, time, useAudioDevices, log.Write, devices);
            return new MainViewModel(storageDir, post, time, log, settings, settingsWarning, new KnownServers(storageDir),
                new ServerIconCache(storageDir), new SoundLibrary(storageDir, log.Write), new LeaveTimer(time, () => post), keys, audio);
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
    /// <param name="audio">Disposed with this view model, after the disconnect; whether it uses the system's devices decides the device list.</param>
    public MainViewModel(string storageDir, Action<Action> post, TimeProvider time, ClientLog log, ClientSettings settings, string? settingsWarning,
        KnownServers knownServers, ServerIconCache icons, SoundLibrary sounds, LeaveTimer leave, KeyPoller keys, AudioEngine audio)
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
        SyncBookmarks();

        DeviceSource = audio.UsesSystemDevices ? AudioDevices.List : _ => [];
        if (audio.UsesSystemDevices) RefreshDevices();
        Keys = keys;
        Audio = audio;
        Audio.Warning += text => post(() => AddNotice(text, NoticeKind.Warning)); // Package 124: devices open when needed
        this.sounds = sounds;
        Audio.SoundSource = sound => sounds.Samples(sound, Settings.SoundFor(sound)); // Package 48
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

    /// <summary>
    /// Package 52: reading a device's name takes 30 to 50 ms, with many (virtual) devices together more than half a
    /// second. So the list is loaded in the background and the settings open with the last one known.
    /// </summary>
    public Func<NAudio.CoreAudioApi.DataFlow, List<AudioDevice>> DeviceSource { get; set; }
    (List<AudioDevice> Inputs, List<AudioDevice> Outputs)? knownDevices;
    int deviceLoads;

    void RefreshDevices()
    {
        var source = DeviceSource;
        int load = ++deviceLoads;
        Task.Run(() => (source(NAudio.CoreAudioApi.DataFlow.Capture), source(NAudio.CoreAudioApi.DataFlow.Render)))
            .ContinueWith(t => post(() =>
            {
                if (load != deviceLoads) return; // a newer list is on its way
                knownDevices = t.Result;
                SettingsPage?.ShowDevices(t.Result.Item1, t.Result.Item2);
            }), CancellationToken.None, TaskContinuationOptions.OnlyOnRanToCompletion, TaskScheduler.Default);
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
    /// <summary>Package 102: kept in step with the settings, so a removed bookmark can fold away.</summary>
    public ObservableCollection<BookmarkItem> Bookmarks { get; } = [];

    /// <summary>Package 102: how long a removed bookmark stays to fold away (the window sets it).</summary>
    public LeaveTimer Leave { get; }

    void SyncBookmarks() =>
        CollectionSync.Sync(Bookmarks, Settings.Bookmarks.Select(b => new BookmarkItem(b, icons.Load(b.Host, b.Port))).ToList(), Leave);
    public bool HasBookmarks => Settings.Bookmarks.Count > 0;

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

    public void ApplySettings(ClientSettings settings) => Applied(Prepare(settings)());

    /// <summary>
    /// Package 98: the same, with the audio devices restarted off the UI thread (the engine locks itself), so the
    /// settings page can show "Speichern" busy meanwhile. Without devices nothing takes long and it all happens at once.
    /// </summary>
    public async Task ApplySettingsAsync(ClientSettings settings)
    {
        var configure = Prepare(settings);
        Applied(Audio.UsesSystemDevices ? await Task.Run(configure) : configure());
    }

    /// <summary>Everything but the audio devices; returns their restart.</summary>
    Func<string?> Prepare(ClientSettings settings)
    {
        Settings = settings.Clamp();
        Appearance = BackgroundAppearance.From(Settings);
        Settings.Save(storageDir);
        sounds.CleanUp(Settings); // own tones nobody points to any more
        Keys.Bindings = Settings.KeyBindings.ToList();
        var keysText = Settings.KeyBindings.Count == 0 ? "keine" : string.Join(", ", Settings.KeyBindings.Select(b => $"{b.Action} {b.Chord.Name}"));
        Log.Write($"Einstellungen: Modus {Settings.Mode}, Tasten {keysText}, " +
                  $"Eingang {Settings.InputDeviceId ?? "Standard"}, Ausgang {Settings.OutputDeviceId ?? "Standard"}, " +
                  $"Verstärkung {Settings.InputGain:0.00}, Lautstärke {Settings.OutputVolume:0.00}, VAD-Schwelle {Settings.VadThresholdDb:0} dB, " +
                  $"Rauschunterdrückung {(Settings.NoiseSuppression ? "an" : "aus")}, automatische Verstärkung {(Settings.AutoGain ? "an" : "aus")}, " +
                  $"Echounterdrückung {(Settings.EchoCancellation ? "an" : "aus")}, Übertragungsart {Settings.Transmission}");
        var applied = Settings;
        return () => Audio.Configure(applied);
    }

    void Applied(string? warning)
    {
        if (warning is not null) AddNotice(warning, NoticeKind.Warning);
        OnPropertyChanged(nameof(Settings));
        OnPropertyChanged(nameof(TalkHint));
        OnPropertyChanged(nameof(HasNoPttBinding));
        OnSettingsListsChanged();
    }

    void OnSettingsListsChanged()
    {
        SyncBookmarks();
        OnPropertyChanged(nameof(Bookmarks));
        OnPropertyChanged(nameof(HasBookmarks));
    }

    public async Task ConnectAsync(ConnectChoice choice)
    {
        if (Server is not null && !await ConfirmLeaveAsync(adminOnly: true)) return; // Package 114: the administration goes with the server
        await DisconnectAsync();
        lastRejection = null;
        if (choice.SaveBookmark) SaveBookmark(choice, passwordConfirmed: false);

        IsConnecting = true;
        try
        {
            Attach(await OpenAsync(choice), choice);
            if (choice.SaveBookmark) SaveBookmark(choice, passwordConfirmed: true);
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
            string.IsNullOrEmpty(choice.Password) ? null : choice.Password, known, ConfirmTofuLogged);
    }

    /// <summary>A connection the server welcomed becomes the current session.</summary>
    /// <param name="restored">Package 139: a reconnect, which brings back no welcome text.</param>
    void Attach(ClientConnection conn, ConnectChoice choice, bool restored = false)
    {
        var mirror = new StateMirror(conn.Welcome);
        var vm = new ServerViewModel(mirror, request =>
        {
            Log.Write(ClientLog.Describe(request, mirror));
            return conn.SendAsync(request);
        }, Time, Dialogs) { Post = post }; // Package 97: the waiting marks' timers come back to the UI thread
        vm.Notice += text => AddNotice(text, NoticeKind.Error);
        TrackServerIcon(vm, choice.Host, choice.Port);
        vm.PropertyChanged += OnServerPropertyChanged;
        vm.SoundRequested += Audio.PlaySound; // Package 47
        conn.MessageReceived += m => post(() =>
        {
            if (ClientLog.Describe(m, mirror) is { } line) Log.Write(line);
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
        connectedAt = DateTime.UtcNow;
        udpLogged = null;
        udpWasReachable = false;
        Server = vm;
        SyncAudioFlags();
        Status = string.Format(Strings.Status_Connected, vm.ServerName);
        Log.Write($"Verbunden mit '{vm.ServerName}' als {mirror.Self?.Nickname} (Session {mirror.SelfId}), " +
                  $"Channel '{mirror.Channels.GetValueOrDefault(mirror.Self?.ChannelId ?? Guid.Empty)?.Name}', {mirror.Users.Count} Nutzer online");
        Audio.PlaySound(SoundEvent.Connected);
        if (!restored && vm.WelcomeText.Length > 0) AddNotice(vm.WelcomeText, NoticeKind.Welcome);
        lastChoice = choice with { SaveBookmark = false }; // Package 139: what a reconnect uses, the password in memory only
    }

    /// <summary>
    /// Puts the server first in the bookmarks, keeping a name given earlier. The password (Package 39) is only
    /// written once the server accepted it: before that the bookmark keeps whatever it had.
    /// </summary>
    void SaveBookmark(ConnectChoice choice, bool passwordConfirmed)
    {
        var old = Settings.Bookmarks.FirstOrDefault(b => b.Host == choice.Host && b.Port == choice.Port);
        var password = !passwordConfirmed ? old?.ProtectedPassword
            : choice.SavePassword && !string.IsNullOrEmpty(choice.Password) ? PasswordProtector.Protect(choice.Password)
            : null;
        Settings.Bookmarks.RemoveAll(b => b.Host == choice.Host && b.Port == choice.Port);
        Settings.Bookmarks.Insert(0, new Bookmark(old?.Name ?? $"{choice.Host}:{choice.Port}", choice.Host, choice.Port, choice.Nickname, password));
        Settings.Save(storageDir);
        if (passwordConfirmed) Log.Write($"Lesezeichen gespeichert{(password is null ? "" : ", Passwort verschlüsselt gespeichert")}");
        OnSettingsListsChanged();
    }

    // ---- Updates (Package 43) ----

    public UpdateChecker? Updates { get; set; }
    public UpdateInstaller? Installer { get; set; }

    public Task StartupUpdateCheckAsync() => Settings.CheckForUpdates ? CheckForUpdatesAsync() : Task.CompletedTask;

    /// <returns>What happened, for the line under "Nach Updates suchen".</returns>
    public async Task<string> CheckForUpdatesAsync()
    {
        if (Updates is not { IsEnabled: true } updates) return Strings.Update_LocalBuild;
        using var timeout = new CancellationTokenSource(PendingViewModel.Timeout); // Package 98 (A113): 10 s like every other wait
        var result = await updates.CheckAsync(timeout.Token);
        if (result.Error is { } error)
        {
            Log.Write($"Update-Prüfung fehlgeschlagen: {error}");
            return string.Format(Strings.Update_CheckFailed, error);
        }
        if (result.Offer is not { } offer)
        {
            Log.Write("Update-Prüfung: keine neuere Version");
            return Strings.Update_Latest;
        }
        Log.Write($"Update verfügbar: {offer.Version} ({offer.Tag})");
        if (Dialogs.OfferUpdate is not { } ask || !await ask(offer) || Installer is null) return string.Format(Strings.Update_Available, offer.Version);

        Status = string.Format(Strings.Update_Downloading, offer.Version);
        Log.Write($"Update auf {offer.Version} wird installiert");
        UpdateInProgress = new UpdateProgress(offer.Version, UpdatePhase.Downloading);
        string? failure;
        try
        {
            // Package 62: reported on this (UI) context, the card over the window follows it
            failure = await Installer.InstallAsync(offer, p => UpdateInProgress = p);
        }
        finally
        {
            UpdateInProgress = null;
        }
        if (failure is null) return Strings.Update_Starting;
        Log.Write(failure);
        Status = failure;
        AddNotice(failure, NoticeKind.Warning);
        return failure;
    }

    // ---- Bookmarks in the sidebar (Package 40) ----

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

    public async Task EditBookmarkAsync(Bookmark bookmark)
    {
        if (Dialogs.EditBookmark is not { } edit || await edit(bookmark) is not { } e) return;
        int index = Settings.Bookmarks.IndexOf(bookmark);
        if (index < 0) return;
        var password = e.SavePassword && !string.IsNullOrEmpty(e.Password) ? PasswordProtector.Protect(e.Password) : null;
        Settings.Bookmarks[index] = new Bookmark(e.Name, e.Host, e.Port, e.Nickname, password);
        Settings.Save(storageDir);
        Log.Write($"Lesezeichen '{bookmark.Name}' geändert: {e.Name}, {e.Host}:{e.Port}, {e.Nickname}, Passwort {(password is null ? "nicht gespeichert" : "gespeichert")}");
        OnSettingsListsChanged();
    }

    public async Task DeleteBookmarkAsync(Bookmark bookmark)
    {
        if (Dialogs.Confirm is not { } confirm || !await confirm(string.Format(Strings.Confirm_DeleteBookmark, bookmark.Name))) return;
        if (!Settings.Bookmarks.Remove(bookmark)) return;
        Settings.Save(storageDir);
        Log.Write($"Lesezeichen '{bookmark.Name}' gelöscht");
        OnSettingsListsChanged();
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
        if (conn is not null && sound) Audio.PlaySound(SoundEvent.Disconnected);
        if (conn is not null) Status = status;
        if (Server is { } vm) vm.PropertyChanged -= OnServerPropertyChanged;
        stateBeforeSelfTest = null; // nothing to restore at a server that is gone
        await SetSelfTestAsync(false);
        Server = null;
        Audio.Send = null;
        voice?.Dispose();
        voice = null;
        SyncAudioFlags();
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
        if (sound) Audio.PlaySound(SoundEvent.Disconnected);
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
        SyncAudioFlags();
    }

    void OnServerPropertyChanged(object? sender, PropertyChangedEventArgs e) => SyncAudioFlags();

    /// <summary>Package 122: the gain table only follows the users who come and go (volumes change in <see cref="OnUserVolumeChanged"/>).</summary>
    void OnServerStateChanged()
    {
        SyncAudioFlags();
        if (Server?.LastChange.HasFlag(StateChange.Members) != true) return;
        SyncGains();
        // Package 124: a speaker's decoder stays over talk pauses, so it goes when they leave
        var users = Server.Mirror.Users;
        Audio.Mixer.RemoveLeft(id => id == AudioEngine.SelfTestSpeaker || users.ContainsKey(id));
    }

    void SyncAudioFlags()
    {
        var s = Server;
        Audio.Connected = s is not null;
        Audio.SelfMuted = s?.SelfMuted ?? true;
        Audio.Deafened = s?.SelfDeafened ?? false;
        Audio.HasSpeakLinked = s?.HasSpeakLinked ?? false;
        Audio.ChannelMuted = s?.CurrentChannel?.IsMuted == true;
        // Package 132: join, move, edit; clamped, so a hand-edited server file cannot hand Opus an invalid bitrate
        Audio.ChannelBitrate = Math.Clamp(s?.CurrentChannel?.Bitrate ?? ProtocolDefaults.Bitrate, ProtocolLimits.MinBitrate, ProtocolLimits.MaxBitrate);
        if (s?.CurrentChannel?.Id != bitrateChannel)
        {
            bitrateChannel = s?.CurrentChannel?.Id;
            Audio.Loss.Reset(Audio.ChannelBitrate); // Package 135: a move starts at the channel's bitrate, also at the same one
        }
        if (s != gainsFor) SyncGains();
    }

    Guid? bitrateChannel;

    ServerViewModel? gainsFor;

    /// <summary>Package 51: the volume per person, mapped to the session ids of this connection (they change on reconnect).</summary>
    void SyncGains()
    {
        var s = gainsFor = Server;
        GainTableBuilds++;
        Audio.Mixer.SetSpeakerGains(s?.Mirror.Users.Values
            .Where(u => Settings.VolumeFor(u.Fingerprint) != 1f)
            .ToDictionary(u => u.SessionId, u => Settings.VolumeFor(u.Fingerprint)) ?? []);
    }

    /// <summary>
    /// Package 51: straight to the mixer and saved, without ApplySettings, which would restart the audio devices on
    /// every step of the slider.
    /// </summary>
    void OnUserVolumeChanged(string fingerprint, float volume)
    {
        Settings.SetVolume(fingerprint, volume);
        Settings.Save(storageDir);
        SyncGains();
    }

    /// <summary>Package 126: tests send UDP through a lossy relay instead of to the server.</summary>
    internal IPEndPoint? VoiceRelay { get; set; }

    /// <summary>Package 135: tests wait for its ping answers.</summary>
    internal VoiceClient? Voice => voice;

    /// <summary>Package 122: how often the speaker gain table was built and the logo checked against its hash (tests count them).</summary>
    internal int GainTableBuilds { get; private set; }
    internal int IconHashes { get; private set; }

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
        VoiceHint = Server switch
        {
            { CurrentChannel.IsMuted: true } => Strings.VoiceHint_MutedChannel,
            { HasSpeakLinked: false } when Keys.LinkPttDown => Strings.VoiceHint_NoLinkRight,
            { } s => MutedTalkHint(s),
            _ => "",
        };
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

    static readonly TimeSpan MutedHintHold = TimeSpan.FromSeconds(2);
    DateTimeOffset mutedTalkAt = DateTimeOffset.MinValue;

    /// <summary>
    /// Package 131 (A140): talking into a muted microphone (a talk key held, or voice activation hears voice) shows why
    /// nobody hears it, until 2 s after. Push-to-mute held is muting on purpose and shows nothing.
    /// </summary>
    string MutedTalkHint(ServerViewModel s)
    {
        string why = s.Mirror.Self?.ServerMuted == true ? Strings.VoiceHint_ServerMuted
            : s.SelfDeafened ? Strings.VoiceHint_Deafened
            : s.SelfMuted && !Keys.MuteHeld ? Strings.VoiceHint_SelfMuted
            : "";
        if (why == "")
        {
            mutedTalkAt = DateTimeOffset.MinValue; // muting again later must not bring back an old attempt
            return "";
        }
        var now = Time.GetUtcNow();
        if (Audio.TalkKeyHeld || (Audio.Mode == TransmitMode.VoiceActivation && Audio.VoiceDetected)) mutedTalkAt = now;
        return now - mutedTalkAt < MutedHintHold ? why : "";
    }

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
        else if (udpLogged == true || (udpLogged is null && DateTime.UtcNow - connectedAt > TimeSpan.FromSeconds(5)))
        {
            Log.Write("UDP nicht erreichbar: Sprache kommt nicht an (Firewall oder NAT?)");
            udpLogged = false;
        }
    }

    /// <summary>Toggle keys work like the buttons, including the report to the server.</summary>
    void OnKeyAction(KeyAction action)
    {
        if (Server is not { } server) return;
        Log.Write($"Taste: {KeyActions.Label(action)}");
        switch (action)
        {
            case KeyAction.ToggleMute: server.ToggleMuteCommand.Execute(null); break;
            case KeyAction.ToggleDeafen: server.ToggleDeafenCommand.Execute(null); break;
        }
    }

    /// <summary>The server answers the same logo once per 10 s; a second more to be clear of the boundary.</summary>
    static readonly TimeSpan IconRetryDelay = TimeSpan.FromSeconds(11);

    /// <summary>
    /// Keeps the logo in step with the server's hash (Package 30): from the cache when it matches, otherwise
    /// downloaded once and cached for next time and for the bookmark tile.
    /// </summary>
    public void TrackServerIcon(ServerViewModel vm, string host, int port)
    {
        string? requested = null, retried = null, seen = null;
        bool synced = false;
        Task<string>? pending = null;
        void Sync()
        {
            var hash = vm.IconHash;
            if (synced && hash == seen) return; // Package 122: nothing to do until the server's logo changes
            (synced, seen) = (true, hash);
            IconHashes++;
            if (hash is null)
            {
                vm.IconPng = null;
                icons.Remove(host, port);
                return;
            }
            if (vm.IconPng is { } shown && ServerIconFormat.ComputeHash(shown) == hash) return;
            if (icons.Load(host, port) is { } cached && ServerIconFormat.ComputeHash(cached) == hash)
            {
                vm.IconPng = cached;
                return;
            }
            if (requested == hash) return;
            requested = hash;
            pending = vm.SendAsync(new GetServerIcon());
        }
        vm.StateChanged += Sync;
        vm.AdminMessage += m =>
        {
            // Refused within the server's interval: asked once more afterwards, if that logo is still wanted.
            if (m is not Error { Code: ReasonCodes.RateLimited } e || pending is not { IsCompletedSuccessfully: true } p || p.Result != e.RequestId) return;
            var hash = requested;
            if (retried == hash) return;
            retried = hash;
            _ = RetryAsync(hash);
        };
        async Task RetryAsync(string? hash)
        {
            await Task.Delay(IconRetryDelay, vm.Time);
            post(() =>
            {
                if (Server != vm || vm.IconHash != hash) return;
                requested = null;
                synced = false;
                Sync();
            });
        }
        vm.IconReceived += icon =>
        {
            if (icon.PngBase64 is null || icon.Hash != vm.IconHash) return;
            var png = Convert.FromBase64String(icon.PngBase64);
            if (ServerIconFormat.ComputeHash(png) != icon.Hash) return;
            icons.Save(host, port, png);
            vm.IconPng = png;
            OnSettingsListsChanged(); // the bookmark tile shows it too
        };
        Sync();
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
        var vm = new SettingsViewModel(Settings, knownDevices?.Inputs, knownDevices?.Outputs, Keys, Time)
        {
            EditKeyBinding = Dialogs.EditKeyBinding,
            CheckNow = CheckForUpdatesAsync,
            ImportSound = (path, sound) => SoundImport.Prepare(path, storageDir, sound),
            PreviewSound = (sound, setting, overall) => Audio.PlaySamples(sounds.Samples(sound, setting), overall * setting.Volume),
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
        RefreshDevices(); // a device may have been plugged in since
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
            foreach (var place in Enum.GetValues<AdminPlace>().Where(admin.HasChanges))
                if (!await admin.SaveAsync(place)) return false; // its error shows on the page, which stays
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
        var notice = new Notice(DateTime.Now, text, kind);
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
