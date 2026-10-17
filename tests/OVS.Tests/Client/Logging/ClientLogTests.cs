using System.Globalization;
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
    async Task<string> EventuallyAsync(Func<Task>? each, params string[] expected)
    {
        var text = "";
        await AsyncWait.UntilAsync(async () =>
        {
            if (each is not null) await each();
            text = LogText();
            return expected.All(text.Contains);
        }, TimeSpan.FromSeconds(15));
        foreach (var value in expected) Assert.Contains(value, text, StringComparison.Ordinal);
        return text;
    }

    /// <summary>Real client (no audio devices) against a real server, with the profile's identity as admin.</summary>
    async Task<ClientRig> StartAsync(string password = "")
    {
        using var identity = IdentityFile.LoadOrCreate(dir);
#pragma warning disable CA2000 // handed to the rig; the test stops it
        var server = await TestServer.StartAsync(TestServer.Grant(identity, "Admin"), password: password);
        var ui = new TestDispatcher();
#pragma warning restore CA2000
        var viewModel = await ui.InvokeAsync(() => Task.FromResult(MainViewModelSetup.Create(dir, ui.Post, trustServers: true)));
        return new ClientRig(server, ui, viewModel);
    }

    static async Task StopAsync(TestServer server, TestDispatcher ui, MainViewModel viewModel)
    {
        await RunAsync(ui, () => viewModel.DisposeAsync().AsTask());
        ui.Dispose();
        await server.DisposeAsync();
    }

    static Task<object?> RunAsync(TestDispatcher ui, Func<Task> work) => ui.InvokeAsync<object?>(async () =>
    {
        await work();
        return null;
    });

    [Fact]
    public async Task Connect_Disconnect_Logged()
    {
        var (server, ui, viewModel) = await StartAsync();
        try
        {
            await RunAsync(ui, () => viewModel.Connection.ConnectAsync(new ConnectChoice("127.0.0.1", server.Port, "", null, false)));
            await RunAsync(ui, () => viewModel.Connection.ConnectAsync(new ConnectChoice("127.0.0.1", server.Port, "anna", null, false)));
            await using (await TestClient.ConnectAsync(server, "bert"))
                await EventuallyAsync(null, "bert connected, channel 'Lobby'");
            await EventuallyAsync(() => RunAsync(ui, () =>
            {
                viewModel.Tick();
                return Task.CompletedTask;
            }), "bert disconnected", "UDP now reachable");
            await RunAsync(ui, viewModel.Connection.DisconnectAsync);

            var log = await EventuallyAsync(null,
                $"Connecting to 127.0.0.1:{server.Port} as anna",
                "Server certificate new for 127.0.0.1",
                "accepted",
                "Abgelehnt: Ungültiger Nickname",
                "Connected to 'Testserver' as anna",
                "Disconnected (own action)");
            Assert.Matches(@"\d{4}-\d{2}-\d{2} \d{2}:\d{2}:\d{2}\.\d{3} Settings: mode PushToTalk", log);
        }
        finally
        {
            await StopAsync(server, ui, viewModel);
        }
    }

    [Fact]
    public async Task OwnActions_Taken_Logged()
    {
        var (server, ui, viewModel) = await StartAsync();
        try
        {
            await RunAsync(ui, () => viewModel.Connection.ConnectAsync(new ConnectChoice("127.0.0.1", server.Port, "anna", null, false)));
            await RunAsync(ui, () => viewModel.Server!.SendAsync(new CreateChannel("Raid", "")));
            await EventuallyAsync(null, "Channel 'Raid' created");
            var raid = await ui.InvokeAsync(() => Task.FromResult(viewModel.Server!.Tree.Channels.Single(channel => channel.Name == "Raid").Id));
            await RunAsync(ui, () => viewModel.Server!.JoinAsync(raid));
            await RunAsync(ui, () => viewModel.Server!.SelfState.ToggleMuteCommand.ExecuteAsync(null));

            await EventuallyAsync(null,
                "Request r1: Create channel 'Raid'",
                "Request r2: Join channel 'Raid'",
                "anna moves from 'Lobby' to 'Raid'",
                "Request r3: Own state: muted");
        }
        finally
        {
            await StopAsync(server, ui, viewModel);
        }
    }

    [Fact]
    public async Task PasswordAndToken_Sent_NeverLogged()
    {
        var (server, ui, viewModel) = await StartAsync(password: "Serverpasswort-4711");
        try
        {
            await RunAsync(ui, () => viewModel.Connection.ConnectAsync(new ConnectChoice("127.0.0.1", server.Port, "anna", "Serverpasswort-4711", false)));
            await RunAsync(ui, () => viewModel.Server!.SendAsync(new RedeemAdminToken("Token-0815-geheim")));
            await RunAsync(ui, () => viewModel.Server!.SendAsync(new UpdateServerSettings("Testserver", "", "Neues-Passwort-42")));

            var log = await EventuallyAsync(null, "with password", "Redeem admin token", "set password", "Server settings changed");
            Assert.DoesNotContain("Serverpasswort-4711", log, StringComparison.Ordinal);
            Assert.DoesNotContain("Token-0815-geheim", log, StringComparison.Ordinal);
            Assert.DoesNotContain("Neues-Passwort-42", log, StringComparison.Ordinal);
        }
        finally
        {
            await StopAsync(server, ui, viewModel);
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
        Assert.Contains("erster Start", File.ReadAllText(first), StringComparison.Ordinal);
        Assert.DoesNotContain("zweiter Start", File.ReadAllText(first), StringComparison.Ordinal);
        Assert.Contains("zweiter Start", File.ReadAllText(second), StringComparison.Ordinal);

        time.Advance(TimeSpan.FromDays(2)); // still running two days later: new file, and cleanup at the day change
        log.Write("übermorgen");
        log.Flush();
        Assert.DoesNotContain("übermorgen", File.ReadAllText(second), StringComparison.Ordinal);
        Assert.Contains("übermorgen", File.ReadAllText(Path.Combine(logs, Name())), StringComparison.Ordinal);
        Assert.False(File.Exists(recent));
    }

    [Fact]
    public void Write_FileFails_DoesNotThrow()
    {
        File.WriteAllText(Path.Combine(dir, "logs"), "blockiert den Ordner");
        var log = new ClientLog(dir, TimeProvider.System);
        log.Write("eins");
        log.Write("zwei");
    }

    [Fact]
    public async Task AudioDebug_On_WritesToClientLog()
    {
        using var keys = new KeyPoller(TimeProvider.System); // no bindings: only simulated keys count
        using var engine = new AudioEngine(keys, TimeProvider.System, hardware: null) { Connected = true, SelfMuted = false, Send = (_, _, _) => { } };
        using (new OVS.Client.Diagnostics.AudioDebugLog(keys, engine, new ClientLog(dir, TimeProvider.System)))
        {
            engine.DeviceLifecycle.SetTone(440);
            keys.Simulate(ptt: true);
            await EventuallyAsync(null, "Audio debug: PTT down");
            // The rate comes from a real one-second timer: on a busy runner a single second may be stretched,
            // so wait (at most 15 s) for a full second of PTT instead of sampling a fixed one.
            await AsyncWait.UntilAsync(() => Rates(LogText()).Any(value => value is >= 45 and <= 55), TimeSpan.FromSeconds(15));
            keys.Simulate(ptt: false);
            await EventuallyAsync(null, "Audio debug: PTT up");
        }
        var log = LogText();
        Assert.Contains("Audio debug: PTT down", log, StringComparison.Ordinal);
        Assert.Contains("Audio debug: PTT up", log, StringComparison.Ordinal);
        // one full second of PTT at 20 ms per frame
        Assert.Contains(Rates(log), value => value is >= 45 and <= 55);
        Assert.Empty(Directory.GetFiles(dir, "audio-debug.log", SearchOption.AllDirectories));
    }

    /// <summary>Only complete numbers count: the log may be read while a line is being written.</summary>
    static List<int> Rates(string log) =>
        System.Text.RegularExpressions.Regex.Matches(log, @"frames sent: (\d+)/s").Select(match => int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture)).ToList();
}
