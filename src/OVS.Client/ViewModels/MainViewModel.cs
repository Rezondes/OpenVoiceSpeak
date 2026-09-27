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

public sealed record ConnectChoice(string Host, int Port, string Nickname, string? Password, bool SaveBookmark);

/// <summary>A bookmark tile: the bookmark and the logo last seen for that server.</summary>
public sealed record BookmarkItem(Bookmark Bookmark, byte[]? Icon)
{
    public string Name => Bookmark.Name;
    public string Host => Bookmark.Host;
    public string Nickname => Bookmark.Nickname;
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
    ClientConnection? connection;
    VoiceClient? voice;
    DateTime connectedAt;
    bool? udpLogged;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsConnected))]
    ServerViewModel? server;

    /// <summary>The chat of the current connection (Package 32), replaces the activity feed.</summary>
    [ObservableProperty] ChatViewModel? chat;

    [ObservableProperty] string status = "Nicht verbunden";
    [ObservableProperty] bool isConnecting;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsHomePage), nameof(IsSettingsPage), nameof(IsAdminPage))]
    Page page = Page.Home;
    [ObservableProperty] SettingsViewModel? settingsPage;
    [ObservableProperty] AdminViewModel? adminPage;
    [ObservableProperty] string pingText = "";
    [ObservableProperty] string transmitText = "";
    [ObservableProperty] string linkHint = "";

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

        Keys = new KeyPoller();
        Audio = new AudioEngine(Keys, useAudioDevices);
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
                Shared.Voice.VoiceHeader.TargetLinked => "Sendet an eigenen Channel und Links",
                _ => "Sendet",
            };
            Server?.SetSelfTransmitting(target);
        });
        Audio.SpeakersChanged += active => post(() => Server?.OnSpeakers(active));
        Keys.Pressed += action => post(() => OnKeyAction(action));
        ApplySettings(Settings);
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
        ? "Sprachaktivierung"
        : Settings.ChordFor(KeyAction.PushToTalk) is { } ptt ? $"PTT: {ptt.Name}" : "Keine PTT-Taste belegt";

    /// <summary>Push-to-talk without a key: nobody can talk. The hint then opens the settings.</summary>
    public bool HasNoPttBinding => Settings.Mode == TransmitMode.PushToTalk && Settings.ChordFor(KeyAction.PushToTalk) is null;
    public Dialogs Dialogs { get; set; } = new();
    public Func<TofuPrompt, Task<bool>> ConfirmTofu { get; set; } = _ => Task.FromResult(false);

    public void ApplySettings(ClientSettings settings)
    {
        Settings = settings.Clamp();
        Settings.Save(storageDir);
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
        if (choice.SaveBookmark)
        {
            Settings.Bookmarks.RemoveAll(b => b.Host == choice.Host && b.Port == choice.Port);
            Settings.Bookmarks.Insert(0, new Bookmark($"{choice.Host}:{choice.Port}", choice.Host, choice.Port, choice.Nickname));
            Settings.Save(storageDir);
            OnSettingsListsChanged();
        }

        Status = $"Verbinde mit {choice.Host}:{choice.Port} ...";
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
            Status = $"Verbunden mit {vm.ServerName}";
            Log.Write($"Verbunden mit '{vm.ServerName}' als {mirror.Self?.Nickname} (Session {mirror.SelfId}), " +
                      $"Channel '{mirror.Channels.GetValueOrDefault(mirror.Self?.ChannelId ?? Guid.Empty)?.Name}', {mirror.Users.Count} Nutzer online");
            if (vm.WelcomeText.Length > 0) AddNotice(vm.WelcomeText, NoticeKind.Welcome);
        }
        catch (ConnectionRejectedException e)
        {
            Status = "Abgelehnt: " + ErrorTexts.For(e.Code, e.Detail);
        }
        catch (TofuRejectedException)
        {
            Status = "Verbindung abgebrochen: Serverzertifikat nicht akzeptiert.";
        }
        catch (Exception e) when (e is SocketException or IOException or AuthenticationException or OperationCanceledException or ProtocolException)
        {
            Status = $"Verbindung fehlgeschlagen: {e.Message}";
        }
        finally
        {
            IsConnecting = false;
        }
        if (connection is null) Log.Write(Status);
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
        return DisconnectAsync("Nicht verbunden");
    }

    /// <param name="status">Set before tearing down, so nobody ever sees "disconnected" with a stale status.</param>
    async Task DisconnectAsync(string status)
    {
        if (Page == Page.Admin) CloseAdmin(); // administration needs a server
        var conn = connection;
        connection = null;
        if (conn is not null) Status = status;
        if (Server is { } vm) vm.PropertyChanged -= OnServerPropertyChanged;
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
        var text = "Getrennt: " + ErrorTexts.For(reason, detail);
        AddNotice(text, NoticeKind.Warning);
        _ = DisconnectAsync(text);
    }

    /// <summary>Every connection starts a fresh chat (A27), with the earlier notices so none get lost.</summary>
    partial void OnServerChanged(ServerViewModel? value) => Chat = value is null ? null : new ChatViewModel(value, Notices.Reverse());

    void OnServerPropertyChanged(object? sender, PropertyChangedEventArgs e) => SyncAudioFlags();

    void SyncAudioFlags()
    {
        var s = Server;
        Audio.Connected = s is not null;
        Audio.SelfMuted = s?.SelfMuted ?? true;
        Audio.Deafened = s?.SelfDeafened ?? false;
        Audio.HasSpeakLinked = s?.HasSpeakLinked ?? false;
    }

    /// <summary>UI timer, every 100 ms.</summary>
    public void Tick()
    {
        Server?.RefreshSpeaking();
        LogUdpReachability();
        PingText = connection?.LastRoundTrip is { } rtt
            ? $"Ping {rtt.TotalMilliseconds:0} ms" + (voice?.Reachable == false ? ", UDP nicht erreichbar" : "")
            : "";
        LinkHint = Keys.LinkPttDown && Server is { HasSpeakLinked: false }
            ? "Kein Recht für Link-Übertragungen: du sprichst nur im eigenen Channel."
            : "";
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

    /// <summary>
    /// Keeps the logo in step with the server's hash (Package 30): from the cache when it matches, otherwise
    /// downloaded once and cached for next time and for the bookmark tile.
    /// </summary>
    void TrackServerIcon(ServerViewModel vm, string host, int port)
    {
        string? requested = null;
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
            _ = vm.SendAsync(new GetServerIcon());
        }
        vm.StateChanged += Sync;
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
        var inputs = useAudioDevices ? AudioDevices.List(NAudio.CoreAudioApi.DataFlow.Capture) : [];
        var outputs = useAudioDevices ? AudioDevices.List(NAudio.CoreAudioApi.DataFlow.Render) : [];
        var vm = new SettingsViewModel(Settings, inputs, outputs, Keys);
        vm.CloseRequested += save =>
        {
            if (save) ApplySettings(vm.ToSettings(Settings));
            CloseSettings();
        };
        Audio.InputLevel += OnInputLevel;
        SettingsPage = vm;
        Page = Page.Settings;
    }

    void OnInputLevel(float db) => post(() =>
    {
        if (SettingsPage is { } vm) vm.InputLevelDb = db;
    });

    void CloseSettings()
    {
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
