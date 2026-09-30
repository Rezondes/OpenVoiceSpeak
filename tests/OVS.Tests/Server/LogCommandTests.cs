using System.IO.Compression;
using System.Text.Json;
using OVS.Server;
using OVS.Server.Data;
using OVS.Server.Permissions;
using OVS.Shared.Identity;
using OVS.Shared.Permissions;
using OVS.Shared.Protocol;
using OVS.Tests.TestSupport;

namespace OVS.Tests.Server;

/// <summary>Package 81: the server and channel logs listed, read page by page and searched from the administration.</summary>
public sealed class LogCommandTests
{
    static async Task<(TestServer Server, TestClient Admin)> StartWithAdminAsync(TimeProvider? time = null, string? dataDir = null)
    {
        var admin = ClientIdentity.Create();
        var server = await TestServer.StartAsync(TestServer.Grant(admin, "Admin"), time: time, dataDir: dataDir);
        return (server, await TestClient.ConnectAsync(server, "chef", admin));
    }

    static async Task<IReadOnlyList<LogFileInfo>> ListAsync(TestClient client)
    {
        var id = Guid.NewGuid().ToString("N");
        await client.SendAsync(new ListLogs { RequestId = id });
        return (await client.WaitForAsync<LogList>(l => l.RequestId == id)).Files;
    }

    static async Task<LogPage> ReadAsync(TestClient client, string fileId, int? page = null)
    {
        var id = Guid.NewGuid().ToString("N");
        await client.SendAsync(new ReadLog(fileId, page) { RequestId = id });
        return await client.WaitForAsync<LogPage>(p => p.RequestId == id, 10_000);
    }

    static async Task<LogSearchResult> SearchAsync(TestClient client, SearchLogs search, int timeoutMs = 10_000)
    {
        var id = Guid.NewGuid().ToString("N");
        await client.SendAsync(search with { RequestId = id });
        return await client.WaitForAsync<LogSearchResult>(r => r.RequestId == id, timeoutMs);
    }

    static string ServerLogDir(TestServer server) => Directory.CreateDirectory(Path.Combine(server.DataDir, "logs", "server")).FullName;

    static void WriteLines(string path, IEnumerable<string> lines) => File.WriteAllLines(path, lines);

    [Fact]
    public async Task ListLogs_ServerAndChannelFiles_NewestFirst_WithNames()
    {
        var time = new ManualTimeProvider(new DateTimeOffset(2026, 3, 1, 10, 0, 0, TimeSpan.Zero));
        var (server, admin) = await StartWithAdminAsync(time);
        await admin.SendAsync(new CreateChannel("Raid", ""));
        var raid = (await admin.WaitForAsync<ChannelAdded>()).Channel.Id;
        await admin.SendAsync(new CreateChannel("Alt", ""));
        var old = (await admin.WaitForAsync<ChannelAdded>()).Channel.Id;
        await admin.SendAsync(new JoinChannel(raid));
        await admin.WaitForAsync<UserUpdated>(u => u.User.ChannelId == raid);
        await admin.SendAsync(new DeleteChannel(old));
        await admin.WaitForAsync<ChannelRemoved>();
        await admin.DisposeAsync();

        time.Advance(TimeSpan.FromMinutes(5));
        await using var restarted = await server.RestartAsync(time);
        var adminIdentity = admin.Identity;
        await using var again = await TestClient.ConnectAsync(restarted, "chef", adminIdentity);
        await again.SendAsync(new EditChannel(raid, "Raid neu", "", 1));
        await again.WaitForAsync<ChannelUpdated>();

        var files = await ListAsync(again);
        var servers = files.Where(f => f.Kind == LogKind.Server).ToList();
        Assert.Equal(2, servers.Count);
        Assert.All(files, f => Assert.True(f.Size > 0, f.Id));
        Assert.All(servers, f => Assert.Null(f.ChannelId));
        Assert.Equal(files.OrderByDescending(f => f.Start).Select(f => f.Start), files.Select(f => f.Start));
        Assert.True(servers[0].Start > servers[1].Start);
        Assert.Equal(time.GetUtcNow(), servers[0].Start);
        // current name for a live channel, the name in the file for a deleted one
        Assert.Contains(files, f => f.ChannelId == raid && f.ChannelName == "Raid neu");
        Assert.Contains(files, f => f.ChannelId == old && f.ChannelName == "Alt");
        Assert.Contains(files, f => f.ChannelName == "Lobby");
        Assert.All(files, f => Assert.Matches(@"^(server|channels/[0-9a-f-]{36})/\d{4}-\d{2}-\d{2}_\d{2}-\d{2}-\d{2}\.log$", f.Id));
    }

