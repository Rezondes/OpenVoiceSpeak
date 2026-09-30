using OVS.Client.Localization;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Net;
using System.Net.Sockets;
using System.Security.Authentication;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OVS.Client.Audio;
using OVS.Client.Input;
using OVS.Client.Logging;
using OVS.Client.Net;
using OVS.Client.Settings;
using OVS.Shared.Identity;
using OVS.Shared.Protocol;

namespace OVS.Client.ViewModels;

/// <param name="SavePassword">Package 39: store the password with the bookmark, after a successful connection only.</param>
public sealed record PasswordAnswer(string Password, bool Save);

/// <param name="Password">The password shown in the dialog; stored only with SavePassword.</param>
public sealed record BookmarkEdit(string Name, string Host, int Port, string Nickname, string? Password, bool SavePassword);

public sealed record ConnectChoice(string Host, int Port, string Nickname, string? Password, bool SaveBookmark, bool SavePassword = false);

/// <summary>A bookmark tile: the bookmark and the logo last seen for that server.</summary>
public sealed record BookmarkItem(Bookmark Bookmark, byte[]? Icon)
{
    public string Name => Bookmark.Name;
    public string Host => Bookmark.Host;
    public string Nickname => Bookmark.Nickname;
    public string Address => $"{Bookmark.Host}:{Bookmark.Port}";
    public bool HasSavedPassword => Bookmark.HasSavedPassword;
}

public enum NoticeKind { Info, Welcome, Warning, Error }

/// <summary>What the main area shows. Everything stays inside the one main window (A20).</summary>
public enum Page { Home, Settings, Admin }

/// <summary>A system notice, shown in the chat tab "Allgemein". ToString keeps the "time  text" form for the debug API.</summary>
public sealed record Notice(DateTime Time, string Text, NoticeKind Kind)
{
    public string TimeText => Time.ToString("HH:mm");
    public bool IsInfo => Kind == NoticeKind.Info;
    public bool IsWelcome => Kind == NoticeKind.Welcome;
    public bool IsWarning => Kind == NoticeKind.Warning;
    public bool IsError => Kind == NoticeKind.Error;
    public override string ToString() => $"{Time:HH:mm:ss}  {Text}";
}

/// <summary>Application shell: settings, audio, and the current connection.</summary>
public sealed partial class MainViewModel : ObservableObject, IAsyncDisposable
{
    readonly string storageDir;
    readonly Action<Action> post;
    readonly bool useAudioDevices;
    readonly KnownServers known;
    readonly ServerIconCache icons;
    readonly SoundLibrary sounds;
    ClientConnection? connection;
    VoiceClient? voice;
    DateTime connectedAt;
    bool? udpLogged;
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

