using OVS.Client.Audio;
using OVS.Client.Input;
using OVS.Client.Logging;
using OVS.Client.Net;
using OVS.Client.ViewModels;
using OVS.Shared.Permissions;
using OVS.Shared.Protocol;
using OVS.Tests.TestSupport;

namespace OVS.Tests.Client;

public sealed class ClientLogTests : IDisposable
{
    static readonly Guid Lobby = Guid.NewGuid(), Raid = Guid.NewGuid(), Mods = Guid.NewGuid();
    readonly string dir = Directory.CreateTempSubdirectory("ovs-clientlog-").FullName;

    public void Dispose()
    {
        try
        {
            Directory.Delete(dir, true);
        }
        catch (IOException)
        {
        }
    }

    static UserInfo User(uint id, Guid channel, string nick, bool muted = false) =>
        new(id, "fp" + id, nick, channel, muted, false, false, Permission.Speak, []);

    static StateMirror Mirror() => new(new Welcome(1, "", new ServerSnapshot(
        new ServerSettingsInfo("S", "", false), Lobby,
        [new ChannelInfo(Lobby, "Lobby", "", 0), new ChannelInfo(Raid, "Raid", "", 1)], [],
        [new GroupInfo(Mods, "Moderator", Permission.Speak)],
        [User(1, Lobby, "ich"), User(2, Lobby, "bert")])));

    public static TheoryData<Message, string> Deltas => new()
    {
        { new ChannelAdded(new ChannelInfo(Guid.NewGuid(), "Neu", "", 2)), "Channel 'Neu' angelegt" },
        { new ChannelUpdated(new ChannelInfo(Raid, "Raid 2", "", 1)), "Channel 'Raid' umbenannt in 'Raid 2'" },
        { new ChannelRemoved(Raid), "Channel 'Raid' gelöscht" },
        { new UserJoined(User(3, Raid, "carla")), "carla verbunden, Channel 'Raid'" },
        { new UserUpdated(User(2, Raid, "bert")), "bert wechselt von 'Lobby' nach 'Raid'" },
        { new UserUpdated(User(2, Lobby, "bert", muted: true)), "bert stumm" },
        { new UserLeft(2), "bert getrennt" },
        { new ChannelsLinked(Lobby, Raid), "Channels 'Lobby' und 'Raid' verlinkt" },
        { new ChannelsUnlinked(Lobby, Raid), "Link zwischen 'Lobby' und 'Raid' entfernt" },
        { new GroupsChanged([new GroupInfo(Mods, "Mods", Permission.Speak)]), "Gruppen geändert: Mods" },
        { new ServerSettingsChanged(new ServerSettingsInfo("Neu", "", true)), "Servereinstellungen geändert: Name 'Neu', mit Passwort" },
    };

    [Theory]
    [MemberData(nameof(Deltas))]
    public void Describe_Message(Message message, string expected) =>
        Assert.Equal(expected, ClientLog.Describe(message, Mirror()));

    [Fact]
    public void Describe_Request_UsesNames_NeverSecrets()
    {
        var mirror = Mirror();
        Assert.Equal("Anfrage r1: Channel 'Raid' betreten", ClientLog.Describe(new JoinChannel(Raid) { RequestId = "r1" }, mirror));
        Assert.Equal("Anfrage r2: bert nach 'Raid' verschieben", ClientLog.Describe(new MoveUser(2, Raid) { RequestId = "r2" }, mirror));
        Assert.Equal("Anfrage r3: Gruppe 'Moderator' an bert vergeben", ClientLog.Describe(new AssignGroup("fp2", Mods) { RequestId = "r3" }, mirror));
        Assert.Equal("Anfrage r4: Admin-Token einlösen", ClientLog.Describe(new RedeemAdminToken("geheim-token") { RequestId = "r4" }, mirror));
        Assert.Equal("Anfrage r5: Servereinstellungen ändern: Name 'S', Passwort setzen",
            ClientLog.Describe(new UpdateServerSettings("S", "", "geheim") { RequestId = "r5" }, mirror));
        Assert.Equal("Anfrage r6: Server-Logo setzen", ClientLog.Describe(new SetServerIcon("AAAA") { RequestId = "r6" }, mirror));
        Assert.Equal("Anfrage r7: Server-Logo entfernen", ClientLog.Describe(new SetServerIcon(null) { RequestId = "r7" }, mirror));
        Assert.Equal("Server hat kein Logo", ClientLog.Describe(new ServerIcon(null, null, null), mirror));
    }