    [Fact]
    public async Task ReadLog_PagesOf1000_LastPageFirst()
    {
        var (server, admin) = await StartWithAdminAsync();
        await using var _ = server;
        await using var a = admin;
        WriteLines(Path.Combine(ServerLogDir(server), "2025-01-01_00-00-00.log"), Enumerable.Range(1, 2500).Select(i => $"Zeile {i}"));
        var file = (await ListAsync(admin)).Single(f => f.Id == "server/2025-01-01_00-00-00.log");

        var last = await ReadAsync(admin, file.Id);
        Assert.Equal((3, 3, 2001, 500), (last.Page, last.PageCount, last.FirstLine, last.Lines.Count));
        Assert.Equal(("Zeile 2001", "Zeile 2500"), (last.Lines[0], last.Lines[^1]));
        var middle = await ReadAsync(admin, file.Id, 2);
        Assert.Equal((2, 3, 1001, 1000), (middle.Page, middle.PageCount, middle.FirstLine, middle.Lines.Count));
        Assert.Equal(("Zeile 1001", "Zeile 2000"), (middle.Lines[0], middle.Lines[^1]));
        var first = await ReadAsync(admin, file.Id, 1);
        Assert.Equal((1, 1, 1000), (first.Page, first.FirstLine, first.Lines.Count));
        Assert.Equal("Zeile 1", first.Lines[0]);
        // beyond the end: the last page
        Assert.Equal(3, (await ReadAsync(admin, file.Id, 9)).Page);

        // exactly 1000 lines are one full page
        WriteLines(Path.Combine(ServerLogDir(server), "2025-01-02_00-00-00.log"), Enumerable.Range(1, 1000).Select(i => $"Zeile {i}"));
        var full = await ReadAsync(admin, "server/2025-01-02_00-00-00.log");
        Assert.Equal((1, 1, 1, 1000), (full.Page, full.PageCount, full.FirstLine, full.Lines.Count));
    }

    [Fact]
    public async Task SearchLogs_AllFiles_CaseInsensitive_Max500_Truncated()
    {
        var (server, admin) = await StartWithAdminAsync();
        await using var _ = server;
        await using var a = admin;
        var dir = ServerLogDir(server);
        var channel = Guid.NewGuid();
        var channelDir = Directory.CreateDirectory(Path.Combine(server.DataDir, "logs", "channels", channel.ToString())).FullName;
        WriteLines(Path.Combine(dir, "2025-01-01_00-00-00.log"), ["alt: gesucht", "axb"]);
        File.SetLastWriteTime(Path.Combine(dir, "2025-01-01_00-00-00.log"), new DateTime(2025, 1, 1, 1, 0, 0));
        WriteLines(Path.Combine(dir, "2025-02-01_00-00-00.log"), Enumerable.Range(1, 1200).Select(i => i % 2 == 0 ? $"{i} GeSuChT" : $"{i} nichts"));
        File.SetLastWriteTime(Path.Combine(dir, "2025-02-01_00-00-00.log"), new DateTime(2025, 2, 1, 1, 0, 0));
        WriteLines(Path.Combine(channelDir, "2025-03-01_00-00-00.log"), ["2025-03-01 00:00:00.000 [Raid] eins gesucht", "zwei", "2025-03-01 00:00:01.000 [Raid] drei GESUCHT"]);
        File.SetLastWriteTime(Path.Combine(channelDir, "2025-03-01_00-00-00.log"), new DateTime(2025, 3, 1, 1, 0, 0));

        var all = await SearchAsync(admin, new SearchLogs("gesucht"));
        Assert.Equal(500, all.Hits.Count);
        Assert.True(all.Truncated);
        Assert.False(all.TimedOut);
        // newest first: the channel file (newest start), its last line first, then the February file from its end
        Assert.Equal([("channels/" + channel + "/2025-03-01_00-00-00.log", 3), ("channels/" + channel + "/2025-03-01_00-00-00.log", 1)],
            all.Hits.Take(2).Select(h => (h.FileId, h.Line)));
        Assert.Equal(("server/2025-02-01_00-00-00.log", 1200, "1200 GeSuChT"), (all.Hits[2].FileId, all.Hits[2].Line, all.Hits[2].Text));
        Assert.All(all.Hits, h => Assert.Contains("gesucht", h.Text, StringComparison.OrdinalIgnoreCase));

        // filters: type, channel and period; plain text, no regex
        var channelOnly = await SearchAsync(admin, new SearchLogs("GESUCHT", LogKind.Channel, channel));
        Assert.Equal([3, 1], channelOnly.Hits.Select(h => h.Line));
        Assert.False(channelOnly.Truncated);
        var serverOnly = await SearchAsync(admin, new SearchLogs("gesucht", LogKind.Server));
        Assert.DoesNotContain(serverOnly.Hits, h => h.FileId.StartsWith("channels/"));
        var january = await SearchAsync(admin, new SearchLogs("gesucht", null, null,
            new DateTimeOffset(2024, 12, 31, 0, 0, 0, TimeSpan.Zero), new DateTimeOffset(2025, 1, 15, 0, 0, 0, TimeSpan.Zero)));
        Assert.Equal(("server/2025-01-01_00-00-00.log", 1), (Assert.Single(january.Hits).FileId, january.Hits[0].Line));
        Assert.Empty((await SearchAsync(admin, new SearchLogs("a.b"))).Hits);

        // an empty or too long query is refused
        await admin.SendAsync(new SearchLogs("  ") { RequestId = "leer" });
        Assert.Equal(Codes.InvalidValue, (await admin.ErrorAsync("leer")).Code);
        await admin.SendAsync(new SearchLogs(new string('x', ProtocolInfo.MaxLogQueryLength + 1)) { RequestId = "lang" });
        Assert.Equal(Codes.InvalidValue, (await admin.ErrorAsync("lang")).Code);
    }