    /// <param name="useAudioDevices">False runs without microphone and speaker (tests, debug API with test tone).</param>
    public MainViewModel(string storageDir, Action<Action> post, bool useAudioDevices = true, ClientLog? log = null)
    {
        this.storageDir = storageDir;
        this.post = post;
        this.useAudioDevices = useAudioDevices;
        Log = log ?? new ClientLog(storageDir, TimeProvider.System);
        known = new KnownServers(Path.Combine(storageDir, "known_servers.json"));
        icons = new ServerIconCache(storageDir);
        Settings = ClientSettings.Load(storageDir, out var warning);
        if (warning is not null) AddNotice(warning, NoticeKind.Warning);

        DeviceSource = useAudioDevices ? AudioDevices.List : _ => [];
        if (useAudioDevices) RefreshDevices();
        Keys = new KeyPoller();
        Audio = new AudioEngine(Keys, useAudioDevices);
        sounds = new SoundLibrary(storageDir, Log.Write);
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
    public KeyPoller Keys { get; }
    public AudioEngine Audio { get; }
    public ObservableCollection<Notice> Notices { get; } = [];
    public bool IsConnected => Server is not null;
    public bool IsHomePage => Page == Page.Home;
    public bool IsSettingsPage => Page == Page.Settings;
    public bool IsAdminPage => Page == Page.Admin;
    public IReadOnlyList<BookmarkItem> Bookmarks => Settings.Bookmarks.Select(b => new BookmarkItem(b, icons.Load(b.Host, b.Port))).ToList();
    public bool HasBookmarks => Settings.Bookmarks.Count > 0;

    /// <summary>How to talk right now, shown under the own name while not sending.</summary>
    public string TalkHint => Settings.Mode == TransmitMode.VoiceActivation
        ? Strings.TalkHint_VoiceActivation
        : Settings.KeyBindings.Where(b => b.Action == KeyAction.PushToTalk).Select(b => b.Chord.Name).ToList() is { Count: > 0 } ptt
            ? $"PTT: {string.Join(", ", ptt)}"
            : Strings.TalkHint_NoPttKey;

    /// <summary>Push-to-talk without a key: nobody can talk. The hint then opens the settings.</summary>
    public bool HasNoPttBinding => Settings.Mode == TransmitMode.PushToTalk && Settings.ChordFor(KeyAction.PushToTalk) is null;
    public Dialogs Dialogs { get; set; } = new();
    public Func<TofuPrompt, Task<bool>> ConfirmTofu { get; set; } = _ => Task.FromResult(false);

    /// <summary>Package 61: how see-through the backgrounds are right now (saved, or previewed on the settings page).</summary>
    [ObservableProperty] BackgroundAppearance appearance = new(1f, false);

    public void ApplySettings(ClientSettings settings)
    {
        Settings = settings.Clamp();
        Appearance = BackgroundAppearance.From(Settings);
        Settings.Save(storageDir);
        sounds.CleanUp(Settings); // own tones nobody points to any more
        Keys.Bindings = Settings.KeyBindings.ToList();
        var keysText = Settings.KeyBindings.Count == 0 ? "keine" : string.Join(", ", Settings.KeyBindings.Select(b => $"{b.Action} {b.Chord.Name}"));
        Log.Write($"Einstellungen: Modus {Settings.Mode}, Tasten {keysText}, " +
                  $"Eingang {Settings.InputDeviceId ?? "Standard"}, Ausgang {Settings.OutputDeviceId ?? "Standard"}, " +
                  $"Verstärkung {Settings.InputGain:0.00}, Lautstärke {Settings.OutputVolume:0.00}, VAD-Schwelle {Settings.VadThresholdDb:0} dB");
        if (Audio.Configure(Settings) is { } warning) AddNotice(warning, NoticeKind.Warning);
        OnPropertyChanged(nameof(Settings));
        OnPropertyChanged(nameof(TalkHint));
        OnPropertyChanged(nameof(HasNoPttBinding));
        OnSettingsListsChanged();
    }

    void OnSettingsListsChanged()
    {
        OnPropertyChanged(nameof(Bookmarks));
        OnPropertyChanged(nameof(HasBookmarks));
    }

    public async Task ConnectAsync(ConnectChoice choice)
    {
        await DisconnectAsync();
        lastRejection = null;
        if (choice.SaveBookmark) SaveBookmark(choice, passwordConfirmed: false);

        Status = string.Format(Strings.Status_Connecting, choice.Host, choice.Port);
        IsConnecting = true;
        Log.Write($"Verbinde mit {choice.Host}:{choice.Port} als {choice.Nickname}{(string.IsNullOrEmpty(choice.Password) ? "" : " mit Passwort")}");
        try
        {
            using var identity = ClientStorage.LoadOrCreateIdentity(storageDir);
            var conn = await ClientConnection.ConnectAsync(choice.Host, choice.Port, identity, choice.Nickname,
                string.IsNullOrEmpty(choice.Password) ? null : choice.Password, known, ConfirmTofuLogged);

            var mirror = new StateMirror(conn.Welcome);
            var vm = new ServerViewModel(mirror, request =>
            {
                Log.Write(ClientLog.Describe(request, mirror));
                return conn.SendAsync(request);
            }, TimeProvider.System, Dialogs);
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

            var udp = new VoiceClient(new IPEndPoint(conn.RemoteAddress, choice.Port), conn.Welcome.SessionId,
                Convert.FromBase64String(conn.Welcome.VoiceKey));
            udp.VoiceReceived += Audio.OnVoice;
            Audio.Send = udp.SendVoice;
            udp.Start();

            connection = conn;
            voice = udp;
            connectedAt = DateTime.UtcNow;
            udpLogged = null;
            Server = vm;
            SyncAudioFlags();
            Status = string.Format(Strings.Status_Connected, vm.ServerName);
            Log.Write($"Verbunden mit '{vm.ServerName}' als {mirror.Self?.Nickname} (Session {mirror.SelfId}), " +
                      $"Channel '{mirror.Channels.GetValueOrDefault(mirror.Self?.ChannelId ?? Guid.Empty)?.Name}', {mirror.Users.Count} Nutzer online");
            Audio.PlaySound(SoundEvent.Connected);
            if (vm.WelcomeText.Length > 0) AddNotice(vm.WelcomeText, NoticeKind.Welcome);
            if (choice.SaveBookmark) SaveBookmark(choice, passwordConfirmed: true);
        }
        catch (ConnectionRejectedException e)
        {
            lastRejection = e.Code;
            Status = string.Format(Strings.Status_Refused, ErrorTexts.For(e.Code, e.Detail));
            Log.Write($"Abgelehnt vom Server: {e.Code}{(string.IsNullOrEmpty(e.Detail) ? "" : $" ({e.Detail})")}");
        }
        catch (TofuRejectedException)
        {
            Status = Strings.Status_TofuRejected;
        }
        catch (Exception e) when (e is SocketException or IOException or AuthenticationException or OperationCanceledException or ProtocolException)
        {
            Status = string.Format(Strings.Status_Failed, e.Message);
        }
        finally
        {
            IsConnecting = false;
        }
        if (connection is null) Log.Write(Status);
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
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
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
            if (lastRejection != Codes.WrongPassword || Dialogs.AskPassword is not { } ask) return;
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

    [RelayCommand]
    public Task DisconnectAsync()
    {
        if (connection is not null) Log.Write("Verbindung getrennt (eigene Aktion)");
        return DisconnectAsync(Strings.Status_NotConnected);
    }

    /// <param name="status">Set before tearing down, so nobody ever sees "disconnected" with a stale status.</param>
    async Task DisconnectAsync(string status)
    {
        if (Page == Page.Admin) CloseAdmin(); // administration needs a server
        var conn = connection;
        connection = null;
        if (conn is not null) Audio.PlaySound(SoundEvent.Disconnected);
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
        if (conn is not null) await conn.DisposeAsync();
    }

    void OnDisconnected(ClientConnection conn, string reason, string? detail)
    {
        if (conn != connection) return;
        var text = string.Format(Strings.Status_Disconnected, ErrorTexts.For(reason, detail));
        if (!string.IsNullOrEmpty(detail)) Log.Write($"Getrennt vom Server: {reason} ({detail})");
        AddNotice(text, NoticeKind.Warning);
        _ = DisconnectAsync(text);
    }

    /// <summary>Every connection starts a fresh chat (A27), with the earlier notices so none get lost.</summary>
    partial void OnServerChanged(ServerViewModel? oldValue, ServerViewModel? newValue)
    {
        if (oldValue is not null)
        {
            oldValue.StateChanged -= SyncAudioFlags;
            oldValue.VolumeChanged -= OnUserVolumeChanged;
        }
        if (newValue is not null)
        {
            newValue.StateChanged += SyncAudioFlags; // e.g. the own channel was muted (Package 34)
            newValue.VolumeOf = fingerprint => Settings.VolumeFor(fingerprint);
            newValue.VolumeChanged += OnUserVolumeChanged;
        }
        Chat = newValue is null ? null : new ChatViewModel(newValue, Notices.Reverse());
        SyncAudioFlags();
    }

    void OnServerPropertyChanged(object? sender, PropertyChangedEventArgs e) => SyncAudioFlags();

    void SyncAudioFlags()
    {
        var s = Server;
        Audio.Connected = s is not null;
        Audio.SelfMuted = s?.SelfMuted ?? true;
        Audio.Deafened = s?.SelfDeafened ?? false;
        Audio.HasSpeakLinked = s?.HasSpeakLinked ?? false;
        Audio.ChannelMuted = s?.CurrentChannel?.IsMuted == true;
        // Package 51: the volume per person, mapped to the session ids of this connection (they change on reconnect)
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
        SyncAudioFlags();
    }

    /// <summary>UI timer, every 100 ms.</summary>
    public void Tick()
    {
        Server?.RefreshSpeaking();
        LogUdpReachability();
        PingText = connection?.LastRoundTrip is { } rtt
            ? $"Ping {rtt.TotalMilliseconds:0} ms" + (voice?.Reachable == false ? Strings.Ping_UdpUnreachable : "")
            : "";
        VoiceHint = Server switch
        {
            { CurrentChannel.IsMuted: true } => Strings.VoiceHint_MutedChannel,
            { HasSpeakLinked: false } when Keys.LinkPttDown => Strings.VoiceHint_NoLinkRight,
            _ => "",
        };
    }

    /// <summary>Logs "reachable" once, or "not reachable" when nothing came back within 5 s (then "reachable" if it recovers).</summary>
    void LogUdpReachability()
    {
        if (voice is not { } v || udpLogged == true) return;
        if (v.Reachable)
        {
            Log.Write("UDP erreichbar");
            udpLogged = true;
        }
        else if (udpLogged is null && DateTime.UtcNow - connectedAt > TimeSpan.FromSeconds(5))
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
        string? requested = null, retried = null;
        Task<string>? pending = null;
        void Sync()
        {
            var hash = vm.IconHash;
            if (hash is null)
            {
                vm.IconPng = null;
                icons.Remove(host, port);
                return;
            }
            if (vm.IconPng is { } shown && ServerIconFormat.Hash(shown) == hash) return;
            if (icons.Load(host, port) is { } cached && ServerIconFormat.Hash(cached) == hash)
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
            if (m is not Error { Code: Codes.RateLimited } e || pending is not { IsCompletedSuccessfully: true } p || p.Result != e.RequestId) return;
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
                Sync();
            });
        }
        vm.IconReceived += icon =>
        {
            if (icon.PngBase64 is null || icon.Hash != vm.IconHash) return;
            var png = Convert.FromBase64String(icon.PngBase64);
            if (ServerIconFormat.Hash(png) != icon.Hash) return;
            icons.Save(host, port, png);
            vm.IconPng = png;
            OnSettingsListsChanged(); // the bookmark tile shows it too
        };
        Sync();
    }

