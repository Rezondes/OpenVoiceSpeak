using OVS.Server.Logging;
using OVS.Shared.Identity;
using OVS.Shared.Permissions;
using OVS.Shared.Protocol;
using OVS.Tests.TestSupport;

namespace OVS.Tests.Server;

public sealed class ServerLogsTests : IDisposable
{
    readonly string dir = Directory.CreateTempSubdirectory("ovs-logs-").FullName;

    public void Dispose() => Directory.Delete(dir, true);

    static string Day(TimeProvider time, int offsetDays = 0) =>
        DateOnly.FromDateTime(time.GetLocalNow().DateTime).AddDays(offsetDays).ToString("yyyy-MM-dd");

    static string Read(string folder) =>
        Directory.Exists(folder)
            ? string.Join("\n", Directory.GetFiles(folder, "*.log").Order().Select(ReadShared))
            : "";

    /// <summary>Like `tail`: reads while the server may be appending.</summary>
    static string ReadShared(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        return new StreamReader(stream).ReadToEnd();
    }

    [Fact]
    public async Task ReaderLockingTheFile_LineIsNotLost()
    {
        // Regression: File.ReadAllText (and editors) lock the file against writers; the line used to be dropped.
        var logs = new ServerLogs(dir, 30, new ManualTimeProvider(), _ => { });
        logs.Server("eins");
        var path = Directory.GetFiles(Path.Combine(dir, "logs", "server")).Single();
        var reader = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        var release = Task.Delay(40).ContinueWith(_ => reader.Dispose());

        logs.Server("zwei");
        await release;
        Assert.Contains("zwei", File.ReadAllText(path));
    }

    static string ServerLog(TestServer s) => Read(Path.Combine(s.DataDir, "logs", "server"));
    static string ChannelLog(TestServer s, Guid id) => Read(Path.Combine(s.DataDir, "logs", "channels", id.ToString()));

    /// <summary>Log lines are written under the server lock; clients may see the delta a moment earlier.</summary>
    static async Task Eventually(Func<string> read, params string[] expected)
    {
        string text = "";
        for (int i = 0; i < 60; i++)
        {
            text = read();
            if (expected.All(text.Contains)) return;
            await Task.Delay(50);
        }
        Assert.Fail($"Erwartet {string.Join(" | ", expected.Where(e => !text.Contains(e)))} in:\n{text}");
    }

    // ---- ServerLogs itself ----

    [Fact]
    public void ServerLine_GoesToConsoleAndDailyFile()
    {
        var time = new ManualTimeProvider();
        var console = new List<string>();
        var logs = new ServerLogs(dir, 30, time, console.Add);
        logs.Server("Hallo Welt");

        Assert.Contains(console, l => l.EndsWith("Hallo Welt"));
        var file = Path.Combine(dir, "logs", "server", Day(time) + ".log");
        Assert.Matches(@"^\d{4}-\d{2}-\d{2} \d{2}:\d{2}:\d{2}\.\d{3} Hallo Welt", File.ReadAllText(file));
    }

    [Fact]
    public void DayChange_StartsNewFile()
    {
        var time = new ManualTimeProvider();
        var logs = new ServerLogs(dir, 30, time, _ => { });
        logs.Server("eins");
        var first = Day(time);
        time.Advance(TimeSpan.FromDays(1));
        logs.Server("zwei");

        Assert.Contains("eins", File.ReadAllText(Path.Combine(dir, "logs", "server", first + ".log")));
        Assert.Contains("zwei", File.ReadAllText(Path.Combine(dir, "logs", "server", Day(time) + ".log")));
    }

    [Fact]
    public void ChannelLine_GoesToChannelFolder_NotConsole()
    {
        var time = new ManualTimeProvider();
        var console = new List<string>();
        var logs = new ServerLogs(dir, 30, time, console.Add);
        var id = Guid.NewGuid();
        logs.Channel(id, "Raid", "anna hat den Channel betreten");

        Assert.Empty(console);
        Assert.Contains("[Raid] anna hat den Channel betreten", File.ReadAllText(Path.Combine(dir, "logs", "channels", id.ToString(), Day(time) + ".log")));
    }

