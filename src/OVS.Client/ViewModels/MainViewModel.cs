using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Net;
using System.Net.Sockets;
using System.Security.Authentication;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OVS.Client.Audio;
using OVS.Client.Input;
using OVS.Client.Net;
using OVS.Client.Settings;
using OVS.Shared.Identity;
using OVS.Shared.Protocol;

namespace OVS.Client.ViewModels;

public sealed record ConnectChoice(string Host, int Port, string Nickname, string? Password, bool SaveBookmark);

/// <summary>Application shell: settings, audio, and the current connection.</summary>
public sealed partial class MainViewModel : ObservableObject, IAsyncDisposable
{
    readonly string storageDir;
    readonly Action<Action> post;
    readonly KnownServers known;
    ClientConnection? connection;
    VoiceClient? voice;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsConnected))]
    ServerViewModel? server;

    [ObservableProperty] string status = "Nicht verbunden";
    [ObservableProperty] string pingText = "";
    [ObservableProperty] string transmitText = "";
    [ObservableProperty] string linkHint = "";

    /// <param name="useAudioDevices">False runs without microphone and speaker (tests, debug API with test tone).</param>
    public MainViewModel(string storageDir, Action<Action> post, bool useAudioDevices = true)
    {
        this.storageDir = storageDir;
        this.post = post;
        known = new KnownServers(Path.Combine(storageDir, "known_servers.json"));
        Settings = ClientSettings.Load(storageDir, out var warning);
        if (warning is not null) AddNotice(warning);

        Keys = new KeyPoller();
        Audio = new AudioEngine(Keys, useAudioDevices);
        Audio.TransmitChanged += target => post(() =>
        {
            TransmitText = target switch
            {
                null => "",
                Shared.Voice.VoiceHeader.TargetLinked => "Sendet an eigenen Channel und Links",
                _ => "Sendet",
            };
            Server?.SetSelfTransmitting(target);
        });
        Audio.SpeakersChanged += active => post(() => Server?.OnSpeakers(active));
        ApplySettings(Settings);
    }

    public ClientSettings Settings { get; private set; }
    public KeyPoller Keys { get; }
    public AudioEngine Audio { get; }
    public ObservableCollection<string> Notices { get; } = [];
    public bool IsConnected => Server is not null;
    public Dialogs Dialogs { get; set; } = new();
    public Func<TofuPrompt, Task<bool>> ConfirmTofu { get; set; } = _ => Task.FromResult(false);

    public void ApplySettings(ClientSettings settings)
    {
        Settings = settings.Clamp();
        Settings.Save(storageDir);
        Keys.PttKey = Settings.PttKey;
        Keys.LinkPttKey = Settings.LinkPttKey;
        if (Audio.Configure(Settings) is { } warning) AddNotice(warning);
    }

    public async Task ConnectAsync(ConnectChoice choice)
    {
        await DisconnectAsync();
        if (choice.SaveBookmark)
        {
            Settings.Bookmarks.RemoveAll(b => b.Host == choice.Host && b.Port == choice.Port);
            Settings.Bookmarks.Insert(0, new Bookmark($"{choice.Host}:{choice.Port}", choice.Host, choice.Port, choice.Nickname));
            Settings.Save(storageDir);
        }

        Status = $"Verbinde mit {choice.Host}:{choice.Port} ...";
        try
        {
            using var identity = ClientStorage.LoadOrCreateIdentity(storageDir);
            var conn = await ClientConnection.ConnectAsync(choice.Host, choice.Port, identity, choice.Nickname,
                string.IsNullOrEmpty(choice.Password) ? null : choice.Password, known, ConfirmTofu);

            var vm = new ServerViewModel(new StateMirror(conn.Welcome), conn.SendAsync, TimeProvider.System, Dialogs);
            vm.Notice += AddNotice;
            vm.PropertyChanged += OnServerPropertyChanged;
            conn.MessageReceived += m => post(() => vm.Apply(m));
            conn.Disconnected += (reason, detail) => post(() => OnDisconnected(conn, reason, detail));

            var udp = new VoiceClient(new IPEndPoint(conn.RemoteAddress, choice.Port), conn.Welcome.SessionId,
                Convert.FromBase64String(conn.Welcome.VoiceKey));
            udp.VoiceReceived += Audio.OnVoice;
            Audio.Send = udp.SendVoice;
            udp.Start();

            connection = conn;
            voice = udp;
            Server = vm;
            SyncAudioFlags();
            Status = $"Verbunden mit {vm.ServerName}";
            if (vm.WelcomeText.Length > 0) AddNotice(vm.WelcomeText);
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
    }

    [RelayCommand]
    public Task DisconnectAsync() => DisconnectAsync("Nicht verbunden");

    /// <param name="status">Set before tearing down, so nobody ever sees "disconnected" with a stale status.</param>
    async Task DisconnectAsync(string status)
    {
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
        AddNotice(text);
        _ = DisconnectAsync(text);
    }

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
        PingText = connection?.LastRoundTrip is { } rtt
            ? $"Ping {rtt.TotalMilliseconds:0} ms" + (voice?.Reachable == false ? ", UDP nicht erreichbar" : "")
            : "";
        LinkHint = Keys.LinkPttDown && Server is { HasSpeakLinked: false }
            ? "Kein Recht für Link-Übertragungen: du sprichst nur im eigenen Channel."
            : "";
    }

    public void AddNotice(string text)
    {
        Notices.Insert(0, $"{DateTime.Now:HH:mm:ss}  {text}");
        while (Notices.Count > 200) Notices.RemoveAt(Notices.Count - 1);
    }

    public async ValueTask DisposeAsync()
    {
        await DisconnectAsync();
        Audio.Dispose();
        Keys.Dispose();
    }
}