    string LogText()
    {
        var folder = Path.Combine(dir, "logs");
        if (!Directory.Exists(folder)) return "";
        return string.Join("\n", Directory.GetFiles(folder, "client-*.log").Order().Select(path =>
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            return new StreamReader(stream).ReadToEnd();
        }));
    }

    /// <param name="each">Runs before every look, e.g. the UI timer tick.</param>
    async Task<string> Eventually(Func<Task>? each, params string[] expected)
    {
        var text = "";
        for (int i = 0; i < 60; i++)
        {
            if (each is not null) await each();
            text = LogText();
            if (expected.All(text.Contains)) return text;
            await Task.Delay(50);
        }
        foreach (var e in expected) Assert.Contains(e, text);
        return text;
    }

    /// <summary>Real client (no audio devices) against a real server, with the profile's identity as admin.</summary>
    async Task<(TestServer Server, TestDispatcher Ui, MainViewModel Vm)> StartAsync(string password = "")
    {
        using var identity = ClientStorage.LoadOrCreateIdentity(dir);
        var server = await TestServer.StartAsync(TestServer.Grant(identity, "Admin"), password: password);
        var ui = new TestDispatcher();
        var vm = await ui.InvokeAsync(() => Task.FromResult(new MainViewModel(dir, ui.Post, useAudioDevices: false)));
        vm.ConfirmTofu = _ => Task.FromResult(true);
        return (server, ui, vm);
    }

    static async Task StopAsync(TestServer server, TestDispatcher ui, MainViewModel vm)
    {
        await Run(ui, () => vm.DisposeAsync().AsTask());
        ui.Dispose();
        await server.DisposeAsync();
    }

    static Task Run(TestDispatcher ui, Func<Task> work) => ui.InvokeAsync<object?>(async () =>
    {
        await work();
        return null;
    });

    [Fact]
    public async Task Connect_Disconnect_Logged()
    {
        var (server, ui, vm) = await StartAsync();
        try
        {
            await Run(ui, () => vm.ConnectAsync(new ConnectChoice("127.0.0.1", server.Port, "", null, false)));
            await Run(ui, () => vm.ConnectAsync(new ConnectChoice("127.0.0.1", server.Port, "anna", null, false)));
            await using (await TestClient.ConnectAsync(server, "bert"))
                await Eventually(null, "bert verbunden, Channel 'Lobby'");
            await Eventually(() => Run(ui, () =>
            {
                vm.Tick();
                return Task.CompletedTask;
            }), "bert getrennt", "UDP erreichbar");
            await Run(ui, vm.DisconnectAsync);

            var log = await Eventually(null,
                $"Verbinde mit 127.0.0.1:{server.Port} als anna",
                "Serverzertifikat neu für 127.0.0.1",
                "akzeptiert",
                "Abgelehnt: Ungültiger Nickname",
                "Verbunden mit 'Testserver' als anna",
                "Verbindung getrennt (eigene Aktion)");
            Assert.Matches(@"\d{4}-\d{2}-\d{2} \d{2}:\d{2}:\d{2}\.\d{3} Einstellungen: Modus PushToTalk", log);
        }
        finally
        {
            await StopAsync(server, ui, vm);
        }
    }

    [Fact]
    public async Task OwnActions_Logged()
    {
        var (server, ui, vm) = await StartAsync();
        try
        {
            await Run(ui, () => vm.ConnectAsync(new ConnectChoice("127.0.0.1", server.Port, "anna", null, false)));
            await Run(ui, () => vm.Server!.CreateChannelAsync("Raid", ""));
            await Eventually(null, "Channel 'Raid' angelegt");
            var raid = await ui.InvokeAsync(() => Task.FromResult(vm.Server!.Channels.Single(c => c.Name == "Raid").Id));
            await Run(ui, () => vm.Server!.JoinAsync(raid));
            await Run(ui, () => vm.Server!.ToggleMuteCommand.ExecuteAsync(null));

            await Eventually(null,
                "Anfrage r1: Channel 'Raid' anlegen",
                "Anfrage r2: Channel 'Raid' betreten",
                "anna wechselt von 'Lobby' nach 'Raid'",
                "Anfrage r3: Eigener Status: stumm");
        }
        finally
        {
            await StopAsync(server, ui, vm);
        }
    }

    [Fact]
    public async Task PasswordAndToken_NeverLogged()
    {
        var (server, ui, vm) = await StartAsync(password: "Serverpasswort-4711");
        try
        {
            await Run(ui, () => vm.ConnectAsync(new ConnectChoice("127.0.0.1", server.Port, "anna", "Serverpasswort-4711", false)));
            await Run(ui, () => vm.Server!.SendAsync(new RedeemAdminToken("Token-0815-geheim")));
            await Run(ui, () => vm.Server!.SendAsync(new UpdateServerSettings("Testserver", "", "Neues-Passwort-42")));

            var log = await Eventually(null, "mit Passwort", "Admin-Token einlösen", "Passwort setzen", "Servereinstellungen geändert");
            Assert.DoesNotContain("Serverpasswort-4711", log);
            Assert.DoesNotContain("Token-0815-geheim", log);
            Assert.DoesNotContain("Neues-Passwort-42", log);
        }
        finally
        {
            await StopAsync(server, ui, vm);
        }
    }

    [Fact]
    public void EachStart_OwnFile_OldFilesDeleted()
    {
        var logs = Path.Combine(dir, "logs");
        Directory.CreateDirectory(logs);
        var time = new ManualTimeProvider(new DateTimeOffset(2026, 9, 27, 12, 0, 0, TimeSpan.Zero));
        string Name(int offsetDays = 0) => $"client-{time.GetLocalNow().AddDays(offsetDays):yyyy-MM-dd_HH-mm-ss}.log";
        var old = Path.Combine(logs, Name(-31));
        var recent = Path.Combine(logs, Name(-29));
        File.WriteAllText(old, "alt");
        File.WriteAllText(recent, "neu");

        var first = Path.Combine(logs, Name());
        new ClientLog(dir, time).Write("erster Start");
        Assert.False(File.Exists(old));
        Assert.True(File.Exists(recent));

        time.Advance(TimeSpan.FromMinutes(90));
        var second = Path.Combine(logs, Name());
        var log = new ClientLog(dir, time);
        log.Write("zweiter Start");
        Assert.Contains("erster Start", File.ReadAllText(first));
        Assert.DoesNotContain("zweiter Start", File.ReadAllText(first));
        Assert.Contains("zweiter Start", File.ReadAllText(second));

        time.Advance(TimeSpan.FromDays(2)); // still running two days later: new file, and cleanup at the day change
        log.Write("übermorgen");
        Assert.DoesNotContain("übermorgen", File.ReadAllText(second));
        Assert.Contains("übermorgen", File.ReadAllText(Path.Combine(logs, Name())));
        Assert.False(File.Exists(recent));
    }

    [Fact]
    public void WriteFailure_DoesNotThrow()
    {
        File.WriteAllText(Path.Combine(dir, "logs"), "blockiert den Ordner");
        var log = new ClientLog(dir, TimeProvider.System);
        log.Write("eins");
        log.Write("zwei");
    }

    [Fact]
    public async Task AudioDebug_WritesToClientLog()
    {
        using var keys = new KeyPoller(); // no bindings: only simulated keys count
        using var engine = new AudioEngine(keys, useDevices: false) { Connected = true, SelfMuted = false, Send = (_, _) => { } };
        using (new OVS.Client.Debug.AudioDebugLog(keys, engine, new ClientLog(dir, TimeProvider.System)))
        {
            engine.SetTone(440);
            keys.Simulate(ptt: true);
            await Task.Delay(2300);
            keys.Simulate(ptt: false);
            await Task.Delay(100);
        }
        var log = LogText();
        Assert.Contains("Audio-Debug: PTT gedrückt", log);
        Assert.Contains("Audio-Debug: PTT losgelassen", log);
        // one full second of PTT at 20 ms per frame
        var rates = log.Split('\n').Where(l => l.Contains("Frames gesendet: "))
            .Select(l => int.Parse(l.Split("Frames gesendet: ")[1].Split('/')[0])).ToList();
        Assert.Contains(rates, r => r is >= 45 and <= 55);
        Assert.Empty(Directory.GetFiles(dir, "audio-debug.log", SearchOption.AllDirectories));
    }
}