    [Fact]
    public async Task Logs_RequireLogsView_UnknownIdRejected_NoPathTraversal()
    {
        var (server, admin) = await StartWithAdminAsync();
        await using var _ = server;
        await using var a = admin;
        await using var guest = await TestClient.ConnectAsync(server, "gast");
        var dir = ServerLogDir(server);
        File.WriteAllText(Path.Combine(dir, "notiz.txt"), "keine Logdatei");
        File.WriteAllText(Path.Combine(server.DataDir, "logs", "wurzel.log"), "nicht unter server oder channels");

        foreach (var request in new Request[] { new ListLogs(), new ReadLog("server/x.log"), new SearchLogs("x") })
        {
            await guest.SendAsync(request with { RequestId = "g" });
            Assert.Equal(Codes.PermissionDenied, (await guest.ErrorAsync("g")).Code);
        }

        var files = await ListAsync(admin);
        Assert.DoesNotContain(files, f => f.Id.EndsWith(".txt") || f.Id.Contains("wurzel"));
        var escapes = new[]
        {
            "server/notiz.txt", "wurzel.log", "../server-data.json", "server/../../server-data.json", "server/../server-data.json",
            Path.Combine(server.DataDir, "server-data.json"), "server\\..\\..\\server-data.json", "", "server/unbekannt.log",
        };
        foreach (var id in escapes)
        {
            await admin.SendAsync(new ReadLog(id) { RequestId = "r" });
            Assert.Equal(Codes.NotFound, (await admin.ErrorAsync("r")).Code);
        }
        await admin.SendAsync(new ReadLog(files[0].Id, 0) { RequestId = "p" });
        Assert.Equal(Codes.InvalidValue, (await admin.ErrorAsync("p")).Code);
    }

