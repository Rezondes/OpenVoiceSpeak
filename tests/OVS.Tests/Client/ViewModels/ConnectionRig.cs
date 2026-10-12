using OVS.Client.Audio;
using OVS.Client.Audio.Sounds;
using OVS.Client.Dialogs;
using OVS.Client.Input;
using OVS.Client.Localization;
using OVS.Client.Models;
using OVS.Client.Net;
using OVS.Client.Services;
using OVS.Client.Settings;
using OVS.Client.ViewModels;
using OVS.Tests.TestSupport;

namespace OVS.Tests.Client.ViewModels;

/// <summary>
/// A <see cref="ConnectionViewModel"/> without a main view model: it is built on the parts it takes (an engine without devices,
/// a manual clock, the connect seam) and on delegates that record what the connection asks of its owner. Everything runs on
/// one "UI thread" (<see cref="ReadAsync{T}"/>, <see cref="RunAsync"/>); <see cref="HoldPosts"/> keeps what is posted to it back until
/// <see cref="ReleasePostsAsync"/>. A test changes the recorded owner side (<see cref="Shown"/>, <see cref="ConfirmLeave"/>,
/// <see cref="OnPlay"/>, <see cref="OnEndLossStreaks"/>, <see cref="OnDialogs"/>, <see cref="OpenOverride"/>) before it starts a connect.
/// </summary>
sealed class ConnectionRig : IAsyncDisposable
{
    readonly TestDispatcher ui = new();
    readonly KeyPoller keys;
    readonly AudioEngine audio;
    readonly object gate = new();
    readonly List<ClientConnection> opened = [];
    readonly List<Action> held = [];
    bool holding;

    public ConnectionRig(string dir)
    {
        Time = new ManualTimeProvider();
        Seam = new ConnectSeam(dir, Time);
        keys = new KeyPoller(Time, new FakeKeys().IsDown);
        audio = new AudioEngine(keys, Time, hardware: null);
        var voiceLink = new VoiceLink(hear: audio.OnVoice, setSend: send => audio.Send = send, time: Time, log: LogLines.Add,
            reportUplink: loss => audio.Loss.Report(loss), endLossStreaks: EndLossStreaks);
        var engineSync = new EngineSync(audio, () => EngineConnectionState.Disconnected, () => null, _ => 1f);
        var selfTest = new SelfTest(() => false, _ => { }, _ => { }, LogLines.Add, () => null, _ => Task.CompletedTask);
        var bookmarks = new BookmarkList(() => Bookmarks, (_, _) => null, new LeaveTimer(Time, () => PostToUi), () => Dialogs.BookmarkList, () => { }, LogLines.Add);
        Connection = new ConnectionViewModel(time: Time, post: PostToUi, log: LogLines.Add, openConnection: OpenAsync, play: Play,
            voiceLink: voiceLink, engineSync: engineSync, selfTest: selfTest, bookmarks: bookmarks, icons: new ServerIconCache(dir),
            stats: ConnectionStats.CreateBlank(),
            askPassword: name => Dialogs.AskPassword?.Invoke(name) ?? Task.FromResult<PasswordAnswer?>(null),
            serverDialogs: () =>
            {
                OnDialogs?.Invoke();
                return Dialogs.Server;
            }, shownServer: () => Shown, show: shown => Shown = shown,
            setStatus: text => Status = text, readStatus: () => Status, addNotice: AddNotice, confirmLeave: () => ConfirmLeave(),
            closeAdmin: () => AdminClosedCount++);
    }

    public ConnectionViewModel Connection { get; }

    /// <summary>The clock of everything: the reconnect series, the server view models, the voice link and the connections.</summary>
    public ManualTimeProvider Time { get; }

    /// <summary>Where the connection opens its connections, unless <see cref="OpenOverride"/> is set.</summary>
    public ConnectSeam Seam { get; }

    /// <summary>Opens the connection in place of <see cref="Seam"/> (called without a task made first, so it can throw at once).</summary>
    public OpenConnection? OpenOverride { get; set; }

