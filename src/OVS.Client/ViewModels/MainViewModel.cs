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
internal sealed partial class MainViewModel : ObservableObject, IAsyncDisposable
{
    readonly string storageDir;
    readonly Action<Action> post;
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

    /// <summary>Runs on the given parts and builds none of them (<see cref="ClientServices.CreateMainViewModel"/> builds them).</summary>
    /// <param name="time">The client's one clock: <see cref="Time"/>, handed on to everything built here.</param>
    /// <param name="settings">The settings as loaded from <paramref name="storageDir"/>.</param>
    /// <param name="settingsWarning">Why the settings file could not be read, shown as a warning notice; null when it could.</param>
    /// <param name="keys">Disposed with this view model, after <paramref name="audio"/>.</param>
    /// <param name="audio">
    /// Disposed with this view model, after the disconnect. When it opens devices, their list loads at the start and a
    /// device restart runs off the UI thread.
    /// </param>
    /// <param name="soundLibrary">The own tones; cleaned up after each save (the engine plays them, <see cref="ClientServices.CreateMainViewModel"/> hands it the same library).</param>
    /// <param name="listDevices">The active devices of a direction, for the settings page; null: none, an empty list.</param>
    /// <param name="connect">
    /// Opens the connection of every connect and every reconnect attempt (the attempts pass <see cref="CancellationToken.None"/>);
    /// the real one is <see cref="ServerConnector.OpenAsync"/>, which asks <see cref="ConfirmTofu"/>.
    /// </param>
    /// <param name="storageDir">The profile folder.</param>
    /// <param name="post">Runs work on the UI thread.</param>
    /// <param name="log">The client's log.</param>
    /// <param name="icons">The cache of the server logos.</param>
    /// <param name="leave">How long a removed entry stays in its list.</param>
    public MainViewModel(string storageDir, Action<Action> post, TimeProvider time, ClientLog log, ClientSettings settings, string? settingsWarning,
        ServerIconCache icons, SoundLibrary soundLibrary, LeaveTimer leave, KeyPoller keys, AudioEngine audio, OpenConnection connect,
        Func<AudioDirection, List<AudioDevice>>? listDevices)
    {
        this.storageDir = storageDir;
        this.post = post;
        Time = time;
        Log = log;
        Settings = settings;
        if (settingsWarning is not null) AddNotice(settingsWarning, NoticeKind.Warning);
        Appearance = BackgroundAppearance.From(Settings); // the saved look from the start, not only after the next save
        Bookmarks = new BookmarkList(() => Settings.Bookmarks, icons.Load, leave, () => Dialogs.BookmarkList, () => Settings.Save(storageDir), Log);
        Update = new UpdateFlow(() => Settings.CheckForUpdates, offer => Dialogs.OfferUpdate?.Invoke(offer) ?? Task.FromResult(false), Log, text => Status = text,
            text => AddNotice(text, NoticeKind.Warning), PendingActionViewModel.Timeout);

        // PageFlow is built after SelfTest (it takes SelfTest.SetAsync); the two lambdas that read it run only after that:
        // the first device listing is asked for below it, the self test runs from the settings page.
        Devices = new DeviceList(post, listDevices, (inputs, outputs) => PageFlow!.SettingsPage?.ShowDevices(inputs, outputs));
        EngineSync = new EngineSync(audio,
            connectionState: () => Server is { } connected
                ? new EngineConnectionState(true, connected.SelfState.SelfMuted, connected.SelfState.SelfDeafened, connected.HasSpeakLinked, connected.InMutedChannel,
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
            log: Log,
            readServerState: () => Server is { } connected ? new SelfState(connected.SelfState.SelfMuted, connected.SelfState.SelfDeafened) : null,
            writeServerState: state => Server is { } connected ? connected.SelfState.SetSelfStateAsync(state) : Task.CompletedTask);
        PageFlow = new PageFlowViewModel(time: time, post: post, settings: () => Settings, knownDevices: () => Devices.Known,
            refreshDevices: () => Devices.RefreshAsync(), captureChord: keys.CaptureNextChordAsync, setKeyBindings: bindings => keys.Bindings = bindings,
            dialogs: () => Dialogs.PageFlow, adminDialogs: () => Dialogs.Admin, checkNow: Update.CheckAsync, importSound: (path, sound) => SoundImport.Prepare(path, storageDir, sound),
            previewSound: audio.Sounds.Preview, testSpeaker: audio.Sounds.PlayTest, applyLive: audio.ApplyLive, showAppearance: appearance => Appearance = appearance,
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
        var voiceLink = new VoiceLink(hear: audio.OnVoice, setSend: send => audio.Send = send, time: time, log: Log,
            reportUplink: loss => audio.Loss.Report(loss), endLossStreaks: () => audio.Loss.EndStreaks());
        Stats = new ConnectionStatsViewModel(time,
            readReachable: () => voiceLink.Reachable,
            readFigures: voiceLink.ReadFigures,
            readReceiveStats: () => audio.Mixer.Stats(),
            readEncoderBitrate: () => audio.EncoderBitrate);
        Connection = new ConnectionViewModel(time: time, post: post, log: Log, openConnection: connect, play: sound => audio.Sounds.Play(sound),
            voiceLink: voiceLink,
            engineSync: EngineSync, selfTest: SelfTest, bookmarks: Bookmarks, icons: icons, stats: Stats,
            askPassword: name => Dialogs.AskPassword?.Invoke(name) ?? Task.FromResult<PasswordAnswer?>(null), serverDialogs: () => Dialogs.Server,
            shownServer: () => Server,
            show: shown => Server = shown, setStatus: text => Status = text, readStatus: () => Status, addNotice: AddNotice,
            confirmLeave: () => PageFlow.ConfirmLeaveAsync(adminOnly: true),
            closeAdmin: () =>
            {
                if (PageFlow.Page == Page.Admin) PageFlow.CloseWithoutAsking();
            },
            hideAddress: () => Settings.StreamerMode);
        if (audio.DeviceLifecycle.OpensDevices) Devices.RefreshAsync();
        hints = new VoiceHints(
            state: () => Server is { } connected
                ? new VoiceHintState(ChannelMuted: connected.InMutedChannel, HasSpeakLinked: connected.HasSpeakLinked,
                    ServerMuted: connected.Mirror.Self?.ServerMuted == true, SelfDeafened: connected.SelfState.SelfDeafened, SelfMuted: connected.SelfState.SelfMuted)
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
            if (target is null) Log.TransmitStopped();
            else if (target == Shared.Voice.VoiceHeader.TargetLinked) Log.TransmitToChannelAndLinks();
            else Log.TransmitToChannel();
            TransmitText = target switch
            {
                null => "",
                Shared.Voice.VoiceHeader.TargetLinked => Strings.Transmit_Linked,
                _ => Strings.Transmit_Channel,
            };
            Server?.Tree.SetSelfTransmitting(target);
        });
        Audio.SpeakersChanged += active => post(() => Server?.Tree.OnSpeakers(active));
        Keys.Pressed += action => post(() => OnKeyAction(action));
        // Package 125: push-to-mute shows and reports the mute like the button. Read on the key thread, so a quick
        // press and release arrive in order; only its own edges count, as Changed also fires for the talk keys.
        bool muteHeld = false;
        Keys.Changed += () =>
        {
            bool held = Keys.MuteHeld;
            if (held == muteHeld) return;
            muteHeld = held;
            post(() => Server?.SelfState.HoldMuteAsync(held));
        };
        ApplySettings(Settings);
    }

    /// <summary>The settings in use.</summary>
    public ClientSettings Settings { get; private set; }

    /// <summary>Package 160: the saved streamer mode; the window hides private values while it is on.</summary>
    public bool StreamerMode => Settings.StreamerMode;
    /// <summary>The client's log.</summary>
    public ClientLog Log { get; }
    /// <summary>The client's one clock: the reconnect, the hints and the statistics, and each connection's view model and voice.</summary>
    public TimeProvider Time { get; }
    /// <summary>Polls the bound keys.</summary>
    public KeyPoller Keys { get; }
    /// <summary>Captures, sends, receives and plays.</summary>
    public AudioEngine Audio { get; }
    /// <summary>The system notices, before and between connections.</summary>
    public ObservableCollection<Notice> Notices { get; } = [];
    /// <summary>A server is shown.</summary>
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
        : Settings.KeyBindings.Where(keyBinding => keyBinding.Action == KeyAction.PushToTalk).Select(keyBinding => keyBinding.Chord.Name).ToList() is { Count: > 0 } ptt
            ? $"PTT: {string.Join(", ", ptt)}"
            : Strings.TalkHint_NoPttKey;

    /// <summary>Push-to-talk without a key: nobody can talk. The hint then opens the settings.</summary>
    public bool HasNoPttBinding => Settings.Mode == TransmitMode.PushToTalk && Settings.ChordFor(KeyAction.PushToTalk) is null;
    /// <summary>The dialogs the view models ask through.</summary>
    public DialogCallbacks Dialogs { get; set; } = new();
    /// <summary>Asks the user about an unknown or changed server certificate.</summary>
    public Func<TofuPrompt, Task<bool>> ConfirmTofu { get; set; } = _ => Task.FromResult(false);

    /// <summary>Package 61: how see-through the backgrounds are right now (saved, or previewed on the settings page).</summary>
    [ObservableProperty] BackgroundAppearance appearance = new(1f, false);

    /// <summary>Applies settings to the engine and the window.</summary>
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
        OnPropertyChanged(nameof(StreamerMode)); // Package 160
        Settings.Save(storageDir);
        soundLibrary.CleanUp(Settings); // own tones nobody points to any more
        Keys.Bindings = Settings.KeyBindings.ToList();
        var keysText = Settings.KeyBindings.Count == 0 ? "none" : string.Join(", ", Settings.KeyBindings.Select(keyBinding => $"{keyBinding.Action} {keyBinding.Chord.Name}"));
        Log.SettingsApplied(Settings.Mode, keysText, Settings.InputDeviceId ?? "default", Settings.OutputDeviceId ?? "default", Settings.InputGain, Settings.OutputVolume,
            #pragma warning disable CA1873 // the log files and the console are always on, so the argument is always used
            Settings.VadThresholdDb, OnOff(Settings.NoiseSuppression), OnOff(Settings.AutoGain), OnOff(Settings.EchoCancellation), Settings.AudioContent);
            #pragma warning restore CA1873
        return Settings;
    }