    [Fact]
    public async Task LongLine_CutAt2000()
    {
        var (server, admin) = await StartWithAdminAsync();
        await using var _ = server;
        await using var a = admin;
        var dir = ServerLogDir(server);
        var longLine = "Anfang " + new string('x', 5000) + " gesucht";
        WriteLines(Path.Combine(dir, "2025-01-01_00-00-00.log"), ["kurz", longLine]);

        var page = await ReadAsync(admin, "server/2025-01-01_00-00-00.log");
        Assert.Equal("kurz", page.Lines[0]);
        Assert.StartsWith(longLine[..ProtocolInfo.MaxLogLineLength], page.Lines[1]);
        Assert.EndsWith(" [...]", page.Lines[1]);
        Assert.True(page.Lines[1].Length < ProtocolInfo.MaxLogLineLength + 10);
        var hit = Assert.Single((await SearchAsync(admin, new SearchLogs("gesucht"))).Hits);
        Assert.StartsWith("Anfang ", hit.Text);
        Assert.True(hit.Text.Length < ProtocolInfo.MaxLogLineLength + 10);

        // a full page of long lines with umlauts (6 bytes each in JSON) still fits one message; lines are cut further
        WriteLines(Path.Combine(dir, "2025-01-02_00-00-00.log"), Enumerable.Range(1, 1000).Select(i => $"{i} " + new string('ä', 3000)));
        var wide = await ReadAsync(admin, "server/2025-01-02_00-00-00.log");
        Assert.Equal(1000, wide.Lines.Count);
        Assert.All(wide.Lines, l => Assert.EndsWith(" [...]", l));
        Assert.True(JsonSerializer.SerializeToUtf8Bytes<Message>(wide, ProtocolJson.Options).Length < FrameReader.MaxFrameSize);
        var many = await SearchAsync(admin, new SearchLogs("ä"));
        Assert.Equal(500, many.Hits.Count);
        Assert.True(JsonSerializer.SerializeToUtf8Bytes<Message>(many, ProtocolJson.Options).Length < FrameReader.MaxFrameSize);
    }

    [Fact]
    public async Task Search_DoesNotHoldStateLock()
    {
        var (server, admin) = await StartWithAdminAsync();
        await using var _ = server;
        await using var a = admin;
        await using var other = await TestClient.ConnectAsync(server, "anna");
        WriteLines(Path.Combine(ServerLogDir(server), "2025-01-01_00-00-00.log"), Enumerable.Range(1, 200_000).Select(i => $"Zeile {i} gesucht"));
        using var entered = new SemaphoreSlim(0);
        using var release = new ManualResetEventSlim();
        server.State.LogReader.ReadHook = _ =>
        {
            entered.Release();
            release.Wait(TimeSpan.FromSeconds(10));
        };

        await admin.SendAsync(new SearchLogs("gesucht") { RequestId = "s" });
        Assert.True(await entered.WaitAsync(TimeSpan.FromSeconds(5)), "search did not start");
        // the search is paused inside a file: chat still flows through the state
        await other.SendAsync(new SendChat(ChatTarget.Channel, null, "noch da?"));
        await admin.WaitForAsync<ChatMessage>(m => m.Text == "noch da?");
        release.Set();
        var result = await admin.WaitForAsync<LogSearchResult>(r => r.RequestId == "s", 10_000);
        Assert.Equal(500, result.Hits.Count);
    }

    // ---- Package 82: download ----

    static string ExportDir(TestServer server) => Path.Combine(server.DataDir, "logs-export");

    static async Task<LogDownloadReady> PrepareAsync(TestClient client, params string[] ids)
    {
        var id = Guid.NewGuid().ToString("N");
        await client.SendAsync(new PrepareLogDownload(ids) { RequestId = id });
        return await client.WaitForAsync<LogDownloadReady>(r => r.RequestId == id, 10_000);
    }

    /// <summary>Pulls every chunk, one request each.</summary>
    static async Task<byte[]> PullAsync(TestClient client, LogDownloadReady ready)
    {
        using var data = new MemoryStream();
        while (true)
        {
            var id = Guid.NewGuid().ToString("N");
            await client.SendAsync(new DownloadLogChunk(ready.DownloadId, data.Length) { RequestId = id });
            var chunk = await client.WaitForAsync<LogChunk>(c => c.RequestId == id, 10_000);
            Assert.Equal((data.Length, ready.Size), (chunk.Offset, chunk.TotalSize));
            data.Write(Convert.FromBase64String(chunk.DataBase64));
            if (chunk.IsLast) return data.ToArray();
        }
    }

    static byte[] ReadShared(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var copy = new MemoryStream();
        stream.CopyTo(copy);
        return copy.ToArray();
    }

    [Fact]
    public async Task DownloadSingleFile_ByteIdenticalSnapshot()
    {
        var (server, admin) = await StartWithAdminAsync();
        await using var _ = server;
        await using var a = admin;
        var live = (await ListAsync(admin)).First(f => f.Kind == LogKind.Server); // the file the server is writing
        var path = Path.Combine(server.DataDir, "logs", live.Id);
        var before = ReadShared(path);

        var ready = await PrepareAsync(admin, live.Id);
        Assert.Equal(Path.GetFileName(path), ready.FileName);
        await using (await TestClient.ConnectAsync(server, "spaeter")) { } // lines written after the request
        await Task.Delay(100);
        var bytes = await PullAsync(admin, ready);
        Assert.Equal(before, bytes);
        Assert.True(ReadShared(path).Length > before.Length);
        Assert.Empty(Directory.GetFiles(ExportDir(server))); // deleted once the last chunk went out
        Assert.Contains(server.Log, l => l.Contains($"Logs heruntergeladen von chef: {Path.GetFileName(path)}"));
    }