    // ---- Pages ----

    [RelayCommand]
    public void OpenSettings()
    {
        if (Page == Page.Settings) return;
        ClosePage();
        var vm = new SettingsViewModel(Settings, knownDevices?.Inputs, knownDevices?.Outputs, Keys)
        {
            EditKeyBinding = Dialogs.EditKeyBinding,
            CheckNow = CheckForUpdatesAsync,
            ImportSound = (path, sound) => SoundImport.Prepare(path, storageDir, sound),
            PreviewSound = (sound, setting, overall) => Audio.Sounds.Play(sounds.Samples(sound, setting), overall * setting.Volume),
            LivePreview = live => // Package 53, 61
            {
                Audio.ApplyLive(live);
                Appearance = BackgroundAppearance.From(live);
            },
            SetSelfTest = SetSelfTestAsync,
        };
        vm.CloseRequested += save =>
        {
            if (save) ApplySettings(vm.ToSettings(Settings));
            else // "Verwerfen": back to what is saved
            {
                Audio.ApplyLive(Settings);
                Appearance = BackgroundAppearance.From(Settings);
            }
            CloseSettings();
        };
        Audio.InputLevel += OnInputLevel;
        SettingsPage = vm;
        Page = Page.Settings;
        RefreshDevices(); // a device may have been plugged in since
    }

    void OnInputLevel(float db) => post(() =>
    {
        if (SettingsPage is { } vm) vm.InputLevelDb = db;
    });

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

    void CloseSettings()
    {
        _ = SetSelfTestAsync(false);
        Audio.InputLevel -= OnInputLevel;
        SettingsPage = null;
        if (Page == Page.Settings) Page = Page.Home;
    }

    [RelayCommand]
    public async Task OpenAdminAsync()
    {
        if (Server is not { CanAdminister: true } server || Page == Page.Admin) return;
        ClosePage();
        var vm = new AdminViewModel(server);
        vm.CloseRequested += CloseAdmin;
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
    }
}