    /// <summary>Called by the connection's play delegate after the tone was noted; may throw.</summary>
    public Action<SoundEvent>? OnPlay { get; set; }

    /// <summary>Called by the voice link's end-loss-streaks delegate (at its start and its stop) after the call was counted; may throw.</summary>
    public Action? OnEndLossStreaks { get; set; }

    /// <summary>The answer to "may the administration's unsaved changes go?"; may throw.</summary>
    public Func<Task<bool>> ConfirmLeave { get; set; } = () => Task.FromResult(true);

    public DialogCallbacks Dialogs { get; set; } = new();

    /// <summary>Called whenever the connection asks for the server dialogs (an attach does, before it owns its connection); may throw.</summary>
    public Action? OnDialogs { get; set; }

    /// <summary>The saved bookmarks of the settings.</summary>
    public List<Bookmark> Bookmarks { get; } = [];

    /// <summary>The server view model the owner shows now (the connection shows and hides it).</summary>
    public ServerViewModel? Shown { get; set; }

    public string Status { get; private set; } = Strings.Status_NotConnected;

    public List<string> LogLines { get; } = [];

    public List<SoundEvent> Played { get; } = [];

    public List<Notice> Notices { get; } = [];

    public int AdminClosedCount { get; private set; }

    public int LossStreaksEndedCount { get; private set; }

    /// <summary>The connections the seam opened for the connection, oldest first.</summary>
    public IReadOnlyList<ClientConnection> Opened
    {
        get
        {
            lock (gate) return [.. opened];
        }
    }

    public Task<T> ReadAsync<T>(Func<T> read) => ui.ReadAsync(read);

    public Task<T> ReadUntilAsync<T>(Func<T> read, Func<T, bool> done) => ui.ReadUntilAsync(read, done);

    public Task RunAsync(Func<Task> work) => ui.RunAsync(work);

    /// <summary>Runs a call on the "UI thread" and completes when it has run.</summary>
    public Task InvokeAsync(Action action) => ui.ReadAsync(() =>
    {
        action();
        return 0;
    });

    public Task ConnectAsync(ConnectChoice choice) => RunAsync(() => Connection.ConnectAsync(choice));

    public Task DisconnectAsync() => RunAsync(Connection.DisconnectAsync);

    /// <summary>Keeps back everything posted to the "UI thread" from now on.</summary>
    public void HoldPosts()
    {
        lock (gate) holding = true;
    }

    /// <summary>Posts what was kept back, in its order, and completes when the "UI thread" has run it.</summary>
    public Task ReleasePostsAsync()
    {
        List<Action> due;
        lock (gate)
        {
            holding = false;
            due = [.. held];
            held.Clear();
        }
        foreach (var action in due) ui.Post(action);
        return ReadAsync(() => 0);
    }

    public async ValueTask DisposeAsync()
    {
        await DisconnectAsync();
        ui.Dispose();
        audio.Dispose();
        keys.Dispose();
    }

    void PostToUi(Action action)
    {
        lock (gate)
        {
            if (holding)
            {
                held.Add(action);
                return;
            }
        }
        ui.Post(action);
    }

    Task<ClientConnection> OpenAsync(ConnectChoice choice, CancellationToken token) => RecordAsync((OpenOverride ?? Seam.Connect)(choice, token));

    async Task<ClientConnection> RecordAsync(Task<ClientConnection> opening)
    {
        var connection = await opening;
        lock (gate) opened.Add(connection);
        return connection;
    }

    void Play(SoundEvent sound)
    {
        Played.Add(sound);
        OnPlay?.Invoke(sound);
    }

    void EndLossStreaks()
    {
        LossStreaksEndedCount++;
        OnEndLossStreaks?.Invoke();
    }

    void AddNotice(string text, NoticeKind kind) => Notices.Add(new Notice(Time.GetUtcNow().UtcDateTime, text, kind));
}