    [Fact]
    public async Task DownloadSelection_ZipWithFolders_TempRemoved()
    {
        var time = new ManualTimeProvider(); // Package 86: one download per 10 s and session
        var (server, admin) = await StartWithAdminAsync(time);
        await admin.SendAsync(new CreateChannel("Raid/1 ..", ""));
        var raid = (await admin.WaitForAsync<ChannelAdded>()).Channel.Id;
        var gone = Guid.NewGuid();
        var dir = ServerLogDir(server);
        var files = new Dictionary<string, string>
        {
            ["server/2025-01-01_00-00-00.log"] = "server alt\n",
            [$"channels/{raid}/2025-02-01_00-00-00.log"] = "2025-02-01 00:00:00.000 [Raid/1 ..] eins\n",
            [$"channels/{gone}/2025-03-01_00-00-00.log"] = "2025-03-01 00:00:00.000 [Weg] zwei\n",
        };
        foreach (var (id, text) in files)
        {
            var file = Path.Combine(server.DataDir, "logs", id);
            Directory.CreateDirectory(Path.GetDirectoryName(file)!);
            File.WriteAllText(file, text);
        }
        File.SetLastWriteTime(Path.Combine(dir, "2025-01-01_00-00-00.log"), new DateTime(2025, 1, 1, 12, 0, 0));
        File.SetLastWriteTime(Path.Combine(server.DataDir, "logs", "channels", raid.ToString(), "2025-02-01_00-00-00.log"), new DateTime(2025, 2, 1, 12, 0, 0));
        File.SetLastWriteTime(Path.Combine(server.DataDir, "logs", "channels", gone.ToString(), "2025-03-01_00-00-00.log"), new DateTime(2025, 3, 2, 12, 0, 0));

        var ready = await PrepareAsync(admin, [.. files.Keys]);
        Assert.Equal("ovs-logs_2025-01-01_2025-03-02.zip", ready.FileName);
        Assert.Single(Directory.GetFiles(ExportDir(server)));
        var zipBytes = await PullAsync(admin, ready);
        using (var zip = new ZipArchive(new MemoryStream(zipBytes)))
        {
            var entries = zip.Entries.ToDictionary(e => e.FullName, e => new StreamReader(e.Open()).ReadToEnd());
            Assert.Equal(new Dictionary<string, string>
            {
                ["server/2025-01-01_00-00-00.log"] = "server alt\n",
                [$"channels/Raid_1_{raid}/2025-02-01_00-00-00.log"] = "2025-02-01 00:00:00.000 [Raid/1 ..] eins\n",
                [$"channels/Weg_{gone}/2025-03-01_00-00-00.log"] = "2025-03-01 00:00:00.000 [Weg] zwei\n",
            }, entries);
        }
        Assert.Empty(Directory.GetFiles(ExportDir(server)));

        // the session ends: its prepared download goes with it
        time.Advance(Limits.HeavyInterval);
        await PrepareAsync(admin, [.. files.Keys]);
        Assert.Single(Directory.GetFiles(ExportDir(server)));
        await admin.DisposeAsync();
        for (int i = 0; i < 50 && Directory.GetFiles(ExportDir(server)).Length > 0; i++) await Task.Delay(20);
        Assert.Empty(Directory.GetFiles(ExportDir(server)));

        // what a crash left behind is removed at the next start
        File.WriteAllText(Path.Combine(ExportDir(server), "0123456789abcdef0123456789abcdef.zip"), "rest");
        await using var restarted = await server.RestartAsync();
        Assert.Empty(Directory.GetFiles(ExportDir(restarted)));
    }

