using OVS.Server.Logging;
using OVS.Shared.Identity;
using OVS.Shared.Permissions;
using OVS.Shared.Protocol.Messages.Channels;
using OVS.Shared.Protocol.Messages.Chat;
using OVS.Shared.Protocol.Messages.Connection;
using OVS.Shared.Protocol.Messages.Groups;
using OVS.Shared.Protocol.Messages.Links;
using OVS.Shared.Protocol.Messages.Moderation;
using OVS.Shared.Protocol.Messages.ServerInfo;
using OVS.Shared.Protocol.Messages.Users;
using OVS.Tests.TestSupport;

namespace OVS.Tests.Server.Logging;

public sealed class ServerLogWriterTests() : TempDirectoryTest("ovs-logs-", quiet: false)
{
    /// <summary>File name of a run started at the provider's current time (plus an offset).</summary>
    static string Start(TimeProvider time, int offsetDays = 0) =>
        time.GetLocalNow().AddDays(offsetDays).ToString("yyyy-MM-dd_HH-mm-ss");

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
    public void WriteServerLine_ReaderLocksTheFile_LineNotLost()
    {
        // Regression: File.ReadAllText (and editors) lock the file against writers; the line used to be dropped.
        var clock = new ManualTimeProvider();
        var logs = new ServerLogWriter(dir, 30, clock, _ => { });
        logs.WriteServerLine("eins");
        var path = Directory.GetFiles(Path.Combine(dir, "logs", "server")).Single();
        using (new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            logs.WriteServerLine("zwei");
            logs.WriteServerLine("drei");
            clock.Advance(TimeSpan.FromMilliseconds(20)); // still locked: the lines wait
        }
        Assert.DoesNotContain("zwei", File.ReadAllText(path));

        clock.Advance(TimeSpan.FromMilliseconds(20));
        var text = File.ReadAllText(path);
        Assert.True(text.IndexOf("eins", StringComparison.Ordinal) < text.IndexOf("zwei", StringComparison.Ordinal)
            && text.IndexOf("zwei", StringComparison.Ordinal) < text.IndexOf("drei", StringComparison.Ordinal), text);
    }

    [Fact]
    public void WriteServerLine_FileLockedPastTheRetries_ReportedOnce()
    {
        var clock = new ManualTimeProvider();
        var failures = new List<string>();
        var logs = new ServerLogWriter(dir, 30, clock, failures.Add);
        logs.WriteServerLine("eins", toConsole: false);
        var path = Directory.GetFiles(Path.Combine(dir, "logs", "server")).Single();
        using var reader = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);

        logs.WriteServerLine("zwei", toConsole: false);
        for (int tick = 0; tick < 300; tick++) clock.Advance(TimeSpan.FromMilliseconds(20));