    static string OnOff(bool on) => on ? "on" : "off";

    void Applied(string? warning)
    {
        if (warning is not null) AddNotice(warning, NoticeKind.Warning);
        OnPropertyChanged(nameof(Settings));
        OnPropertyChanged(nameof(TalkHint));
        OnPropertyChanged(nameof(HasNoPttBinding));
        Bookmarks.Refresh();
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
            newValue.SelfState.HoldMuteAsync(Keys.MuteHeld); // Package 125: connected with the key held; its release then unmutes
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
        Server?.Tree.RefreshSpeaking();
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
    Task ShowConnectionDetailsAsync() => Dialogs.ConnectionDetails?.Invoke(Stats) ?? Task.CompletedTask;

    /// <summary>Toggle keys work like the buttons, including the report to the server.</summary>
    void OnKeyAction(KeyAction action)
    {
        if (action == KeyAction.ToggleStreamerMode) // Package 160: also without a connection
        {
            Log.KeyPressed(action);
            Settings.StreamerMode = !Settings.StreamerMode;
            Settings.Save(storageDir);
            OnPropertyChanged(nameof(StreamerMode));
            return;
        }
        if (Server is not { } server) return;
        Log.KeyPressed(action);
        switch (action)
        {
            case KeyAction.ToggleMute: server.SelfState.ToggleMuteCommand.Execute(null); break;
            case KeyAction.ToggleDeafen: server.SelfState.ToggleDeafenCommand.Execute(null); break;
        }
    }

    /// <summary>Shows a system notice.</summary>
    public void AddNotice(string text, NoticeKind kind = NoticeKind.Info)
    {
        Log.Notice(text);
        var notice = new Notice(DateTime.SpecifyKind(Time.GetLocalNow().DateTime, DateTimeKind.Local), text, kind);
        Notices.Insert(0, notice);
        Chat?.AddNotice(notice);
        while (Notices.Count > 200) Notices.RemoveAt(Notices.Count - 1);
    }

    /// <summary>Disconnects and releases the engine and the key poller.</summary>
    public async ValueTask DisposeAsync()
    {
        await Connection.DisconnectAsync();
        Audio.Dispose();
        Keys.Dispose();
        Log.Flush(); // Package 124: the lines still queued
    }
}
