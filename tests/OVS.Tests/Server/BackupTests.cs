using System.Collections.Concurrent;
using System.IO.Compression;
using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using OVS.Server;
using OVS.Server.Data;
using OVS.Shared.Identity;
using OVS.Shared.Permissions;
using OVS.Shared.Protocol;
using OVS.Tests.TestSupport;

namespace OVS.Tests.Server;

/// <summary>Package 74: backups on the server, created, listed, deleted and restored from the administration.</summary>
public sealed class BackupTests : IDisposable
{
    readonly string dir = Directory.CreateTempSubdirectory("ovs-backup-").FullName;

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

    static Dictionary<string, byte[]> Entries(string path)
    {
        using var zip = ZipFile.OpenRead(path);
        return zip.Entries.ToDictionary(e => e.FullName, e =>
        {
            using var stream = e.Open();
            using var copy = new MemoryStream();
            stream.CopyTo(copy);
            return copy.ToArray();
        });
    }

    static ServerData DataOf(byte[] json) => JsonSerializer.Deserialize<ServerData>(json, ProtocolJson.Options)!;

    static async Task<(TestServer Server, TestClient Admin)> StartWithAdminAsync(TimeProvider? time = null, Action<ServerData>? seed = null)
    {
        var admin = ClientIdentity.Create();
        var server = await TestServer.StartAsync(d => { TestServer.Grant(admin, "Admin")(d); seed?.Invoke(d); }, time: time);
        return (server, await TestClient.ConnectAsync(server, "chef", admin));
    }

    [Fact]
    public async Task Create_ContainsDataCertIcon_NoLogs()
    {
        var (server, admin) = await StartWithAdminAsync();
        await using var _ = server;
        await using var a = admin;
        var png = TestImages.Encode(64, 64);
        await admin.SendAsync(new SetServerIcon(Convert.ToBase64String(png)));
        await admin.WaitForAsync<ServerSettingsChanged>(m => m.Settings.IconHash is not null);
        await admin.SendAsync(new CreateChannel("Alt", ""));
        await admin.WaitForAsync<ChannelAdded>();
        Directory.CreateDirectory(Path.Combine(server.DataDir, "logs", "server"));
        File.WriteAllText(Path.Combine(server.DataDir, "logs", "server", "x.log"), "geheim");

        await admin.SendAsync(new CreateBackup { RequestId = "b" });
        var list = await admin.WaitForAsync<BackupList>(l => l.RequestId == "b");
        var info = Assert.Single(list.Backups);
        Assert.Matches(@"^\d{4}-\d{2}-\d{2}_\d{2}-\d{2}-\d{2}\.ovsbackup$", info.FileName);
        var path = Path.Combine(server.DataDir, "backups", info.FileName);
        Assert.Equal(new FileInfo(path).Length, info.Size);
        Assert.Equal(OVS.Shared.BuildInfo.Current.Version, info.ServerVersion);

        var entries = Entries(path);
        Assert.Equal(["cert.pfx", "manifest.json", "server-data.json", "server-icon.png"], entries.Keys.Order());
        Assert.Equal(File.ReadAllBytes(Path.Combine(server.DataDir, "cert.pfx")), entries["cert.pfx"]);
        Assert.Equal(png, entries["server-icon.png"]);
        Assert.Contains(DataOf(entries["server-data.json"]).Channels, c => c.Name == "Alt");
        using var manifest = JsonDocument.Parse(entries["manifest.json"]);
        Assert.Equal(ServerData.CurrentVersion, manifest.RootElement.GetProperty("dataVersion").GetInt32());
        Assert.Equal(1, manifest.RootElement.GetProperty("formatVersion").GetInt32());
        Assert.Contains(server.Log, l => l.Contains($"Backup {info.FileName} angelegt von chef"));
    }

    [Fact]
    public async Task List_NewestFirst_Delete()
    {
        var time = new ManualTimeProvider();
        var other = ClientIdentity.Create(); // Package 86: one backup per 10 s and session, so a second admin
        var (server, admin) = await StartWithAdminAsync(time, TestServer.Grant(other, "Admin"));
        await using var _ = server;
        await using var a = admin;
        await admin.SendAsync(new CreateBackup());
        await admin.WaitForAsync<BackupList>();
        time.Advance(TimeSpan.FromMinutes(1));
        await admin.SendAsync(new CreateBackup());
        await admin.WaitForAsync<BackupList>();
        await using var second = await TestClient.ConnectAsync(server, "zweiter", other);
        await second.SendAsync(new CreateBackup()); // same second: a name of its own
        await second.WaitForAsync<BackupList>();

        await admin.SendAsync(new ListBackups { RequestId = "l" });
        var list = (await admin.WaitForAsync<BackupList>(l => l.RequestId == "l")).Backups;
        Assert.Equal(3, list.Count);
        Assert.Equal(3, list.Select(b => b.FileName).Distinct().Count());
        Assert.True(list[0].CreatedAt >= list[1].CreatedAt && list[1].CreatedAt > list[2].CreatedAt);

        await admin.SendAsync(new DeleteBackup(list[2].FileName) { RequestId = "d" });
        var after = (await admin.WaitForAsync<BackupList>(l => l.RequestId == "d")).Backups;
        Assert.Equal(list.Take(2).Select(b => b.FileName), after.Select(b => b.FileName));
        Assert.False(File.Exists(Path.Combine(server.DataDir, "backups", list[2].FileName)));
        Assert.Contains(server.Log, l => l.Contains($"Backup {list[2].FileName} gelöscht von chef"));
    }

    static void WriteArchive(string path, Dictionary<string, byte[]> entries)
    {
        using var zip = ZipFile.Open(path, ZipArchiveMode.Create);
        foreach (var (name, bytes) in entries)
        {
            using var stream = zip.CreateEntry(name).Open();
            stream.Write(bytes);
        }
    }