    [Fact]
    public async Task Download_RequiresLogsDownload_TooLargeRejected()
    {
        var reader = ClientIdentity.Create();
        var admin = ClientIdentity.Create();
        var time = new ManualTimeProvider(); // Package 86: one download per 10 s and session
        var server = await TestServer.StartAsync(data =>
        {
            TestServer.Grant(admin, "Admin")(data);
            var group = new Group(Guid.NewGuid(), "Leser", Permission.LogsView | Permission.Speak);
            data.Groups.Add(group);
            data.Users.Add(new UserRecord { Fingerprint = reader.Fingerprint, LastNickname = "leser", GroupIds = [group.Id] });
        }, time: time);
        await using var _ = server;
        await using var chef = await TestClient.ConnectAsync(server, "chef", admin);
        await using var leser = await TestClient.ConnectAsync(server, "leser", reader);
        var id = (await ListAsync(leser))[0].Id; // viewing works
        foreach (var request in new Request[] { new PrepareLogDownload([id]), new DownloadLogChunk("0123456789abcdef0123456789abcdef", 0) })
        {
            await leser.SendAsync(request with { RequestId = "d" });
            Assert.Equal(Codes.PermissionDenied, (await leser.ErrorAsync("d")).Code);
        }

        // more than 200 MB together is refused, nothing is copied
        var dir = ServerLogDir(server);
        foreach (var name in new[] { "2025-01-01_00-00-00.log", "2025-01-02_00-00-00.log" })
            using (var big = File.Create(Path.Combine(dir, name))) big.SetLength(101L * 1024 * 1024);
        await chef.SendAsync(new PrepareLogDownload(["server/2025-01-01_00-00-00.log", "server/2025-01-02_00-00-00.log"]) { RequestId = "gross" });
        Assert.Equal(Codes.LogsTooLarge, (await chef.ErrorAsync("gross")).Code);
        Assert.False(Directory.Exists(ExportDir(server)) && Directory.EnumerateFileSystemEntries(ExportDir(server)).Any());

        // unknown or no files, unknown download
        time.Advance(Limits.HeavyInterval);
        await chef.SendAsync(new PrepareLogDownload([id, "../server-data.json"]) { RequestId = "fremd" });
        Assert.Equal(Codes.NotFound, (await chef.ErrorAsync("fremd")).Code);
        await chef.SendAsync(new PrepareLogDownload([]) { RequestId = "leer" });
        Assert.Equal(Codes.InvalidValue, (await chef.ErrorAsync("leer")).Code);
        await chef.SendAsync(new DownloadLogChunk("0123456789abcdef0123456789abcdef", 0) { RequestId = "unbekannt" });
        Assert.Equal(Codes.NotFound, (await chef.ErrorAsync("unbekannt")).Code);
    }

    [Fact]
    public async Task Download_ChunkedPullBased_ChatStillFlows()
    {
        var (server, admin) = await StartWithAdminAsync();
        await using var _ = server;
        await using var a = admin;
        await using var other = await TestClient.ConnectAsync(server, "anna");
        var content = new byte[3 * ProtocolInfo.BackupChunkBytes / 2 + 17];
        Random.Shared.NextBytes(content);
        File.WriteAllBytes(Path.Combine(ServerLogDir(server), "2025-01-01_00-00-00.log"), content);
        using var entered = new SemaphoreSlim(0);
        using var release = new ManualResetEventSlim();
        server.State.LogReader.ReadHook = _ =>
        {
            entered.Release();
            release.Wait(TimeSpan.FromSeconds(10));
        };

        await admin.SendAsync(new PrepareLogDownload(["server/2025-01-01_00-00-00.log"]) { RequestId = "p" });
        Assert.True(await entered.WaitAsync(TimeSpan.FromSeconds(5)), "copy did not start");
        await other.SendAsync(new SendChat(ChatTarget.Channel, null, "noch da?")); // the copy is paused: chat still flows
        await admin.WaitForAsync<ChatMessage>(m => m.Text == "noch da?");
        release.Set();
        var ready = await admin.WaitForAsync<LogDownloadReady>(r => r.RequestId == "p", 10_000);
        Assert.Equal(content.Length, ready.Size);

        // one chunk per request, nothing more without asking
        await admin.SendAsync(new DownloadLogChunk(ready.DownloadId, 0) { RequestId = "c1" });
        var first = await admin.WaitForAsync<LogChunk>(c => c.RequestId == "c1");
        Assert.Equal(ProtocolInfo.BackupChunkBytes, Convert.FromBase64String(first.DataBase64).Length);
        Assert.False(first.IsLast);
        await admin.AssertNoMessageAsync<LogChunk>();
        Assert.Equal(content, await PullAsync(admin, ready));
    }
}
