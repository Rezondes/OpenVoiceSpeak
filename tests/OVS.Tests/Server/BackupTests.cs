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

    static async Task<(TestServer Server, TestClient Admin)> StartWithAdminAsync(TimeProvider? time = null)
    {
        var admin = ClientIdentity.Create();
        var server = await TestServer.StartAsync(TestServer.Grant(admin, "Admin"), time: time);
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
        var (server, admin) = await StartWithAdminAsync(time);
        await using var _ = server;
        await using var a = admin;
        await admin.SendAsync(new CreateBackup());
        await admin.WaitForAsync<BackupList>();
        time.Advance(TimeSpan.FromMinutes(1));
        await admin.SendAsync(new CreateBackup());
        await admin.WaitForAsync<BackupList>();
        await admin.SendAsync(new CreateBackup()); // same second: a name of its own
        await admin.WaitForAsync<BackupList>();

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
}