    void Touch(string folder, string day)
    {
        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, day + ".log"), "alt");
    }

    [Fact]
    public void OldFiles_DeletedAfterRetention()
    {
        var time = new ManualTimeProvider();
        var server = Path.Combine(dir, "logs", "server");
        var channel = Path.Combine(dir, "logs", "channels", Guid.NewGuid().ToString());
        Touch(server, Day(time, -31));
        Touch(server, Day(time, -5));
        Touch(channel, Day(time, -40));

        var logs = new ServerLogs(dir, 30, time, _ => { }); // cleanup at start
        Assert.False(File.Exists(Path.Combine(server, Day(time, -31) + ".log")));
        Assert.True(File.Exists(Path.Combine(server, Day(time, -5) + ".log")));
        Assert.False(File.Exists(Path.Combine(channel, Day(time, -40) + ".log")));

        time.Advance(TimeSpan.FromDays(26)); // the -5 file is now 31 days old: cleanup at day change
        logs.Server("neuer Tag");
        Assert.False(File.Exists(Path.Combine(server, Day(time, -31) + ".log")));
    }

    [Fact]
    public void RetentionZero_KeepsAll()
    {
        var time = new ManualTimeProvider();
        var server = Path.Combine(dir, "logs", "server");
        Touch(server, Day(time, -400));
        new ServerLogs(dir, 0, time, _ => { }).Server("x");
        Assert.True(File.Exists(Path.Combine(server, Day(time, -400) + ".log")));
    }

    [Fact]
    public void WriteFailure_DoesNotThrow_ReportsOnce()
    {
        File.WriteAllText(Path.Combine(dir, "logs"), "ich bin eine Datei, kein Ordner");
        var console = new List<string>();
        var logs = new ServerLogs(dir, 30, new ManualTimeProvider(), console.Add);
        logs.Server("eins");
        logs.Channel(Guid.NewGuid(), "Raid", "zwei");
        logs.Server("drei");
        Assert.Single(console, l => l.Contains("Log konnte nicht geschrieben werden"));
        Assert.Contains(console, l => l.EndsWith("drei")); // the console keeps working
    }

    // ---- what the server logs ----

    [Fact]
    public async Task ConnectDisconnectRejected_Logged()
    {
        await using var server = await TestServer.StartAsync();
        var anna = await TestClient.ConnectAsync(server, "anna");
        await using (var dup = await TestClient.OpenAsync(server.Port))
            Assert.IsType<Rejected>(await dup.HandshakeAsync("ANNA"));
        await anna.DisposeAsync();

        await Eventually(() => ServerLog(server),
            "anna verbunden (", "abgelehnt: NicknameTaken", "127.0.0.1", "anna getrennt (vom Client beendet)");
    }

    [Fact]
    public async Task AdminActions_LoggedWithActor()
    {
        var adminId = ClientIdentity.Create();
        await using var server = await TestServer.StartAsync(TestServer.Grant(adminId, "Admin"));
        await using var admin = await TestClient.ConnectAsync(server, "chef", adminId);
        var g1 = await TestClient.ConnectAsync(server, "gast1");
        var g2 = await TestClient.ConnectAsync(server, "gast2");

        await admin.SendAsync(new CreateGroup("Team", Permission.Speak));
        var team = (await admin.WaitForAsync<GroupsChanged>()).Groups.Single(g => g.Name == "Team");
        await admin.SendAsync(new UpdateGroup(team.Id, "Team 2", Permission.Speak | Permission.SpeakLinked));
        await admin.SendAsync(new AssignGroup(g1.Identity.Fingerprint, team.Id));
        await admin.SendAsync(new UnassignGroup(g1.Identity.Fingerprint, team.Id));
        await admin.SendAsync(new DeleteGroup(team.Id));
        await admin.SendAsync(new UpdateServerSettings("Neuer Name", "Hallo", "pw"));
        await admin.SendAsync(new SetServerMute(g1.Id, true));
        await admin.SendAsync(new Kick(g1.Id, "Spam"));
        await admin.SendAsync(new Ban(g2.Id, "Troll", 60, true));
        await admin.SendAsync(new ListBans { RequestId = "l" });
        var ban = Assert.Single((await admin.WaitForAsync<BanList>(b => b.RequestId == "l")).Bans);
        await admin.SendAsync(new Unban(ban.Id));

        await Eventually(() => ServerLog(server),
            "Gruppe 'Team' angelegt von chef",
            "Gruppe 'Team' geändert von chef: Name 'Team' -> 'Team 2'",
            "Gruppe 'Team 2' an gast1 vergeben von chef",
            "Gruppe 'Team 2' von gast1 entfernt von chef",
            "Gruppe 'Team 2' gelöscht von chef",
            "Servereinstellungen geändert von chef: Name 'Testserver' -> 'Neuer Name', Willkommenstext geändert, Passwort gesetzt",
            "gast1 serverseitig stummgeschaltet von chef",
            "gast1 wurde von chef gekickt: Spam",
            "gast2 wurde von chef gebannt für 60 Minuten mit IP: Troll",
            "Bann von gast2 aufgehoben von chef");
        await g1.DisposeAsync();
        await g2.DisposeAsync();
    }

    [Fact]
    public async Task JoinMoveLeave_LoggedInChannelLogs()
    {
        var adminId = ClientIdentity.Create();
        await using var server = await TestServer.StartAsync(TestServer.Grant(adminId, "Admin"));
        await using var admin = await TestClient.ConnectAsync(server, "chef", adminId);
        var lobby = admin.Welcome.Snapshot.DefaultChannelId;
        var gast = await TestClient.ConnectAsync(server, "gast");
        await admin.SendAsync(new CreateChannel("Raid", ""));
        var raid = (await admin.WaitForAsync<ChannelAdded>()).Channel.Id;

        await gast.SendAsync(new JoinChannel(raid));
        await admin.WaitForAsync<UserUpdated>(u => u.User.SessionId == gast.Id && u.User.ChannelId == raid);
        await admin.SendAsync(new MoveUser(gast.Id, lobby));
        await admin.WaitForAsync<UserUpdated>(u => u.User.SessionId == gast.Id && u.User.ChannelId == lobby);
        await gast.DisposeAsync();

        await Eventually(() => ChannelLog(server, lobby),
            "[Lobby] gast hat den Channel betreten (verbunden)",
            "[Lobby] gast hat den Channel verlassen (wechselt nach Raid)",
            "[Lobby] gast wurde von chef aus Raid hierher verschoben",
            "[Lobby] gast hat den Channel verlassen (vom Client beendet)");
        await Eventually(() => ChannelLog(server, raid),
            "[Raid] Channel angelegt von chef",
            "[Raid] gast hat den Channel betreten (kommt aus Lobby)",
            "[Raid] gast wurde von chef nach Lobby verschoben");
        await Eventually(() => ServerLog(server), "Channel 'Raid' angelegt von chef");
        Assert.DoesNotContain("betreten", ServerLog(server)); // channel matters stay in channel logs
    }

    [Fact]
    public async Task Link_LoggedInBothChannels_EditDeleteLogged()
    {
        var adminId = ClientIdentity.Create();
        await using var server = await TestServer.StartAsync(TestServer.Grant(adminId, "Admin"));
        await using var admin = await TestClient.ConnectAsync(server, "chef", adminId);
        var lobby = admin.Welcome.Snapshot.DefaultChannelId;
        await admin.SendAsync(new CreateChannel("Raid", ""));
        var raid = (await admin.WaitForAsync<ChannelAdded>()).Channel.Id;

        await admin.SendAsync(new LinkChannels(lobby, raid));
        await admin.WaitForAsync<ChannelsLinked>();
        await admin.SendAsync(new EditChannel(raid, "Raid 2", "neu", 7));
        await admin.WaitForAsync<ChannelUpdated>();
        await admin.SendAsync(new DeleteChannel(raid));
        await admin.WaitForAsync<ChannelRemoved>();

        await Eventually(() => ChannelLog(server, lobby),
            "[Lobby] Link zu Raid gesetzt von chef", "[Lobby] Link zu Raid 2 entfernt (Channel gelöscht)");
        await Eventually(() => ChannelLog(server, raid),
            "[Raid] Link zu Lobby gesetzt von chef",
            "[Raid 2] Channel geändert von chef: Name 'Raid' -> 'Raid 2', Beschreibung geändert, Reihenfolge 1 -> 7",
            "[Raid 2] Channel gelöscht von chef");
        await Eventually(() => ServerLog(server), "Channel 'Raid 2' gelöscht von chef");
    }

    [Fact]
    public async Task TokenAndPassword_NeverInFiles()
    {
        await using var server = await TestServer.StartAsync();
        var token = server.State.PendingAdminToken!;
        await using var a = await TestClient.ConnectAsync(server, "anna");
        await a.SendAsync(new RedeemAdminToken(token));
        await a.WaitForAsync<UserUpdated>(u => u.User.SessionId == a.Id && u.User.Permissions == Permission.All);
        await a.SendAsync(new UpdateServerSettings("S", "", "geheim-123"));
        await a.WaitForAsync<ServerSettingsChanged>();

        await Eventually(() => ServerLog(server), "Admin-Token erzeugt", "anna hat das Admin-Token eingelöst", "Passwort gesetzt");
        var all = string.Join("\n", Directory.GetFiles(Path.Combine(server.DataDir, "logs"), "*.log", SearchOption.AllDirectories).Select(ReadShared));
        Assert.DoesNotContain(token, all);
        Assert.DoesNotContain("geheim-123", all);
        Assert.Contains(server.Log, l => l.Contains(token)); // the console still shows it
    }

    [Fact]
    public async Task RenamedChannel_SameFolderAfterRestart()
    {
        var adminId = ClientIdentity.Create();
        var server = await TestServer.StartAsync(TestServer.Grant(adminId, "Admin"));
        Guid raid;
        await using (var admin = await TestClient.ConnectAsync(server, "chef", adminId))
        {
            await admin.SendAsync(new CreateChannel("Raid", ""));
            raid = (await admin.WaitForAsync<ChannelAdded>()).Channel.Id;
            await admin.SendAsync(new EditChannel(raid, "Raid 2", "", 1));
            await admin.WaitForAsync<ChannelUpdated>();
        }
        server = await server.RestartAsync();
        await using var _ = server;
        await using var again = await TestClient.ConnectAsync(server, "chef", adminId);
        await again.SendAsync(new JoinChannel(raid));
        await again.WaitForAsync<UserUpdated>(u => u.User.ChannelId == raid);

        await Eventually(() => ChannelLog(server, raid), "[Raid] Channel angelegt von chef", "[Raid 2] chef hat den Channel betreten");
    }
}