    static byte[] Manifest(int dataVersion) =>
        JsonSerializer.SerializeToUtf8Bytes(new { formatVersion = 1, dataVersion, serverVersion = "test", createdAt = DateTimeOffset.UtcNow });

    [Fact]
    public async Task Restore_Invalid_NothingChanged()
    {
        var (server, admin) = await StartWithAdminAsync();
        await using var _ = server;
        await using var a = admin;
        await using var other = await TestClient.ConnectAsync(server, "anna");
        await admin.SendAsync(new CreateBackup());
        var good = Entries(Path.Combine(server.DataDir, "backups", Assert.Single((await admin.WaitForAsync<BackupList>()).Backups).FileName));
        var backups = Path.Combine(server.DataDir, "backups");

        File.WriteAllBytes(Path.Combine(backups, "kaputt.ovsbackup"), [1, 2, 3, 4]);
        WriteArchive(Path.Combine(backups, "ohne-manifest.ovsbackup"), good.Where(e => e.Key != "manifest.json").ToDictionary());
        var future = DataOf(good["server-data.json"]);
        future.DataVersion = ServerData.CurrentVersion + 1;
        WriteArchive(Path.Combine(backups, "zu-neu.ovsbackup"), new()
        {
            ["manifest.json"] = Manifest(ServerData.CurrentVersion + 1),
            ["server-data.json"] = JsonSerializer.SerializeToUtf8Bytes(future, ProtocolJson.Options),
            ["cert.pfx"] = good["cert.pfx"],
        });
        WriteArchive(Path.Combine(backups, "ohne-zertifikat.ovsbackup"), good.Where(e => e.Key != "cert.pfx").ToDictionary());
        WriteArchive(Path.Combine(backups, "daten-kaputt.ovsbackup"), new(good) { ["server-data.json"] = "{nicht json"u8.ToArray() });

        var dataBefore = File.ReadAllBytes(Path.Combine(server.DataDir, DataStore.FileName));
        foreach (var name in new[] { "kaputt", "ohne-manifest", "zu-neu", "ohne-zertifikat", "daten-kaputt" })
        {
            await admin.SendAsync(new RestoreBackup(name + ".ovsbackup") { RequestId = name });
            Assert.Equal(Codes.InvalidBackup, (await admin.ErrorAsync(name)).Code);
        }
        await other.AssertNoMessageAsync<Disconnected>();
        Assert.Equal(dataBefore, File.ReadAllBytes(Path.Combine(server.DataDir, DataStore.FileName)));
        Assert.DoesNotContain(Directory.GetFiles(backups), f => Path.GetFileName(f).StartsWith("vor-wiederherstellung_"));
        Assert.Contains(server.Log, l => l.Contains("Backup zu-neu.ovsbackup ist ungültig"));

        await admin.SendAsync(new ListBackups { RequestId = "l" }); // broken ones still show, so they can be deleted
        Assert.Equal(6, (await admin.WaitForAsync<BackupList>(l => l.RequestId == "l")).Backups.Count);
    }

    [Fact]
    public async Task PathTraversal_AndMissingRight_Rejected()
    {
        var (server, admin) = await StartWithAdminAsync();
        await using var _ = server;
        await using var a = admin;
        await using var guest = await TestClient.ConnectAsync(server, "gast");
        var backups = Directory.CreateDirectory(Path.Combine(server.DataDir, "backups")).FullName;
        var outside = Path.Combine(server.DataDir, "draussen.ovsbackup");
        File.WriteAllBytes(outside, [1]);
        File.WriteAllBytes(Path.Combine(backups, "ohne-endung"), [1]);
        var sub = Directory.CreateDirectory(Path.Combine(backups, "unter"));
        File.WriteAllBytes(Path.Combine(sub.FullName, "tief.ovsbackup"), [1]);

        var names = new[]
        {
            "../draussen.ovsbackup", "..\\draussen.ovsbackup", outside, "../server-data.json", "ohne-endung",
            "unter/tief.ovsbackup", "unter\\tief.ovsbackup", "", ".ovsbackup", "../" + DataStore.FileName,
        };
        int n = 0;
        foreach (var name in names)
        {
            foreach (Request request in new Request[] { new DeleteBackup(name), new RestoreBackup(name) })
            {
                var id = $"x{n++}";
                await admin.SendAsync(request with { RequestId = id });
                Assert.Equal(Codes.NotFound, (await admin.ErrorAsync(id)).Code);
            }
        }
        Assert.True(File.Exists(outside));
        Assert.True(File.Exists(Path.Combine(backups, "ohne-endung")));
        Assert.True(File.Exists(Path.Combine(sub.FullName, "tief.ovsbackup")));
        Assert.True(File.Exists(Path.Combine(server.DataDir, DataStore.FileName)));

        await admin.SendAsync(new CreateBackup());
        var real = Assert.Single((await admin.WaitForAsync<BackupList>()).Backups).FileName;
        foreach (Request request in new Request[] { new ListBackups(), new CreateBackup(), new DeleteBackup(real), new RestoreBackup(real) })
        {
            var id = $"g{n++}";
            await guest.SendAsync(request with { RequestId = id });
            Assert.Equal(Codes.PermissionDenied, (await guest.ErrorAsync(id)).Code);
        }
        Assert.Single(Directory.GetFiles(backups, "*.ovsbackup"));
        await guest.AssertNoMessageAsync<Disconnected>();
    }

    // ---- Package 75: download to and upload from the admin's PC, in chunks over the control connection ----

    const int Chunk = 512 * 1024;

    static string[] UploadFiles(string dataDir) =>
        Directory.Exists(Path.Combine(dataDir, "backups"))
            ? Directory.GetFiles(Path.Combine(dataDir, "backups"), ".upload-*")
            : [];

