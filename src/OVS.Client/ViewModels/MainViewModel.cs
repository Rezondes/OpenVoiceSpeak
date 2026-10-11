using OVS.Client.Dialogs;
using OVS.Client.Localization;
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OVS.Client.Audio;
using OVS.Client.Audio.Devices;
using OVS.Client.Audio.Processing;
using OVS.Client.Audio.Sounds;
using OVS.Client.Audio.Transmit;
using OVS.Client.Input;
using OVS.Client.Logging;
using OVS.Client.Models;
using OVS.Client.Net;
using OVS.Client.Services;
using OVS.Client.Settings;

namespace OVS.Client.ViewModels;

/// <summary>Application shell: settings, audio, and the current connection.</summary>
public sealed partial class MainViewModel : ObservableObject, IAsyncDisposable
{
    readonly string storageDir;
    readonly Action<Action> post;
    readonly KnownServers known;
    readonly SoundLibrary soundLibrary;
    readonly VoiceHints hints; // Packages 34 and 131: why nobody hears the user, shown as VoiceHint on every tick

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsConnected))]
    ServerViewModel? server;

    /// <summary>The chat of the current connection (Package 32), replaces the activity feed.</summary>
    [ObservableProperty] ChatViewModel? chat;

    [ObservableProperty] string status = Strings.Status_NotConnected;

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
    /// <param name="connect">How a connection is opened, the real one by default (tests pass their own); see the constructor.</param>
    public static MainViewModel Create(string storageDir, Action<Action> post, TimeProvider time, ClientLog log, ClientSettings settings,
        string? settingsWarning, IAudioHardware? hardware, Func<int, bool>? keyState = null,
        OpenConnection? connect = null)
    {
        var keys = new KeyPoller(time, keyState);
        var soundLibrary = new SoundLibrary(storageDir, log.Write); // holds no handle; the engine plays its tones
        AudioEngine? audio = null;
        try
        {
            audio = new AudioEngine(keys, time, hardware, new NativeCaptureFilters(log.Write), soundLibrary.Samples);
            return new MainViewModel(storageDir, post, time, log, settings, settingsWarning, new KnownServers(storageDir),
                new ServerIconCache(storageDir), soundLibrary, new LeaveTimer(time, () => post), keys, audio,
                hardware is null ? null : hardware.ListActive, connect);
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
    /// <param name="connect">
    /// Opens the connection of every connect and every reconnect attempt (the attempts pass <see cref="CancellationToken.None"/>);
    /// null: the identity file of <paramref name="storageDir"/> plus <see cref="ClientConnection.ConnectAsync"/>.
    /// </param>
    public MainViewModel(string storageDir, Action<Action> post, TimeProvider time, ClientLog log, ClientSettings settings, string? settingsWarning,
        KnownServers knownServers, ServerIconCache icons, SoundLibrary soundLibrary, LeaveTimer leave, KeyPoller keys, AudioEngine audio,
        Func<AudioDirection, List<AudioDevice>>? listDevices, OpenConnection? connect = null)
    {
        this.storageDir = storageDir;
        this.post = post;
        Time = time;
        Log = log;
        known = knownServers;
        Settings = settings;
        if (settingsWarning is not null) AddNotice(settingsWarning, NoticeKind.Warning);
        Appearance = BackgroundAppearance.From(Settings); // the saved look from the start, not only after the next save
        Bookmarks = new BookmarkList(() => Settings.Bookmarks, icons.Load, leave, () => Dialogs, () => Settings.Save(storageDir), Log.Write);
        Update = new UpdateFlow(() => Settings.CheckForUpdates, offer => Dialogs.OfferUpdate?.Invoke(offer) ?? Task.FromResult(false), Log.Write, text => Status = text,
            text => AddNotice(text, NoticeKind.Warning), PendingViewModel.Timeout);

        // PageFlow is built after SelfTest (it takes SelfTest.SetAsync); the two lambdas that read it run only after that:
        // the first device listing is asked for below it, the self test runs from the settings page.
        Devices = new DeviceList(post, listDevices, (inputs, outputs) => PageFlow!.SettingsPage?.ShowDevices(inputs, outputs));
        EngineSync = new EngineSync(audio,
            connectionState: () => Server is { } connected
                ? new EngineConnectionState(true, connected.SelfMuted, connected.SelfDeafened, connected.HasSpeakLinked, connected.InMutedChannel,
                    connected.CurrentChannel?.Id, connected.CurrentChannel?.Bitrate)
                : EngineConnectionState.Disconnected,
            mirror: () => Server?.Mirror,
            volumeFor: fingerprint => Settings.VolumeFor(fingerprint));
        SelfTest = new SelfTest(
            readEngineRunning: () => audio.SelfTest,
            setEngineRunning: on => audio.SelfTest = on,
            showOnPage: on =>
            {
                if (PageFlow!.SettingsPage is { } page) page.IsSelfTesting = on;
            },
            log: Log.Write,
            readServerState: () => Server is { } connected ? new SelfState(connected.SelfMuted, connected.SelfDeafened) : null,
            writeServerState: state => Server is { } connected ? connected.SetSelfStateAsync(state) : Task.CompletedTask);
        PageFlow = new PageFlowViewModel(time: time, post: post, settings: () => Settings, knownDevices: () => Devices.Known,
            refreshDevices: () => Devices.Refresh(), captureChord: keys.CaptureNextChordAsync, setKeyBindings: bindings => keys.Bindings = bindings,
            dialogs: () => Dialogs, checkNow: Update.CheckAsync, importSound: (path, sound) => SoundImport.Prepare(path, storageDir, sound),
            previewSound: audio.Sounds.Preview, applyLive: audio.ApplyLive, showAppearance: appearance => Appearance = appearance,
            setSelfTest: SelfTest.SetAsync, applySaved: ApplySettingsAsync,
            startLevelMeter: handler =>
            {
                audio.InputLevel += handler;
                audio.LevelMeter = true;
            },
            stopLevelMeter: handler =>
            {
                audio.InputLevel -= handler;
                audio.LevelMeter = false;
            },
            readThresholdDb: () => audio.InputThresholdDb, shownServer: () => Server);
        var voiceLink = new VoiceLink(hear: audio.OnVoice, setSend: send => audio.Send = send, time: time, log: Log.Write,
            reportUplink: loss => audio.Loss.Report(loss), endLossStreaks: () => audio.Loss.EndStreaks());
        Stats = new ConnectionStatsViewModel(time,
            readReachable: () => voiceLink.Reachable,
            readFigures: voiceLink.ReadFigures,
            readReceiveStats: () => audio.Mixer.Stats(),
            readEncoderBitrate: () => audio.EncoderBitrate);
        Connection = new ConnectionViewModel(time: time, post: post, log: Log.Write, openConnection: connect ?? OpenConnectionAsync, play: sound => audio.Sounds.Play(sound),
            voiceLink: voiceLink,
            engineSync: EngineSync, selfTest: SelfTest, bookmarks: Bookmarks, icons: icons, stats: Stats, dialogs: () => Dialogs, shownServer: () => Server,
            show: shown => Server = shown, setStatus: text => Status = text, readStatus: () => Status, addNotice: AddNotice,
            confirmLeave: () => PageFlow.ConfirmLeaveAsync(adminOnly: true),
            closeAdmin: () =>
            {
                if (PageFlow.Page == Page.Admin) PageFlow.ClosePage();
            });
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
    /// <summary>Package 40: the saved servers, shown as tiles in the sidebar.</summary>
    internal BookmarkList Bookmarks { get; }
    /// <summary>Packages 43 and 62: the update check, the install and the card over the window.</summary>
    internal UpdateFlow Update { get; }

    /// <summary>Keeps the audio engine in step: the connection's state and gains, and the saved settings.</summary>
    internal EngineSync EngineSync { get; }

    /// <summary>Package 52: the audio devices the settings page offers, loaded in the background.</summary>
    internal DeviceList Devices { get; }

    /// <summary>Package 53: the self test of the settings page, which mutes and deafens at the server meanwhile.</summary>
    internal SelfTest SelfTest { get; }

    /// <summary>Packages 104 and 114: home, the settings page and the administration, and the question before leaving one.</summary>
    internal PageFlowViewModel PageFlow { get; }

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

    /// <summary>The real connect: the profile's identity, the trusted fingerprints and the certificate question.</summary>
    async Task<ClientConnection> OpenConnectionAsync(ConnectChoice choice, CancellationToken token)
    {
        using var identity = ClientStorage.LoadOrCreateIdentity(storageDir);
        return await ClientConnection.ConnectAsync(choice.Host, choice.Port, identity, choice.Nickname,
            choice.PasswordOrNull, known, ConfirmTofuLogged, Time, token);
    }

    async Task<bool> ConfirmTofuLogged(TofuPrompt prompt)
    {
        bool accepted = await ConfirmTofu(prompt);
        Log.Write($"Serverzertifikat {(prompt.Result == TofuResult.Mismatch ? "GEÄNDERT" : "neu")} für {prompt.Host}:{prompt.Port}, " +
                  $"Fingerprint {prompt.Fingerprint}: {(accepted ? "akzeptiert" : "abgelehnt")}");
        return accepted;
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

    /// <summary>Package 122: the gain table only follows the users who come and go (volumes change in <see cref="OnUserVolumeChanged"/>).</summary>
    void OnServerStateChanged()
    {
        EngineSync.SyncConnection();
        if (Server?.LastChange.HasFlag(StateChange.Members) != true) return;
        EngineSync.SyncMembers();
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

    /// <summary>Package 139: connecting, the reconnect series and the session of the current connection.</summary>
    internal ConnectionViewModel Connection { get; }

    /// <summary>UI timer, every 100 ms.</summary>
    public void Tick()
    {
        Server?.RefreshSpeaking();
        Connection.VoiceLink.LogReachability();
        Connection.VoiceLink.FeedLoss(); // Package 135: the adapter hears each answer at the next tick
        Stats.Refresh();
        Connection.Tick();
        VoiceHint = hints.Evaluate();
    }

    // ---- Package 133 (A142): the signal icon and the connection details ----

    /// <summary>The voice connection's figures, behind the signal icon next to the ping.</summary>
    public ConnectionStatsViewModel Stats { get; }

    [RelayCommand]
    Task ShowConnectionDetails() => Dialogs.ConnectionDetails?.Invoke(Stats) ?? Task.CompletedTask;

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
        await Connection.DisconnectAsync();
        Audio.Dispose();
        Keys.Dispose();
        Log.Flush(); // Package 124: the lines still queued
    }
}
