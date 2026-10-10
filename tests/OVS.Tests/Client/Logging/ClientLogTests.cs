using OVS.Client.Audio;
using OVS.Client.Dialogs;
using OVS.Client.Input;
using OVS.Client.Logging;
using OVS.Client.Net;
using OVS.Client.ViewModels;
using OVS.Shared.Protocol.Messages.Channels;
using OVS.Shared.Protocol.Messages.Groups;
using OVS.Shared.Protocol.Messages.ServerInfo;
using OVS.Tests.TestSupport;

namespace OVS.Tests.Client.Logging;

public sealed class ClientLogTests() : TempDirectoryTest("ovs-clientlog-", quiet: true)
{
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

    /// <summary>Looks again until every line is there, at most 15 s by the clock: the network and UI threads may come late on a busy runner.</summary>
    /// <param name="each">Runs before every look, e.g. the UI timer tick.</param>
    async Task<string> Eventually(Func<Task>? each, params string[] expected)
    {
        var text = "";
        var watch = System.Diagnostics.Stopwatch.StartNew();
        while (true)
        {
            if (each is not null) await each();
            text = LogText();
            if (expected.All(text.Contains) || watch.ElapsedMilliseconds > 15_000) break;
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
        var vm = await ui.InvokeAsync(() => Task.FromResult(MainViewModelSetup.Create(dir, ui.Post, trustServers: true)));
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
            await Run(ui, () => vm.Connection.ConnectAsync(new ConnectChoice("127.0.0.1", server.Port, "", null, false)));
            await Run(ui, () => vm.Connection.ConnectAsync(new ConnectChoice("127.0.0.1", server.Port, "anna", null, false)));
            await using (await TestClient.ConnectAsync(server, "bert"))
                await Eventually(null, "bert connected, channel 'Lobby'");
            await Eventually(() => Run(ui, () =>
            {
                vm.Tick();
                return Task.CompletedTask;
            }), "bert disconnected", "UDP now reachable");
            await Run(ui, vm.Connection.DisconnectAsync);

            var log = await Eventually(null,
                $"Connecting to 127.0.0.1:{server.Port} as anna",
                "Serverzertifikat neu für 127.0.0.1",
                "akzeptiert",
                "Abgelehnt: Ungültiger Nickname",
                "Connected to 'Testserver' as anna",
                "Disconnected (own action)");
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
            await Run(ui, () => vm.Connection.ConnectAsync(new ConnectChoice("127.0.0.1", server.Port, "anna", null, false)));
            await Run(ui, () => vm.Server!.SendAsync(new CreateChannel("Raid", "")));
            await Eventually(null, "Channel 'Raid' created");
            var raid = await ui.InvokeAsync(() => Task.FromResult(vm.Server!.Channels.Single(c => c.Name == "Raid").Id));
            await Run(ui, () => vm.Server!.JoinAsync(raid));
            await Run(ui, () => vm.Server!.ToggleMuteCommand.ExecuteAsync(null));

            await Eventually(null,
                "Request r1: Create channel 'Raid'",
                "Request r2: Join channel 'Raid'",
                "anna moves from 'Lobby' to 'Raid'",
                "Request r3: Own state: muted");
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
            await Run(ui, () => vm.Connection.ConnectAsync(new ConnectChoice("127.0.0.1", server.Port, "anna", "Serverpasswort-4711", false)));
            await Run(ui, () => vm.Server!.SendAsync(new RedeemAdminToken("Token-0815-geheim")));
            await Run(ui, () => vm.Server!.SendAsync(new UpdateServerSettings("Testserver", "", "Neues-Passwort-42")));

            var log = await Eventually(null, "with password", "Redeem admin token", "set password", "Server settings changed");
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
        var start = new ClientLog(dir, time);
        start.Write("erster Start");
        start.Flush(); // Package 124: written in the background, with the time it is written at
        Assert.False(File.Exists(old));
        Assert.True(File.Exists(recent));

        time.Advance(TimeSpan.FromMinutes(90));
        var second = Path.Combine(logs, Name());
        var log = new ClientLog(dir, time);
        log.Write("zweiter Start");
        log.Flush();
        Assert.Contains("erster Start", File.ReadAllText(first));
        Assert.DoesNotContain("zweiter Start", File.ReadAllText(first));
        Assert.Contains("zweiter Start", File.ReadAllText(second));

        time.Advance(TimeSpan.FromDays(2)); // still running two days later: new file, and cleanup at the day change
        log.Write("übermorgen");
        log.Flush();
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
        using var keys = new KeyPoller(TimeProvider.System); // no bindings: only simulated keys count
        using var engine = new AudioEngine(keys, TimeProvider.System, hardware: null) { Connected = true, SelfMuted = false, Send = (_, _) => { } };
        using (new OVS.Client.Diagnostics.AudioDebugLog(keys, engine, new ClientLog(dir, TimeProvider.System)))
        {
            engine.DeviceLifecycle.SetTone(440);
            keys.Simulate(ptt: true);
            await Eventually(null, "Audio-Debug: PTT gedrückt");
            // The rate comes from a real one-second timer: on a busy runner a single second may be stretched,
            // so wait (at most 15 s) for a full second of PTT instead of sampling a fixed one.
            var watch = System.Diagnostics.Stopwatch.StartNew();
            while (!Rates(LogText()).Any(r => r is >= 45 and <= 55) && watch.ElapsedMilliseconds < 15_000) await Task.Delay(100);
            keys.Simulate(ptt: false);
            await Eventually(null, "Audio-Debug: PTT losgelassen");
        }
        var log = LogText();
        Assert.Contains("Audio-Debug: PTT gedrückt", log);
        Assert.Contains("Audio-Debug: PTT losgelassen", log);
        // one full second of PTT at 20 ms per frame
        Assert.Contains(Rates(log), r => r is >= 45 and <= 55);
        Assert.Empty(Directory.GetFiles(dir, "audio-debug.log", SearchOption.AllDirectories));
    }

    /// <summary>Only complete numbers count: the log may be read while a line is being written.</summary>
    static List<int> Rates(string log) =>
        System.Text.RegularExpressions.Regex.Matches(log, @"Frames gesendet: (\d+)/s").Select(m => int.Parse(m.Groups[1].Value)).ToList();
}