    [Fact]
    public async Task Download_ChunkedByteIdentical()
    {
        var (server, admin) = await StartWithAdminAsync();
        await using var _ = server;
        await using var a = admin;
        var logo = new byte[1_500_000];
        Random.Shared.NextBytes(logo); // does not compress: the archive needs three chunks
        File.WriteAllBytes(Path.Combine(server.DataDir, ServerIconStore.FileName), logo);
        await admin.SendAsync(new CreateBackup());
        var info = Assert.Single((await admin.WaitForAsync<BackupList>()).Backups);
        var path = Path.Combine(server.DataDir, "backups", info.FileName);

        using var received = new MemoryStream();
        int chunks = 0;
        while (true)
        {
            var id = $"d{chunks}";
            await admin.SendAsync(new DownloadBackup(info.FileName, received.Length) { RequestId = id });
            var chunk = await admin.WaitForAsync<BackupChunk>(c => c.RequestId == id);
            await admin.AssertNoMessageAsync<BackupChunk>(100); // one chunk per request, the next only on request
            Assert.Equal(info.FileName, chunk.FileName);
            Assert.Equal(received.Length, chunk.Offset);
            Assert.Equal(new FileInfo(path).Length, chunk.TotalSize);
            var bytes = Convert.FromBase64String(chunk.DataBase64);
            Assert.InRange(bytes.Length, 1, Chunk);
            received.Write(bytes);
            chunks++;
            if (chunks == 1) // AC4: other requests go through between two chunks
            {
                await admin.SendAsync(new SendChat(ChatTarget.Server, null, "während des Downloads"));
                await admin.WaitForAsync<ChatMessage>(m => m.Text == "während des Downloads");
            }
            Assert.Equal(received.Length == chunk.TotalSize, chunk.IsLast);
            if (chunk.IsLast) break;
        }
        Assert.Equal(3, chunks);
        Assert.Equal(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(path)), System.Security.Cryptography.SHA256.HashData(received.ToArray()));