        Assert.Single(failures);
    }

    static string ServerLog(TestServer server) => Read(Path.Combine(server.DataDir, "logs", "server"));
    static string ChannelLog(TestServer server, Guid id) => Read(Path.Combine(server.DataDir, "logs", "channels", id.ToString()));

    /// <summary>Log lines are written under the server lock; clients may see the delta a moment earlier.</summary>
    static async Task EventuallyAsync(Func<string> read, params string[] expected)
    {
        string text = "";
        // Generous: returns as soon as the lines are there, and a save on a slow CI disk can hold the lock for seconds.
        for (var deadline = DateTime.UtcNow.AddSeconds(30); DateTime.UtcNow < deadline;)
        {
            text = read();
            if (expected.All(text.Contains)) return;
            await Task.Delay(50);
        }
        text = read();
        if (expected.All(text.Contains)) return;
        Assert.Fail($"Erwartet {string.Join(" | ", expected.Where(value => !text.Contains(value)))} in:\n{text}");
    }

    // ---- ServerLogWriter itself ----

    [Fact]
    public void WriteServerLine_Line_GoesToConsoleAndFileOfThisStart()
    {
        var time = new ManualTimeProvider();
        var console = new List<string>();
        var logs = new ServerLogWriter(dir, 30, time, console.Add);
        var start = Start(time);
        time.Advance(TimeSpan.FromMinutes(5));
        logs.WriteServerLine("Hallo Welt");

        Assert.Contains(console, line => line.EndsWith("Hallo Welt"));
        var file = Path.Combine(dir, "logs", "server", start + ".log");
        Assert.Matches(@"^\d{4}-\d{2}-\d{2} \d{2}:\d{2}:\d{2}\.\d{3} Hallo Welt", File.ReadAllText(file));
    }

    [Fact]
    public void EachStart_OwnFile_SharedByServerAndChannelLogs()
    {
        var time = new ManualTimeProvider();
        var channel = Guid.NewGuid();
        var first = Start(time);
        var logs = new ServerLogWriter(dir, 30, time, _ => { });
        logs.WriteServerLine("Lauf eins");
        logs.WriteChannelLine(channel, "Raid", "Lauf eins");

        time.Advance(TimeSpan.FromMinutes(90));
        var second = Start(time);
        logs = new ServerLogWriter(dir, 30, time, _ => { });
        logs.WriteServerLine("Lauf zwei");
        logs.WriteChannelLine(channel, "Raid", "Lauf zwei");

        foreach (var folder in new[] { Path.Combine(dir, "logs", "server"), Path.Combine(dir, "logs", "channels", channel.ToString()) })
        {
            Assert.Equal([first + ".log", second + ".log"], Directory.GetFiles(folder).Select(Path.GetFileName).Order());
            Assert.DoesNotContain("zwei", File.ReadAllText(Path.Combine(folder, first + ".log")));
            Assert.DoesNotContain("eins", File.ReadAllText(Path.Combine(folder, second + ".log")));
        }
    }

    [Fact]
    public void DayChange_StartsNewFile_NamedAfterThatMoment()
    {
        var time = new ManualTimeProvider();
        var first = Start(time);
        var logs = new ServerLogWriter(dir, 30, time, _ => { });
        logs.WriteServerLine("eins");
        time.Advance(TimeSpan.FromDays(1));
        logs.WriteServerLine("zwei");

        Assert.Contains("eins", File.ReadAllText(Path.Combine(dir, "logs", "server", first + ".log")));
        Assert.Contains("zwei", File.ReadAllText(Path.Combine(dir, "logs", "server", Start(time) + ".log")));
    }

    [Fact]
    public void ChannelLine_GoesToChannelFolder_NotConsole()
    {
        var time = new ManualTimeProvider();
        var console = new List<string>();
        var logs = new ServerLogWriter(dir, 30, time, console.Add);
        var id = Guid.NewGuid();
        logs.WriteChannelLine(id, "Raid", "anna hat den Channel betreten");

        Assert.Empty(console);
        Assert.Contains("[Raid] anna hat den Channel betreten", File.ReadAllText(Path.Combine(dir, "logs", "channels", id.ToString(), Start(time) + ".log")));
    }

    [Fact]
    public void LineBreaksInText_Escaped_EntryStaysOneLine()
    {
        // Regression (CWE-117): chat text and reasons come from guests; a CR/LF used to add a forged, timestamped line.
        var time = new ManualTimeProvider();
        var logs = new ServerLogWriter(dir, 30, time, _ => { });
        var id = Guid.NewGuid();
        logs.WriteChannelLine(id, "Raid", "hi\r\n2026-01-01 00:00:00.000 [Raid] chef gebannt\u0085a\u2028b\u2029c\td\0e\u001b[31m");
        logs.WriteChannelLine(id, "Raid", @"Pfad C:\neu äöü");

        var lines = File.ReadAllText(Path.Combine(dir, "logs", "channels", id.ToString(), Start(time) + ".log"))
            .Split(Environment.NewLine);
        Assert.Equal(3, lines.Length); // two entries and the empty rest after the last newline
        Assert.EndsWith(@"[Raid] hi\r\n2026-01-01 00:00:00.000 [Raid] chef gebannt\u0085a\u2028b\u2029c\td\u0000e\u001B[31m", lines[0]);
        Assert.EndsWith(@"[Raid] Pfad C:\neu äöü", lines[1]); // text without control characters is unchanged
        Assert.Equal("", lines[2]);
    }

    /// <summary>Package 90: the console (docker logs) gets the same escaped line as the file, ANSI escape sequences included.</summary>
    [Fact]
    public void WriteServerLine_ControlCharacters_ConsoleEscapedLikeFile()
    {
        var time = new ManualTimeProvider();
        var console = new List<string>();
        var logs = new ServerLogWriter(dir, 30, time, console.Add);
        logs.WriteServerLine("a\rb\nc\u2028d\u2029e\u001b[31mrot\u009b2J\u0007");

        var line = Assert.Single(console);
        Assert.EndsWith(@" a\rb\nc\u2028d\u2029e\u001B[31mrot\u009B2J\u0007", line);
        Assert.DoesNotContain(line, character => char.IsControl(character) || character is '\u2028' or '\u2029');
        var file = File.ReadAllText(Path.Combine(dir, "logs", "server", Start(time) + ".log"));
        Assert.Equal(line + Environment.NewLine, file);
    }

    /// <summary>Package 90: a chat message cannot forge a timestamped line on the console or in the file.</summary>
    [Fact]
    public async Task ChannelLog_ChatWithFakeLine_StaysOneLine()
    {
        var id = ClientIdentity.Create(); // server-wide chat needs a right a guest lacks
        await using var server = await TestServer.StartAsync(TestServer.Grant(id, "Admin"));
        await using var anna = await TestClient.ConnectAsync(server, "anna", id);
        await anna.SendAsync(new SendChat(ChatTarget.Server, null, "hallo\n2026-09-30 12:00:00.000 Admin-Token: gefaelscht"));
        await EventuallyAsync(() => ServerLog(server), "gefaelscht");

        var console = Assert.Single(server.Log, line => line.Contains("gefaelscht"));
        Assert.DoesNotContain(console, char.IsControl);
        Assert.EndsWith(@"Chat from anna: hallo\n2026-09-30 12:00:00.000 Admin-Token: gefaelscht", console);
        var file = Assert.Single(ServerLog(server).Split('\n'), line => line.Contains("gefaelscht"));
        Assert.Equal(console, file.TrimEnd('\r'));
    }

    static void Touch(string folder, string name)
    {
        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, name + ".log"), "alt");
    }

    [Fact]
    public void CleanUp_OldFiles_DeletedAfterRetention()
    {
        var time = new ManualTimeProvider();
        var server = Path.Combine(dir, "logs", "server");
        var channel = Path.Combine(dir, "logs", "channels", Guid.NewGuid().ToString());
        Touch(server, Start(time, -31));
        Touch(server, Start(time, -5));
        Touch(channel, Start(time, -40));

        var logs = new ServerLogWriter(dir, 30, time, _ => { }); // cleanup at start
        Assert.False(File.Exists(Path.Combine(server, Start(time, -31) + ".log")));
        Assert.True(File.Exists(Path.Combine(server, Start(time, -5) + ".log")));
        Assert.False(File.Exists(Path.Combine(channel, Start(time, -40) + ".log")));

        time.Advance(TimeSpan.FromDays(26)); // the -5 file is now 31 days old: cleanup at day change
        logs.WriteServerLine("neuer Tag");
        Assert.False(File.Exists(Path.Combine(server, Start(time, -31) + ".log")));
    }

    [Fact]
    public void RotateDailyOff_FileLastsUntilRestart_OldFilesStillDeleted()
    {
        var time = new ManualTimeProvider();
        var server = Path.Combine(dir, "logs", "server");
        var start = Start(time);
        var logs = new ServerLogWriter(dir, 30, time, _ => { }, newFileEachDay: false);
        logs.WriteServerLine("eins");
        Touch(server, Start(time, -29));

        time.Advance(TimeSpan.FromDays(2)); // the other file is now 31 days old
        logs.WriteServerLine("zwei");
        time.Advance(TimeSpan.FromDays(40)); // the running file is older than the retention, but still in use
        logs.WriteServerLine("drei");

        Assert.Equal([start + ".log"], Directory.GetFiles(server).Select(Path.GetFileName));
        var text = File.ReadAllText(Path.Combine(server, start + ".log"));
        Assert.Contains("eins", text);
        Assert.Contains("drei", text);
    }

    /// <summary>Package 69: retention and daily files change while the server runs.</summary>
    [Fact]
    public void Update_RetentionChangedAtRuntime_NextCleanupUsesIt()
    {
        var time = new ManualTimeProvider();
        var server = Path.Combine(dir, "logs", "server");
        var start = Start(time);
        Touch(server, Start(time, -10));
        var logs = new ServerLogWriter(dir, 30, time, _ => { });
        logs.WriteServerLine("eins");
        Assert.True(File.Exists(Path.Combine(server, Start(time, -10) + ".log")));

        logs.Update(5, newFileEachDay: false);
        time.Advance(TimeSpan.FromDays(1));
        logs.WriteServerLine("zwei");

        Assert.Equal([start + ".log"], Directory.GetFiles(server).Select(Path.GetFileName)); // old file gone, no new day file
        Assert.Contains("zwei", File.ReadAllText(Path.Combine(server, start + ".log")));
    }

    [Fact]
    public void CleanUp_RetentionZero_KeepsAll()
    {
        var time = new ManualTimeProvider();
        var server = Path.Combine(dir, "logs", "server");
        Touch(server, Start(time, -400));
        new ServerLogWriter(dir, 0, time, _ => { }).WriteServerLine("x");
        Assert.True(File.Exists(Path.Combine(server, Start(time, -400) + ".log")));
    }

    [Fact]
    public void WriteFailure_DoesNotThrow_ReportsOnce()
    {
        File.WriteAllText(Path.Combine(dir, "logs"), "ich bin eine Datei, kein Ordner");
        var console = new List<string>();
        var logs = new ServerLogWriter(dir, 30, new ManualTimeProvider(), console.Add);
        logs.WriteServerLine("eins");
        logs.WriteChannelLine(Guid.NewGuid(), "Raid", "zwei");
        logs.WriteServerLine("drei");
        Assert.Single(console, line => line.Contains("Log konnte nicht geschrieben werden"));
        Assert.Contains(console, line => line.EndsWith("drei")); // the console keeps working
    }

    // ---- what the server logs ----

    [Fact]
    public async Task ServerLog_ConnectDisconnectRejected_Logged()
    {
        await using var server = await TestServer.StartAsync();
        var anna = await TestClient.ConnectAsync(server, "anna");
        await using (var dup = await TestClient.OpenAsync(server.Port))
            Assert.IsType<Rejected>(await dup.HandshakeAsync("ANNA"));
        await anna.DisposeAsync();

        await EventuallyAsync(() => ServerLog(server),
            "anna connected (", "refused: NicknameTaken", "127.0.0.1", "anna disconnected (ended by the client)");
    }

    [Fact]
    public async Task ServerLog_AdminActions_LoggedWithActor()
    {
        var adminId = ClientIdentity.Create();
        await using var server = await TestServer.StartAsync(TestServer.Grant(adminId, "Admin"));
        await using var admin = await TestClient.ConnectAsync(server, "chef", adminId);
        var g1 = await TestClient.ConnectAsync(server, "gast1");
        var g2 = await TestClient.ConnectAsync(server, "gast2");

        await admin.SendAsync(new CreateGroup("Team", Permission.Speak));
        var team = (await admin.WaitForAsync<GroupsChanged>()).Groups.Single(groupInfo => groupInfo.Name == "Team");
        await admin.SendAsync(new UpdateGroup(team.Id, "Team 2", Permission.Speak | Permission.SpeakLinked));
        await admin.SendAsync(new AssignGroup(g1.Identity.Fingerprint, team.Id));
        await admin.SendAsync(new UnassignGroup(g1.Identity.Fingerprint, team.Id));
        await admin.SendAsync(new DeleteGroup(team.Id));
        await admin.SendAsync(new UpdateServerSettings("Neuer Name", "Hallo", "pw"));
        await admin.SendAsync(new SetSessionServerMute(g1.Id, true));
        await admin.SendAsync(new KickSession(g1.Id, "Spam"));
        await admin.SendAsync(new BanSession(g2.Id, "Troll", 60, true));
        await admin.SendAsync(new ListBans { RequestId = "l" });
        var ban = Assert.Single((await admin.WaitForAsync<BanList>(banList => banList.RequestId == "l")).Bans);
        await admin.SendAsync(new Unban(ban.Id));

        await EventuallyAsync(() => ServerLog(server),
            "Group 'Team' created by chef",
            "Group 'Team' changed by chef: name 'Team' -> 'Team 2'",
            "Group 'Team 2' given to gast1 by chef",
            "Group 'Team 2' taken from gast1 by chef",
            "Group 'Team 2' deleted by chef",
            "Server settings changed by chef: name 'Testserver' -> 'Neuer Name', welcome text changed, password set",
            "gast1 server-muted by chef",
            "gast1 was kicked by chef: Spam",
            "gast2 was banned by chef for 60 minutes with IP: Troll",
            "Ban of gast2 lifted by chef");
        await g1.DisposeAsync();
        await g2.DisposeAsync();
    }

    [Fact]
    public async Task ChannelLog_JoinMoveLeave_Logged()
    {
        var adminId = ClientIdentity.Create();
        await using var server = await TestServer.StartAsync(TestServer.Grant(adminId, "Admin"));
        await using var admin = await TestClient.ConnectAsync(server, "chef", adminId);
        var lobby = admin.Welcome.Snapshot.DefaultChannelId;
        var gast = await TestClient.ConnectAsync(server, "gast");
        await admin.SendAsync(new CreateChannel("Raid", ""));
        var raid = (await admin.WaitForAsync<ChannelAdded>()).Channel.Id;

        await gast.SendAsync(new JoinChannel(raid));
        await admin.WaitForAsync<UserUpdated>(userUpdated => userUpdated.User.SessionId == gast.Id && userUpdated.User.ChannelId == raid);
        await admin.SendAsync(new MoveUser(gast.Id, lobby));
        await admin.WaitForAsync<UserUpdated>(userUpdated => userUpdated.User.SessionId == gast.Id && userUpdated.User.ChannelId == lobby);
        await gast.DisposeAsync();

        await EventuallyAsync(() => ChannelLog(server, lobby),
            "[Lobby] gast joined the channel (connected)",
            "[Lobby] gast left the channel (moving to Raid)",
            "[Lobby] gast was moved here by chef from Raid",
            "[Lobby] gast left the channel (ended by the client)");
        await EventuallyAsync(() => ChannelLog(server, raid),
            "[Raid] Channel created by chef",
            "[Raid] gast joined the channel (coming from Lobby)",
            "[Raid] gast was moved by chef to Lobby");
        await EventuallyAsync(() => ServerLog(server), "Channel 'Raid' created by chef");
        Assert.DoesNotContain("joined", ServerLog(server)); // channel matters stay in channel logs
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

        await EventuallyAsync(() => ChannelLog(server, lobby),
            "[Lobby] Link to Raid set by chef", "[Lobby] Link to Raid 2 removed (channel deleted)");
        await EventuallyAsync(() => ChannelLog(server, raid),
            "[Raid] Link to Lobby set by chef",
            "[Raid 2] Channel changed by chef: name 'Raid' -> 'Raid 2', description changed", // Package 83: the order stays
            "[Raid 2] Channel deleted by chef");
        await EventuallyAsync(() => ServerLog(server), "Channel 'Raid 2' deleted by chef");
    }

    [Fact]
    public async Task Logs_TokenAndPassword_NeverInFiles()
    {
        await using var server = await TestServer.StartAsync();
        var token = server.State.PendingAdminToken!;
        await using var client = await TestClient.ConnectAsync(server, "anna");
        await client.SendAsync(new RedeemAdminToken(token));
        await client.WaitForAsync<UserUpdated>(userUpdated => userUpdated.User.SessionId == client.Id && userUpdated.User.Permissions == Permission.All);
        await client.SendAsync(new UpdateServerSettings("S", "", "geheim-123"));
        await client.WaitForAsync<ServerSettingsChanged>();

        await EventuallyAsync(() => ServerLog(server), "Admin token created", "anna redeemed the admin token", "password set");
        var all = string.Join("\n", Directory.GetFiles(Path.Combine(server.DataDir, "logs"), "*.log", SearchOption.AllDirectories).Select(ReadShared));
        Assert.DoesNotContain(token, all);
        Assert.DoesNotContain("geheim-123", all);
        Assert.Contains(server.Log, line => line.Contains(token)); // the console still shows it
    }

    [Fact]
    public async Task ChannelLog_RenamedChannel_SameFolderAfterRestart()
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
        await again.WaitForAsync<UserUpdated>(userUpdated => userUpdated.User.ChannelId == raid);

        await EventuallyAsync(() => ChannelLog(server, raid), "[Raid] Channel created by chef", "[Raid 2] chef joined the channel");
    }
}