        await admin.SendAsync(new DownloadBackup(info.FileName, received.Length + 1) { RequestId = "weit" });
        Assert.Equal(Codes.InvalidValue, (await admin.ErrorAsync("weit")).Code);
        await admin.SendAsync(new DownloadBackup(info.FileName, -1) { RequestId = "minus" });
        Assert.Equal(Codes.InvalidValue, (await admin.ErrorAsync("minus")).Code);
        await admin.SendAsync(new DownloadBackup("../" + DataStore.FileName, 0) { RequestId = "raus" });
        Assert.Equal(Codes.NotFound, (await admin.ErrorAsync("raus")).Code);
    }

    /// <summary>Sends the bytes in chunks of the given size, each after the answer to the one before; returns the last answer.</summary>
    static async Task<Message> UploadAsync(TestClient client, byte[] bytes, int chunkSize = Chunk, string? uploadId = null)
    {
        uploadId ??= Guid.NewGuid().ToString("N");
        for (int offset = 0, n = 0; ; offset += chunkSize, n++)
        {
            var size = Math.Min(chunkSize, bytes.Length - offset);
            var last = offset + size >= bytes.Length;
            var id = $"u{n}";
            await client.SendAsync(new UploadBackupChunk(uploadId, offset, Convert.ToBase64String(bytes, offset, size), last) { RequestId = id });
            var answer = await client.WaitForAsync<Message>(m => m is UploadBackupAck { RequestId: var r } && r == id
                                                                 || m is BackupUploaded { RequestId: var u } && u == id
                                                                 || m is Error { RequestId: var e } && e == id);
            if (answer is UploadBackupAck ack) Assert.Equal(offset + size, ack.Received);
            if (last || answer is not UploadBackupAck) return answer;
        }
    }

    [Fact]
    public async Task Upload_ValidatedStored_Listed()
    {
        var (server, admin) = await StartWithAdminAsync();
        await using var _ = server;
        await using var a = admin;
        await admin.SendAsync(new CreateChannel("Hochgeladen", ""));
        await admin.WaitForAsync<ChannelAdded>();
        await admin.SendAsync(new CreateBackup());
        var original = Assert.Single((await admin.WaitForAsync<BackupList>()).Backups);
        var bytes = File.ReadAllBytes(Path.Combine(server.DataDir, "backups", original.FileName));
        await admin.SendAsync(new DeleteBackup(original.FileName) { RequestId = "weg" });
        Assert.Empty((await admin.WaitForAsync<BackupList>(l => l.RequestId == "weg")).Backups);

        var answer = Assert.IsType<BackupUploaded>(await UploadAsync(admin, bytes, chunkSize: 1000)); // several chunks
        Assert.EndsWith(".ovsbackup", answer.Backup.FileName);
        Assert.Equal(bytes.Length, answer.Backup.Size);
        Assert.Equal(original.CreatedAt, answer.Backup.CreatedAt);
        var list = await admin.WaitForAsync<BackupList>(l => l.RequestId == answer.RequestId);
        Assert.Equal(answer.Backup, Assert.Single(list.Backups));
        Assert.Equal(bytes, File.ReadAllBytes(Path.Combine(server.DataDir, "backups", answer.Backup.FileName)));
        Assert.Empty(UploadFiles(server.DataDir));
        Assert.Contains(server.Log, l => l.Contains($"Backup {answer.Backup.FileName} hochgeladen von chef"));

        // a second upload of the same file gets a name of its own
        var again = Assert.IsType<BackupUploaded>(await UploadAsync(admin, bytes));
        Assert.NotEqual(answer.Backup.FileName, again.Backup.FileName);
    }

    [Fact]
    public async Task Upload_TooLargeOrAborted_NoFileLeft()
    {
        var (server, admin) = await StartWithAdminAsync(); // disposed through RestartAsync at the end
        var backups = Path.Combine(server.DataDir, "backups");

        // over 50 MB: refused as soon as a chunk crosses the limit
        var tooLarge = await UploadAsync(admin, new byte[50 * 1024 * 1024 + 1]);
        Assert.Equal(Codes.BackupTooLarge, Assert.IsType<Error>(tooLarge).Code);
        Assert.Empty(UploadFiles(server.DataDir));

        // a chunk bigger than 512 KB, a wrong offset, a bad id and bad base64
        var id = Guid.NewGuid().ToString("N");
        await admin.SendAsync(new UploadBackupChunk(id, 0, Convert.ToBase64String(new byte[Chunk + 1]), false) { RequestId = "gross" });
        Assert.Equal(Codes.InvalidValue, (await admin.ErrorAsync("gross")).Code);
        await admin.SendAsync(new UploadBackupChunk(id, 0, Convert.ToBase64String(new byte[10]), false) { RequestId = "ok" });
        Assert.Equal(10, (await admin.WaitForAsync<UploadBackupAck>(r => r.RequestId == "ok")).Received);
        await admin.SendAsync(new UploadBackupChunk(id, 5, Convert.ToBase64String(new byte[10]), false) { RequestId = "luecke" });
        Assert.Equal(Codes.InvalidValue, (await admin.ErrorAsync("luecke")).Code);
        Assert.Empty(UploadFiles(server.DataDir));
        foreach (var bad in new[] { "../../x", "..\\x", "abc", "", id.ToUpperInvariant(), id + "0" })
        {
            await admin.SendAsync(new UploadBackupChunk(bad, 0, Convert.ToBase64String(new byte[10]), false) { RequestId = "id" + bad });
            Assert.Equal(Codes.InvalidValue, (await admin.ErrorAsync("id" + bad)).Code);
        }
        await admin.SendAsync(new UploadBackupChunk(id, 0, "kein base64!", false) { RequestId = "b64" });
        Assert.Equal(Codes.InvalidValue, (await admin.ErrorAsync("b64")).Code);
        Assert.Empty(UploadFiles(server.DataDir));
        Assert.False(File.Exists(Path.Combine(server.DataDir, "x")));

        // the connection ends in the middle of an upload
        await admin.SendAsync(new UploadBackupChunk(id, 0, Convert.ToBase64String(new byte[10]), false) { RequestId = "halb" });
        await admin.WaitForAsync<UploadBackupAck>(r => r.RequestId == "halb");
        Assert.Single(UploadFiles(server.DataDir));
        await admin.DisposeAsync();
        for (int i = 0; i < 50 && UploadFiles(server.DataDir).Length > 0; i++) await Task.Delay(50);
        Assert.Empty(UploadFiles(server.DataDir));
        Assert.Empty(Directory.GetFiles(backups, "*.ovsbackup"));

        // left over from a crash: removed on the next start
        File.WriteAllBytes(Path.Combine(backups, ".upload-" + Guid.NewGuid().ToString("N")), [1, 2, 3]);
        await using var restarted = await server.RestartAsync();
        Assert.Empty(UploadFiles(restarted.DataDir));
        Assert.True(File.Exists(Path.Combine(backups, "..", DataStore.FileName)));
    }

    [Fact]
    public async Task Upload_InvalidArchive_Rejected()
    {
        var (server, admin) = await StartWithAdminAsync();
        await using var _ = server;
        await using var a = admin;
        await using var guest = await TestClient.ConnectAsync(server, "gast");
        await admin.SendAsync(new CreateBackup());
        var good = Entries(Path.Combine(server.DataDir, "backups", Assert.Single((await admin.WaitForAsync<BackupList>()).Backups).FileName));
        var scratch = Path.Combine(dir, "ohne-manifest.zip");
        WriteArchive(scratch, good.Where(e => e.Key != "manifest.json").ToDictionary());

        foreach (var bytes in new[] { new byte[] { 1, 2, 3, 4 }, File.ReadAllBytes(scratch) })
        {
            var answer = Assert.IsType<Error>(await UploadAsync(admin, bytes));
            Assert.Equal(Codes.InvalidBackup, answer.Code);
        }
        var name = Assert.Single(Directory.GetFiles(Path.Combine(server.DataDir, "backups"), "*.ovsbackup"));
        Assert.Empty(UploadFiles(server.DataDir));
        Assert.Contains(server.Log, l => l.Contains("Hochgeladenes Backup ist ungültig"));

        // AC5: the right ServerConfig for both directions
        await guest.SendAsync(new DownloadBackup(Path.GetFileName(name), 0) { RequestId = "g1" });
        Assert.Equal(Codes.PermissionDenied, (await guest.ErrorAsync("g1")).Code);
        await guest.SendAsync(new UploadBackupChunk(Guid.NewGuid().ToString("N"), 0, Convert.ToBase64String(File.ReadAllBytes(name)), true) { RequestId = "g2" });
        Assert.Equal(Codes.PermissionDenied, (await guest.ErrorAsync("g2")).Code);
        Assert.Single(Directory.GetFiles(Path.Combine(server.DataDir, "backups"), "*.ovsbackup"));
        Assert.Empty(UploadFiles(server.DataDir));
    }

    // ---- Restore through the real host: the run ends and a new one starts on the restored files ----

    static async Task<TestClient?> TryConnectAsync(int port, string nickname, ClientIdentity identity)
    {
        try
        {
            var client = await TestClient.OpenAsync(port, identity);
            if (await client.HandshakeAsync(nickname) is Welcome) return client;
            await client.DisposeAsync();
            return null;
        }
        catch (Exception e) when (e is SocketException or IOException)
        {
            return null;
        }
    }

    static async Task<TestClient> ConnectWhenUpAsync(int port, string nickname, ClientIdentity identity, Task host)
    {
        for (int i = 0; i < 100 && !host.IsCompleted; i++)
        {
            if (await TryConnectAsync(port, nickname, identity) is { } client) return client;
            await Task.Delay(100);
        }
        throw new TimeoutException($"{nickname} could not connect");
    }

    sealed class Host(Task<int> run, int port, CancellationTokenSource stop) : IAsyncDisposable
    {
        public Task<int> Run => run;
        public int Port => port;

        public async ValueTask DisposeAsync()
        {
            stop.Cancel();
            Assert.Equal(0, await run.WaitAsync(TimeSpan.FromSeconds(10)));
            stop.Dispose();
        }
    }

    /// <summary>Starts ServerHost on the data dir; a port that Windows reserves makes the run end with 1, then another is tried.</summary>
    static async Task<(Host Host, TestClient Admin)> StartHostAsync(string dataDir, ClientIdentity admin)
    {
        for (int attempt = 0; ; attempt++)
        {
            int port = Random.Shared.Next(20_000, 45_000);
            var env = new Dictionary<string, string> { ["OVS_DATA_DIR"] = dataDir, ["OVS_PORT"] = port.ToString() };
            var output = new ConcurrentQueue<string>();
            var stop = new CancellationTokenSource();
            var run = ServerHost.RunAsync(env.GetValueOrDefault, IPAddress.Loopback, TimeProvider.System, output.Enqueue, output.Enqueue, stop.Token);
            try
            {
                var client = await ConnectWhenUpAsync(port, "chef", admin, run);
                return (new Host(run, port, stop), client);
            }
            catch (TimeoutException) when (attempt < 20 && run.IsCompleted)
            {
                stop.Dispose();
            }
        }
    }

    static void SeedAdmin(string dataDir, ClientIdentity admin, Action<ServerData>? more = null)
    {
        var store = new DataStore(Path.Combine(dataDir, DataStore.FileName));
        var data = store.LoadOrCreate(() => ServerData.CreateDefault(new ServerConfig(0, dataDir, 50, "Testserver", "")));
        TestServer.Grant(admin, "Admin")(data);
        more?.Invoke(data);
        store.Save(data);
    }

    [Fact]
    public async Task Restore_ReplacesState_SafetyBackup_RestartsRun()
    {
        var adminId = ClientIdentity.Create();
        SeedAdmin(dir, adminId);
        string backup;
        byte[] backupCertHash;
        var (first, admin) = await StartHostAsync(dir, adminId);
        await using (first)
        await using (admin)
        {
            await admin.SendAsync(new CreateChannel("Alt", ""));
            await admin.WaitForAsync<ChannelAdded>();
            await admin.SendAsync(new CreateBackup());
            backup = Assert.Single((await admin.WaitForAsync<BackupList>()).Backups).FileName;
            backupCertHash = admin.ServerCertHash;
        }
        File.Delete(Path.Combine(dir, "cert.pfx")); // the next start makes a new certificate

        var (host, admin2) = await StartHostAsync(dir, adminId);
        await using (host)
        {
            Assert.NotEqual(backupCertHash, admin2.ServerCertHash);
            await using var anna = await ConnectWhenUpAsync(host.Port, "anna", ClientIdentity.Create(), host.Run);
            await admin2.SendAsync(new CreateChannel("Neu", ""));
            await admin2.WaitForAsync<ChannelAdded>();

            await admin2.SendAsync(new RestoreBackup(backup));
            Assert.Equal(Codes.Restoring, (await admin2.WaitForAsync<Disconnected>(timeoutMs: 10_000)).Reason);
            Assert.Equal(Codes.Restoring, (await anna.WaitForAsync<Disconnected>(timeoutMs: 10_000)).Reason);
            await admin2.DisposeAsync();

            await using var again = await ConnectWhenUpAsync(host.Port, "chef", adminId, host.Run);
            var channels = again.Welcome.Snapshot.Channels.Select(c => c.Name).ToList();
            Assert.Contains("Alt", channels);
            Assert.DoesNotContain("Neu", channels);
            Assert.Equal(backupCertHash, again.ServerCertHash);
            Assert.True(again.Welcome.Snapshot.Users.Single(u => u.Nickname == "chef").Permissions.Has(Permission.ServerConfig));

            await again.SendAsync(new ListBackups { RequestId = "l" });
            var list = (await again.WaitForAsync<BackupList>(l => l.RequestId == "l")).Backups;
            var safety = Assert.Single(list, b => b.FileName.StartsWith("vor-wiederherstellung_"));
            Assert.Contains(list, b => b.FileName == backup);
            Assert.Contains(DataOf(Entries(Path.Combine(dir, "backups", safety.FileName))["server-data.json"]).Channels, c => c.Name == "Neu");
        }
        var log = string.Join("\n", Directory.GetFiles(Path.Combine(dir, "logs", "server")).Select(File.ReadAllText));
        Assert.Contains($"Backup {backup} wird wiederhergestellt von chef", log);
        Assert.Contains("Backup eingespielt", log);
    }

    [Fact]
    public async Task Restore_OlderDataVersion_Migrated()
    {
        var adminId = ClientIdentity.Create();
        SeedAdmin(dir, adminId);
        var (host, admin) = await StartHostAsync(dir, adminId);
        await using (host)
        {
            // a backup from a version 1 server: guests could not chat yet
            var old = DataOf(File.ReadAllBytes(Path.Combine(dir, DataStore.FileName)));
            old.DataVersion = 1;
            int guest = old.Groups.FindIndex(g => g.Id == OVS.Server.Permissions.PermissionRules.GuestGroupId);
            old.Groups[guest] = old.Groups[guest] with { Permissions = Permission.Speak };
            old.Channels.Add(new ChannelRecord { Id = Guid.NewGuid(), Name = "Früher", Order = 1 });
            var backups = Directory.CreateDirectory(Path.Combine(dir, "backups")).FullName;
            WriteArchive(Path.Combine(backups, "alt.ovsbackup"), new()
            {
                ["manifest.json"] = Manifest(1),
                ["server-data.json"] = JsonSerializer.SerializeToUtf8Bytes(old, ProtocolJson.Options),
                ["cert.pfx"] = File.ReadAllBytes(Path.Combine(dir, "cert.pfx")),
            });

            File.WriteAllBytes(Path.Combine(dir, ServerIconStore.FileName), TestImages.Encode(64, 64)); // a logo the backup does not have
            await admin.SendAsync(new RestoreBackup("alt.ovsbackup"));
            Assert.Equal(Codes.Restoring, (await admin.WaitForAsync<Disconnected>(timeoutMs: 10_000)).Reason);
            await admin.DisposeAsync();

            await using var again = await ConnectWhenUpAsync(host.Port, "chef", adminId, host.Run);
            Assert.Contains(again.Welcome.Snapshot.Channels, c => c.Name == "Früher");
            var guestGroup = again.Welcome.Snapshot.Groups.Single(g => g.Id == OVS.Server.Permissions.PermissionRules.GuestGroupId);
            Assert.True(guestGroup.Permissions.Has(Permission.ChatChannel));
            Assert.False(File.Exists(Path.Combine(dir, ServerIconStore.FileName))); // the backup had no logo
        }
        Assert.Equal(ServerData.CurrentVersion, DataOf(File.ReadAllBytes(Path.Combine(dir, DataStore.FileName))).DataVersion);
    }

    // ---- Package 89 (A101): the right BackupsManage, upload and restore only for the Admin group, full validation ----

    static Action<ServerData> AddGroup(string name, Permission permissions) =>
        d => d.Groups.Add(new OVS.Server.Permissions.Group(Guid.NewGuid(), name, permissions));

    [Fact]
    public async Task BackupsManage_Required_ServerConfigAloneNotEnough()
    {
        var config = ClientIdentity.Create();
        var keeper = ClientIdentity.Create();
        await using var server = await TestServer.StartAsync(d =>
        {
            AddGroup("Konfig", Permission.ServerConfig)(d);
            AddGroup("Sicherung", Permission.BackupsManage)(d);
            TestServer.Grant(config, "Konfig")(d);
            TestServer.Grant(keeper, "Sicherung")(d);
        });
        await using var c = await TestClient.ConnectAsync(server, "konfig", config);
        await using var k = await TestClient.ConnectAsync(server, "sicherung", keeper);

        await k.SendAsync(new CreateBackup { RequestId = "c" });
        var name = Assert.Single((await k.WaitForAsync<BackupList>(l => l.RequestId == "c")).Backups).FileName;
        Assert.Contains(server.Log, l => l.Contains($"Backup {name} angelegt von sicherung"));
        int n = 0;
        foreach (Request request in new Request[] { new ListBackups(), new CreateBackup(), new DownloadBackup(name, 0), new DeleteBackup(name) })
        {
            var id = $"c{n++}";
            await c.SendAsync(request with { RequestId = id });
            Assert.Equal(Codes.PermissionDenied, (await c.ErrorAsync(id)).Code);
        }

        await k.SendAsync(new ListBackups { RequestId = "l" });
        Assert.Single((await k.WaitForAsync<BackupList>(l => l.RequestId == "l")).Backups);
        await k.SendAsync(new DownloadBackup(name, 0) { RequestId = "d" });
        Assert.True((await k.WaitForAsync<BackupChunk>(ch => ch.RequestId == "d")).IsLast);
        // uploading and restoring are not part of the right
        await k.SendAsync(new RestoreBackup(name) { RequestId = "r" });
        Assert.Equal(Codes.PermissionDenied, (await k.ErrorAsync("r")).Code);
        var bytes = File.ReadAllBytes(Path.Combine(server.DataDir, "backups", name));
        Assert.Equal(Codes.PermissionDenied, Assert.IsType<Error>(await UploadAsync(k, bytes)).Code);
        Assert.Empty(UploadFiles(server.DataDir));
        await k.SendAsync(new DeleteBackup(name) { RequestId = "x" });
        Assert.Empty((await k.WaitForAsync<BackupList>(l => l.RequestId == "x")).Backups);
        await k.AssertNoMessageAsync<Disconnected>();
    }

    [Fact]
    public async Task UploadAndRestore_OnlyAdminGroup_CheckedPerChunk()
    {
        var second = ClientIdentity.Create();
        var almighty = ClientIdentity.Create();
        var (server, admin) = await StartWithAdminAsync(seed: d =>
        {
            TestServer.Grant(second, "Admin")(d);
            AddGroup("Alles", Permission.All)(d);
            TestServer.Grant(almighty, "Alles")(d);
        });
        await using var _ = server;
        await using var a = admin;
        await using var two = await TestClient.ConnectAsync(server, "zweiter", second);
        await using var all = await TestClient.ConnectAsync(server, "allmacht", almighty);
        await admin.SendAsync(new CreateBackup());
        var name = Assert.Single((await admin.WaitForAsync<BackupList>()).Backups).FileName;
        var backups = Path.Combine(server.DataDir, "backups");
        var bytes = File.ReadAllBytes(Path.Combine(backups, name));

        // every right, but not in the Admin group
        Assert.Equal(Codes.PermissionDenied, Assert.IsType<Error>(await UploadAsync(all, bytes)).Code);
        await all.SendAsync(new RestoreBackup(name) { RequestId = "r" });
        Assert.Equal(Codes.PermissionDenied, (await all.ErrorAsync("r")).Code);
        Assert.Empty(UploadFiles(server.DataDir));
        await all.SendAsync(new ListBackups { RequestId = "l" });
        Assert.Single((await all.WaitForAsync<BackupList>(l => l.RequestId == "l")).Backups);

        // checked on every chunk: out of the Admin group in the middle of an upload, the next chunk is refused and the file goes
        var id = Guid.NewGuid().ToString("N");
        await two.SendAsync(new UploadBackupChunk(id, 0, Convert.ToBase64String(bytes, 0, 100), false) { RequestId = "c0" });
        await two.WaitForAsync<UploadBackupAck>(r => r.RequestId == "c0");
        Assert.Single(UploadFiles(server.DataDir));
        LeaveAdminGroup(server.State, second.Fingerprint);
        await two.WaitForAsync<UserUpdated>(u => u.User.Nickname == "zweiter" && !u.User.GroupIds.Contains(WellKnownGroups.Admin));
        await two.SendAsync(new UploadBackupChunk(id, 100, Convert.ToBase64String(bytes, 100, bytes.Length - 100), true) { RequestId = "c1" });
        Assert.Equal(Codes.PermissionDenied, (await two.ErrorAsync("c1")).Code);
        Assert.Empty(UploadFiles(server.DataDir));
        Assert.Single(Directory.GetFiles(backups, "*.ovsbackup"));

        // a member of the Admin group can
        Assert.IsType<BackupUploaded>(await UploadAsync(admin, bytes));
    }

    /// <summary>
    /// Package 84 (A102): nobody outranks a member of the Admin group, so no request can take it away from another admin.
    /// The test changes the stored groups itself, under the server's lock, and lets the server apply them as after a group change.
    /// </summary>
    static void LeaveAdminGroup(ServerState state, string fingerprint)
    {
        const System.Reflection.BindingFlags flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
        lock (typeof(ServerState).GetField("gate", flags)!.GetValue(state)!)
        {
            var data = (ServerData)typeof(ServerState).GetField("data", flags)!.GetValue(state)!;
            data.Users.Single(u => u.Fingerprint == fingerprint).GroupIds.Remove(WellKnownGroups.Admin);
            typeof(ServerState).GetMethod("RecomputePermissions", flags)!.Invoke(state, [false]);
        }
    }

    /// <summary>Content that passed the old checks but broke every later start, or references that do not fit together.</summary>
    static void Break(ServerData d, string variant)
    {
        switch (variant)
        {
            case "users-null": d.Users = null!; break;
            case "settings-null": d.Settings = null!; break;
            case "channels-null": d.Channels = null!; break;
            case "links-null": d.Links = null!; break;
            case "groups-null": d.Groups = null!; break;
            case "bans-null": d.Bans = null!; break;
            case "user-entry-null": d.Users.Add(null!); break;
            case "user-groups-null": d.Users[0].GroupIds = null!; break;
            case "channel-name-empty": d.Channels[0].Name = " "; break;
            case "channel-name-control": d.Channels[0].Name = "a\u0007b"; break;
            case "group-name-too-long": d.Groups[1] = d.Groups[1] with { Name = new string('g', 33) }; break;
            case "server-name-null": d.Settings.Name = null!; break;
            case "channel-id-twice": d.Channels.Add(new ChannelRecord { Id = d.Channels[0].Id, Name = "Zweite" }); break;
            case "group-id-twice": d.Groups.Add(d.Groups[1] with { Name = "Kopie" }); break;
            case "user-twice": d.Users.Add(new UserRecord { Fingerprint = d.Users[0].Fingerprint, LastNickname = "doppelt" }); break;
            case "dangling-group": d.Users[0].GroupIds.Add(Guid.NewGuid()); break;
            case "dangling-link": d.Links.Add(ChannelLink.Of(d.Channels[0].Id, Guid.NewGuid())); break;
            case "no-default-channel": d.DefaultChannelId = Guid.NewGuid(); break;
            case "no-admin": d.Users.ForEach(u => u.GroupIds.Remove(WellKnownGroups.Admin)); break;
            case "bad-password-hash": d.Settings.PasswordHash = "kein-hash"; break;
            default: throw new ArgumentException(variant);
        }
    }

    [Theory]
    [InlineData("users-null")]
    [InlineData("settings-null")]
    [InlineData("channels-null")]
    [InlineData("links-null")]
    [InlineData("groups-null")]
    [InlineData("bans-null")]
    [InlineData("user-entry-null")]
    [InlineData("user-groups-null")]
    [InlineData("channel-name-empty")]
    [InlineData("channel-name-control")]
    [InlineData("group-name-too-long")]
    [InlineData("server-name-null")]
    [InlineData("channel-id-twice")]
    [InlineData("group-id-twice")]
    [InlineData("user-twice")]
    [InlineData("dangling-group")]
    [InlineData("dangling-link")]
    [InlineData("no-default-channel")]
    [InlineData("no-admin")]
    [InlineData("bad-password-hash")]
    public async Task Restore_InvalidContent_RejectedServerStillStarts(string variant)
    {
        var (server, admin) = await StartWithAdminAsync(); // disposed through RestartAsync at the end
        await admin.SendAsync(new CreateBackup());
        var backups = Path.Combine(server.DataDir, "backups");
        var good = Entries(Path.Combine(backups, Assert.Single((await admin.WaitForAsync<BackupList>()).Backups).FileName));
        var data = DataOf(good["server-data.json"]);
        Break(data, variant);
        var archive = Path.Combine(dir, variant + ".ovsbackup");
        WriteArchive(archive, new(good) { ["server-data.json"] = JsonSerializer.SerializeToUtf8Bytes(data, ProtocolJson.Options) });

        // uploaded: refused with the same check, no file left
        Assert.Equal(Codes.InvalidBackup, Assert.IsType<Error>(await UploadAsync(admin, File.ReadAllBytes(archive))).Code);
        Assert.Empty(UploadFiles(server.DataDir));
        Assert.Contains(server.Log, l => l.Contains("Hochgeladenes Backup ist ungültig"));

        // put on the server by hand: the restore is refused and nothing is applied
        File.Copy(archive, Path.Combine(backups, "kaputt.ovsbackup"));
        var dataBefore = File.ReadAllBytes(Path.Combine(server.DataDir, DataStore.FileName));
        await admin.SendAsync(new RestoreBackup("kaputt.ovsbackup") { RequestId = "r" });
        Assert.Equal(Codes.InvalidBackup, (await admin.ErrorAsync("r")).Code);
        await admin.AssertNoMessageAsync<Disconnected>();
        Assert.Equal(dataBefore, File.ReadAllBytes(Path.Combine(server.DataDir, DataStore.FileName)));
        Assert.DoesNotContain(Directory.GetFiles(backups), f => Path.GetFileName(f).StartsWith("vor-wiederherstellung_"));
        await admin.DisposeAsync();

        // and the server still starts
        await using var restarted = await server.RestartAsync();
        await using var anna = await TestClient.ConnectAsync(restarted, "anna");
        Assert.Contains(anna.Welcome.Snapshot.Channels, c => c.Name == "Lobby");
    }

    [Fact]
    public async Task ManifestCap_Quota_ListingCached()
    {
        var time = new ManualTimeProvider(); // Package 86: one CreateBackup per 10 s and session
        var (server, admin) = await StartWithAdminAsync(time);
        await using var _ = server;
        await using var a = admin;
        await admin.SendAsync(new CreateBackup());
        var first = Assert.Single((await admin.WaitForAsync<BackupList>()).Backups).FileName;
        var backups = Path.Combine(server.DataDir, "backups");
        var bytes = File.ReadAllBytes(Path.Combine(backups, first));
        var good = Entries(Path.Combine(backups, first));

        // a manifest over 16 KB is never read: listed without a version, refused for a restore
        var big = JsonSerializer.SerializeToUtf8Bytes(new
        {
            formatVersion = 1, dataVersion = ServerData.CurrentVersion, serverVersion = new string('x', 17 * 1024), createdAt = DateTimeOffset.UtcNow,
        });
        WriteArchive(Path.Combine(backups, "gross.ovsbackup"), new(good) { ["manifest.json"] = big });
        await admin.SendAsync(new ListBackups { RequestId = "l" });
        Assert.Equal("?", (await admin.WaitForAsync<BackupList>(l => l.RequestId == "l")).Backups.Single(b => b.FileName == "gross.ovsbackup").ServerVersion);
        await admin.SendAsync(new RestoreBackup("gross.ovsbackup") { RequestId = "r" });
        Assert.Equal(Codes.InvalidBackup, (await admin.ErrorAsync("r")).Code);

        // at most 50 backups: creating and uploading beyond that are refused
        for (int i = 0; i < 48; i++) File.WriteAllBytes(Path.Combine(backups, $"alt-{i:00}.ovsbackup"), [1]);
        time.Advance(TimeSpan.FromMinutes(1));
        await admin.SendAsync(new CreateBackup { RequestId = "q" });
        Assert.Equal(Codes.BackupQuotaExceeded, (await admin.ErrorAsync("q")).Code);
        Assert.Equal(Codes.BackupQuotaExceeded, Assert.IsType<Error>(await UploadAsync(admin, bytes)).Code);
        Assert.Empty(UploadFiles(server.DataDir));
        Assert.Equal(50, Directory.GetFiles(backups, "*.ovsbackup").Length);
        Assert.Contains(server.Log, l => l.Contains("Backup-Limit erreicht"));
        await admin.SendAsync(new DeleteBackup("alt-00.ovsbackup") { RequestId = "d" });
        Assert.Equal(49, (await admin.WaitForAsync<BackupList>(l => l.RequestId == "d")).Total);
        Assert.IsType<BackupUploaded>(await UploadAsync(admin, bytes));

        // at most MaxBytes in total (a small limit here instead of 2 GB)
        var data = Directory.CreateDirectory(Path.Combine(dir, "daten")).FullName;
        foreach (var file in new[] { DataStore.FileName, "cert.pfx" }) File.Copy(Path.Combine(server.DataDir, file), Path.Combine(data, file));
        var probe = new BackupStore(data, time).Create();
        var store = new BackupStore(data, time, maxCount: 50, maxBytes: probe.Size * 2 + probe.Size / 2);
        time.Advance(TimeSpan.FromSeconds(1));
        store.Create();
        time.Advance(TimeSpan.FromSeconds(1));
        Assert.Throws<BackupQuotaException>(() => store.Create());
        Assert.Equal(2, store.List().Count);
        Assert.Empty(Directory.GetFiles(Path.Combine(data, "backups"), "*.tmp"));

        // the listing is cached: an archive changed in place (the folder itself unchanged) keeps its listed version
        var listed = store.List();
        var path = Path.Combine(store.Folder, listed[0].FileName);
        var changed = Path.Combine(dir, "geaendert.zip");
        WriteArchive(changed, new(Entries(path))
        {
            ["manifest.json"] = JsonSerializer.SerializeToUtf8Bytes(new { formatVersion = 1, dataVersion = 1, serverVersion = "geaendert", createdAt = listed[0].CreatedAt }),
        });
        File.WriteAllBytes(path, File.ReadAllBytes(changed));
        Assert.Equal(listed, store.List());
        // refreshed once the store changes files
        store.Delete(Path.Combine(store.Folder, listed[1].FileName));
        Assert.Equal("geaendert", Assert.Single(store.List()).ServerVersion);
        // and when a file is added or removed from outside
        File.Delete(path);
        Assert.Empty(store.List());
    }
}
