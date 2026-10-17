using System.Collections.Concurrent;
using System.Globalization;
using System.IO.Compression;
using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using OVS.Server;
using OVS.Server.Data;
using OVS.Server.Data.Records;
using OVS.Server.Exceptions;
using OVS.Server.State;
using OVS.Shared.Identity;
using OVS.Shared.Permissions;
using OVS.Shared.Protocol;
using OVS.Shared.Protocol.Framing;
using OVS.Shared.Protocol.Messages;
using OVS.Shared.Protocol.Messages.Channels;
using OVS.Shared.Protocol.Messages.Chat;
using OVS.Shared.Protocol.Messages.Connection;
using OVS.Shared.Protocol.Messages.Groups;
using OVS.Shared.Protocol.Messages.ServerBackups;
using OVS.Shared.Protocol.Messages.ServerInfo;
using OVS.Shared.Protocol.Messages.Users;
using OVS.Tests.TestSupport;

namespace OVS.Tests.Server.Data;

/// <summary>Package 74: backups on the server, created, listed, deleted and restored from the administration.</summary>
public sealed class BackupTests() : TempDirectoryTest("ovs-backup-", quiet: true)
{
    static Dictionary<string, byte[]> Entries(string path)
    {
        using var zip = ZipFile.OpenRead(path);
        return zip.Entries.ToDictionary(zipArchiveEntry => zipArchiveEntry.FullName, zipArchiveEntry =>
        {
            using var stream = zipArchiveEntry.Open();
            using var copy = new MemoryStream();
            stream.CopyTo(copy);
            return copy.ToArray();
        });
    }

    static ServerData DataOf(byte[] json) => JsonSerializer.Deserialize<ServerData>(json, ProtocolJson.Options)!;

    static async Task<AdminRig> StartWithAdminAsync(TimeProvider? time = null, Action<ServerData>? seed = null)
    {
        var admin = ClientIdentity.Create();
        var server = await TestServer.StartAsync(serverData => { TestServer.Grant(admin, "Admin")(serverData); seed?.Invoke(serverData); }, time: time);
        return new AdminRig(server, await TestClient.ConnectAsync(server, "chef", admin));
    }

    [Fact]
    public async Task Create_ContainsDataCertIcon_NoLogs()
    {
        var (server, admin) = await StartWithAdminAsync();
        await using var _ = server;
        await using var client = admin;
        var png = TestImages.Encode(64, 64);
        await admin.SendAsync(new SetServerIcon(Convert.ToBase64String(png)));
        await admin.WaitForAsync<ServerSettingsChanged>(serverSettingsChanged => serverSettingsChanged.Settings.IconHash is not null);
        await admin.SendAsync(new CreateChannel("Alt", ""));
        await admin.WaitForAsync<ChannelAdded>();
        Directory.CreateDirectory(Path.Combine(server.DataDir, "logs", "server"));
        await File.WriteAllTextAsync(Path.Combine(server.DataDir, "logs", "server", "x.log"), "geheim");

        await admin.SendAsync(new CreateBackup { RequestId = "b" });
        var list = await admin.WaitForAsync<BackupList>(backupList => backupList.RequestId == "b");
        var info = Assert.Single(list.Backups);
        Assert.Matches(@"^\d{4}-\d{2}-\d{2}_\d{2}-\d{2}-\d{2}\.ovsbackup$", info.FileName);
        var path = Path.Combine(server.DataDir, "backups", info.FileName);
        Assert.Equal(new FileInfo(path).Length, info.Size);
        Assert.Equal(OVS.Shared.BuildInfo.Current.Version, info.ServerVersion);

        var entries = Entries(path);
        Assert.Equal(["cert.pfx", "manifest.json", "server-data.json", "server-icon.png"], entries.Keys.Order());
        Assert.Equal(await File.ReadAllBytesAsync(Path.Combine(server.DataDir, "cert.pfx")), entries["cert.pfx"]);
        Assert.Equal(png, entries["server-icon.png"]);
        Assert.Contains(DataOf(entries["server-data.json"]).Channels, channel => channel.Name == "Alt");
        using var manifest = JsonDocument.Parse(entries["manifest.json"]);
        Assert.Equal(ServerData.CurrentVersion, manifest.RootElement.GetProperty("dataVersion").GetInt32());
        Assert.Equal(1, manifest.RootElement.GetProperty("formatVersion").GetInt32());
        Assert.Contains(server.Log, line => line.Contains($"Backup {info.FileName} created by chef", StringComparison.Ordinal));
    }

    /// <summary>Package 168: a backup holds every profile picture under its fingerprint and a restore writes them back.</summary>
    [Fact]
    public async Task Backup_ContainsAvatars_Restored()
    {
        var (server, admin) = await StartWithAdminAsync();
        await using var _ = server;
        await using var client = admin;
        var png = TestPng.Rgba();
        await admin.SendAsync(new OVS.Shared.Protocol.Messages.Users.SetAvatar(Convert.ToBase64String(png)));
        await admin.WaitForAsync<OVS.Shared.Protocol.Messages.Users.UserUpdated>(userUpdated => userUpdated.User.Avatar is not null);
        await admin.SendAsync(new CreateBackup { RequestId = "b" });
        var path = Path.Combine(server.DataDir, "backups", Assert.Single((await admin.WaitForAsync<BackupList>(backupList => backupList.RequestId == "b")).Backups).FileName);

        var entry = $"avatars/{admin.Identity.Fingerprint}.png";
        Assert.Equal(png, Entries(path)[entry]);
        var content = BackupArchive.Read(path);
        Assert.Equal(png, content.Avatars![admin.Identity.Fingerprint]);
        var restored = Path.Combine(dir, "restored");
        Directory.CreateDirectory(Path.Combine(restored, "avatars"));
        await File.WriteAllBytesAsync(Path.Combine(restored, "avatars", new string('b', 64) + ".png"), png); // a picture made after the backup goes
        content.ApplyTo(restored);
        Assert.Equal([admin.Identity.Fingerprint + ".png"], Directory.GetFiles(Path.Combine(restored, "avatars")).Select(Path.GetFileName));
        Assert.Equal(png, await File.ReadAllBytesAsync(Path.Combine(restored, entry)));
    }

    [Fact]
    public async Task Create_RightAfterFirstConnect_ContainsUser()
    {
        var (server, admin) = await StartWithAdminAsync(new ManualTimeProvider()); // the debounced save never fires
        await using var _ = server;
        await using var testClient = admin;
        var anna = ClientIdentity.Create();
        await using var client = await TestClient.ConnectAsync(server, "anna", anna);

        await admin.SendAsync(new CreateBackup { RequestId = "b" });
        var info = Assert.Single((await admin.WaitForAsync<BackupList>(backupList => backupList.RequestId == "b")).Backups);
        var data = DataOf(Entries(Path.Combine(server.DataDir, "backups", info.FileName))["server-data.json"]);
        Assert.Contains(data.Users, user => user.Fingerprint == anna.Fingerprint);
    }

    [Fact]
    public async Task Restore_WithinSaveDelay_SafetyBackupHasNewUserAndStats()
    {
        var (server, admin) = await StartWithAdminAsync(new ManualTimeProvider()); // the debounced save never fires
        await using var _ = server;
        await using var testClient = admin;
        await admin.SendAsync(new CreateBackup { RequestId = "b" });
        var backup = Assert.Single((await admin.WaitForAsync<BackupList>(backupList => backupList.RequestId == "b")).Backups).FileName;

        var bert = ClientIdentity.Create();
        await using var client = await TestClient.ConnectAsync(server, "bert", bert);
        await client.SendAsync(new SendChat(ChatTarget.Channel, null, "hallo"));
        await client.WaitForAsync<ChatMessage>(chatMessage => chatMessage.Text == "hallo");

        await admin.SendAsync(new RestoreBackup(backup));
        // a restore writes a safety backup and the data file first, slow on a CI disk
        Assert.Equal(ReasonCodes.Restoring, (await admin.WaitForAsync<Disconnected>(timeoutMs: 30_000)).Reason);
        // The disconnect arrives while the server still holds its lock and writes the data file on closing; reading the
        // pending restore takes that lock, so the file is complete once it returns.
        Assert.NotNull(server.State.PendingRestore);
        var safety = Directory.GetFiles(Path.Combine(server.DataDir, "backups"), BackupStore.SafetyPrefix + "*").Single();
        var user = DataOf(Entries(safety)["server-data.json"]).Users.Single(userRecord => userRecord.Fingerprint == bert.Fingerprint);
        Assert.Equal((1, 1), (user.LoginCount, user.ChatMessages));

        // the running session was counted once, not again on the disconnect
        var live = new DataStore(Path.Combine(server.DataDir, DataStore.FileName)).LoadOrCreate(() => throw new InvalidOperationException());
        Assert.Equal(1, live.Users.Single(userRecord => userRecord.Fingerprint == bert.Fingerprint).ChatMessages);
    }

    [Fact]
    public async Task List_NewestFirst_Delete()
    {
        var time = new ManualTimeProvider();
        var other = ClientIdentity.Create(); // Package 86: one backup per 10 s and session, so a second admin
        var (server, admin) = await StartWithAdminAsync(time, TestServer.Grant(other, "Admin"));
        await using var _ = server;
        await using var client = admin;
        await admin.SendAsync(new CreateBackup());
        await admin.WaitForAsync<BackupList>();
        time.Advance(TimeSpan.FromMinutes(1));
        await admin.SendAsync(new CreateBackup());
        await admin.WaitForAsync<BackupList>();
        await using var second = await TestClient.ConnectAsync(server, "zweiter", other);
        await second.SendAsync(new CreateBackup()); // same second: a name of its own
        await second.WaitForAsync<BackupList>();

        await admin.SendAsync(new ListBackups { RequestId = "l" });
        var list = (await admin.WaitForAsync<BackupList>(backupList => backupList.RequestId == "l")).Backups;
        Assert.Equal(3, list.Count);
        Assert.Equal(3, list.Select(backup => backup.FileName).Distinct().Count());
        Assert.True(list[0].CreatedAt >= list[1].CreatedAt && list[1].CreatedAt > list[2].CreatedAt);

        await admin.SendAsync(new DeleteBackup(list[2].FileName) { RequestId = "d" });
        var after = (await admin.WaitForAsync<BackupList>(backupList => backupList.RequestId == "d")).Backups;
        Assert.Equal(list.Take(2).Select(backup => backup.FileName), after.Select(backup => backup.FileName));
        Assert.False(File.Exists(Path.Combine(server.DataDir, "backups", list[2].FileName)));
        Assert.Contains(server.Log, line => line.Contains($"Backup {list[2].FileName} deleted by chef", StringComparison.Ordinal));
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
        await using var client = admin;
        await using var other = await TestClient.ConnectAsync(server, "anna");
        await admin.SendAsync(new CreateBackup());
        var good = Entries(Path.Combine(server.DataDir, "backups", Assert.Single((await admin.WaitForAsync<BackupList>()).Backups).FileName));
        var backups = Path.Combine(server.DataDir, "backups");

        File.WriteAllBytes(Path.Combine(backups, "kaputt.ovsbackup"), [1, 2, 3, 4]);
        WriteArchive(Path.Combine(backups, "ohne-manifest.ovsbackup"), good.Where(pair => pair.Key != "manifest.json").ToDictionary());
        var future = DataOf(good["server-data.json"]);
        future.DataVersion = ServerData.CurrentVersion + 1;
        WriteArchive(Path.Combine(backups, "zu-neu.ovsbackup"), new()
        {
            ["manifest.json"] = Manifest(ServerData.CurrentVersion + 1),
            ["server-data.json"] = JsonSerializer.SerializeToUtf8Bytes(future, ProtocolJson.Options),
            ["cert.pfx"] = good["cert.pfx"],
        });
        WriteArchive(Path.Combine(backups, "ohne-zertifikat.ovsbackup"), good.Where(pair => pair.Key != "cert.pfx").ToDictionary());
        WriteArchive(Path.Combine(backups, "daten-kaputt.ovsbackup"), new(good) { ["server-data.json"] = "{nicht json"u8.ToArray() });

        var dataBefore = await File.ReadAllBytesAsync(Path.Combine(server.DataDir, DataStore.FileName));
        foreach (var name in new[] { "kaputt", "ohne-manifest", "zu-neu", "ohne-zertifikat", "daten-kaputt" })
        {
            await admin.SendAsync(new RestoreBackup(name + ".ovsbackup") { RequestId = name });
            Assert.Equal(ReasonCodes.InvalidBackup, (await admin.ErrorAsync(name)).Code);
        }
        await other.AssertNoMessageAsync<Disconnected>();
        Assert.Equal(dataBefore, await File.ReadAllBytesAsync(Path.Combine(server.DataDir, DataStore.FileName)));
        Assert.DoesNotContain(Directory.GetFiles(backups), text => Path.GetFileName(text).StartsWith("vor-wiederherstellung_", StringComparison.Ordinal));
        Assert.Contains(server.Log, line => line.Contains("Backup zu-neu.ovsbackup is invalid", StringComparison.Ordinal));

        await admin.SendAsync(new ListBackups { RequestId = "l" }); // broken ones still show, so they can be deleted
        Assert.Equal(6, (await admin.WaitForAsync<BackupList>(backupList => backupList.RequestId == "l")).Backups.Count);
    }

    [Fact]
    public async Task PathTraversal_AndMissingRight_Rejected()
    {
        var (server, admin) = await StartWithAdminAsync();
        await using var _ = server;
        await using var client = admin;
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
        int count = 0;
        foreach (var name in names)
        {
            foreach (Request request in new Request[] { new DeleteBackup(name), new RestoreBackup(name) })
            {
                var id = $"x{count++}";
                await admin.SendAsync(request with { RequestId = id });
                Assert.Equal(ReasonCodes.NotFound, (await admin.ErrorAsync(id)).Code);
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
            var id = $"g{count++}";
            await guest.SendAsync(request with { RequestId = id });
            Assert.Equal(ReasonCodes.PermissionDenied, (await guest.ErrorAsync(id)).Code);
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
    public async Task Download_InChunks_ByteIdentical()
    {
        var (server, admin) = await StartWithAdminAsync();
        await using var _ = server;
        await using var client = admin;
        var logo = new byte[1_500_000];
        Random.Shared.NextBytes(logo); // does not compress: the archive needs three chunks
        await File.WriteAllBytesAsync(Path.Combine(server.DataDir, ServerIconStore.FileName), logo);
        await admin.SendAsync(new CreateBackup());
        var info = Assert.Single((await admin.WaitForAsync<BackupList>()).Backups);
        var path = Path.Combine(server.DataDir, "backups", info.FileName);

        using var received = new MemoryStream();
        int chunks = 0;
        while (true)
        {
            var id = $"d{chunks}";
            await admin.SendAsync(new DownloadBackup(info.FileName, received.Length) { RequestId = id });
            var chunk = await admin.WaitForAsync<BackupChunk>(backupChunk => backupChunk.RequestId == id);
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
                await admin.WaitForAsync<ChatMessage>(chatMessage => chatMessage.Text == "während des Downloads");
            }
            Assert.Equal(received.Length == chunk.TotalSize, chunk.IsLast);
            if (chunk.IsLast) break;
        }
        Assert.Equal(3, chunks);
        Assert.Equal(System.Security.Cryptography.SHA256.HashData(await File.ReadAllBytesAsync(path)), System.Security.Cryptography.SHA256.HashData(received.ToArray()));

        await admin.SendAsync(new DownloadBackup(info.FileName, received.Length + 1) { RequestId = "weit" });
        Assert.Equal(ReasonCodes.InvalidValue, (await admin.ErrorAsync("weit")).Code);
        await admin.SendAsync(new DownloadBackup(info.FileName, -1) { RequestId = "minus" });
        Assert.Equal(ReasonCodes.InvalidValue, (await admin.ErrorAsync("minus")).Code);
        await admin.SendAsync(new DownloadBackup("../" + DataStore.FileName, 0) { RequestId = "raus" });
        Assert.Equal(ReasonCodes.NotFound, (await admin.ErrorAsync("raus")).Code);
    }

    /// <summary>Sends the bytes in chunks of the given size, each after the answer to the one before; returns the last answer.</summary>
    static async Task<Message> UploadAsync(TestClient client, byte[] bytes, int chunkSize = Chunk, string? uploadId = null)
    {
        uploadId ??= Guid.NewGuid().ToString("N");
        for (int offset = 0, count = 0; ; offset += chunkSize, count++)
        {
            var size = Math.Min(chunkSize, bytes.Length - offset);
            var last = offset + size >= bytes.Length;
            var id = $"u{count}";
            await client.SendAsync(new UploadBackupChunk(uploadId, offset, Convert.ToBase64String(bytes, offset, size), last) { RequestId = id });
            var answer = await client.WaitForAsync<Message>(message => message is UploadBackupAck { RequestId: var text } && text == id
                                                                 || message is BackupUploaded { RequestId: var value } && value == id
                                                                 || message is Error { RequestId: var errorId } && errorId == id);
            if (answer is UploadBackupAck ack) Assert.Equal(offset + size, ack.Received);
            if (last || answer is not UploadBackupAck) return answer;
        }
    }

    [Fact]
    public async Task Upload_ValidatedStored_Listed()
    {
        var (server, admin) = await StartWithAdminAsync();
        await using var _ = server;
        await using var client = admin;
        await admin.SendAsync(new CreateChannel("Hochgeladen", ""));
        await admin.WaitForAsync<ChannelAdded>();
        await admin.SendAsync(new CreateBackup());
        var original = Assert.Single((await admin.WaitForAsync<BackupList>()).Backups);
        var bytes = await File.ReadAllBytesAsync(Path.Combine(server.DataDir, "backups", original.FileName));
        await admin.SendAsync(new DeleteBackup(original.FileName) { RequestId = "weg" });
        Assert.Empty((await admin.WaitForAsync<BackupList>(backupList => backupList.RequestId == "weg")).Backups);

        var answer = Assert.IsType<BackupUploaded>(await UploadAsync(admin, bytes, chunkSize: 1000)); // several chunks
        Assert.EndsWith(".ovsbackup", answer.Backup.FileName, StringComparison.Ordinal);
        Assert.Equal(bytes.Length, answer.Backup.Size);
        Assert.Equal(original.CreatedAt, answer.Backup.CreatedAt);
        var list = await admin.WaitForAsync<BackupList>(backupList => backupList.RequestId == answer.RequestId);
        Assert.Equal(answer.Backup, Assert.Single(list.Backups));
        Assert.Equal(bytes, await File.ReadAllBytesAsync(Path.Combine(server.DataDir, "backups", answer.Backup.FileName)));
        Assert.Empty(UploadFiles(server.DataDir));
        Assert.Contains(server.Log, line => line.Contains($"Backup {answer.Backup.FileName} uploaded by chef", StringComparison.Ordinal));

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
        Assert.Equal(ReasonCodes.BackupTooLarge, Assert.IsType<Error>(tooLarge).Code);
        Assert.Empty(UploadFiles(server.DataDir));

        // a chunk bigger than 512 KB, a wrong offset, a bad id and bad base64
        var id = Guid.NewGuid().ToString("N");
        await admin.SendAsync(new UploadBackupChunk(id, 0, Convert.ToBase64String(new byte[Chunk + 1]), false) { RequestId = "gross" });
        Assert.Equal(ReasonCodes.InvalidValue, (await admin.ErrorAsync("gross")).Code);
        await admin.SendAsync(new UploadBackupChunk(id, 0, Convert.ToBase64String(new byte[10]), false) { RequestId = "ok" });
        Assert.Equal(10, (await admin.WaitForAsync<UploadBackupAck>(uploadBackupAck => uploadBackupAck.RequestId == "ok")).Received);
        await admin.SendAsync(new UploadBackupChunk(id, 5, Convert.ToBase64String(new byte[10]), false) { RequestId = "luecke" });
        Assert.Equal(ReasonCodes.InvalidValue, (await admin.ErrorAsync("luecke")).Code);
        Assert.Empty(UploadFiles(server.DataDir));
        foreach (var bad in new[] { "../../x", "..\\x", "abc", "", id.ToUpperInvariant(), id + "0" })
        {
            await admin.SendAsync(new UploadBackupChunk(bad, 0, Convert.ToBase64String(new byte[10]), false) { RequestId = "id" + bad });
            Assert.Equal(ReasonCodes.InvalidValue, (await admin.ErrorAsync("id" + bad)).Code);
        }
        await admin.SendAsync(new UploadBackupChunk(id, 0, "kein base64!", false) { RequestId = "b64" });
        Assert.Equal(ReasonCodes.InvalidValue, (await admin.ErrorAsync("b64")).Code);
        Assert.Empty(UploadFiles(server.DataDir));
        Assert.False(File.Exists(Path.Combine(server.DataDir, "x")));

        // the connection ends in the middle of an upload
        await admin.SendAsync(new UploadBackupChunk(id, 0, Convert.ToBase64String(new byte[10]), false) { RequestId = "halb" });
        await admin.WaitForAsync<UploadBackupAck>(uploadBackupAck => uploadBackupAck.RequestId == "halb");
        Assert.Single(UploadFiles(server.DataDir));
        await admin.DisposeAsync();
        // the server notices the closed connection on its own time: poll with a generous cap
        await AsyncWait.UntilAsync(() => UploadFiles(server.DataDir).Length == 0, TimeSpan.FromSeconds(30));
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
        await using var client = admin;
        await using var guest = await TestClient.ConnectAsync(server, "gast");
        await admin.SendAsync(new CreateBackup());
        var good = Entries(Path.Combine(server.DataDir, "backups", Assert.Single((await admin.WaitForAsync<BackupList>()).Backups).FileName));
        var scratch = Path.Combine(dir, "ohne-manifest.zip");
        WriteArchive(scratch, good.Where(pair => pair.Key != "manifest.json").ToDictionary());

        foreach (var bytes in new[] { new byte[] { 1, 2, 3, 4 }, await File.ReadAllBytesAsync(scratch) })
        {
            var answer = Assert.IsType<Error>(await UploadAsync(admin, bytes));
            Assert.Equal(ReasonCodes.InvalidBackup, answer.Code);
        }
        var name = Assert.Single(Directory.GetFiles(Path.Combine(server.DataDir, "backups"), "*.ovsbackup"));
        Assert.Empty(UploadFiles(server.DataDir));
        Assert.Contains(server.Log, line => line.Contains("Uploaded backup is invalid", StringComparison.Ordinal));

        // AC5: the right ServerConfig for both directions
        await guest.SendAsync(new DownloadBackup(Path.GetFileName(name), 0) { RequestId = "g1" });
        Assert.Equal(ReasonCodes.PermissionDenied, (await guest.ErrorAsync("g1")).Code);
        await guest.SendAsync(new UploadBackupChunk(Guid.NewGuid().ToString("N"), 0, Convert.ToBase64String(await File.ReadAllBytesAsync(name)), true) { RequestId = "g2" });
        Assert.Equal(ReasonCodes.PermissionDenied, (await guest.ErrorAsync("g2")).Code);
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
        catch (Exception exception) when (exception is SocketException or IOException)
        {
            return null;
        }
    }

    static async Task<TestClient> ConnectWhenUpAsync(int port, string nickname, ClientIdentity identity, Task host)
    {
        // Generous: a run ending and the next one starting can take long on a loaded CI runner; it returns once up.
        for (var deadline = DateTime.UtcNow.AddSeconds(30); DateTime.UtcNow < deadline && !host.IsCompleted;)
        {
            if (await TryConnectAsync(port, nickname, identity) is { } client) return client;
            await Task.Delay(100);
        }
        throw new TimeoutException($"{nickname} could not connect");
    }

    /// <summary>Starts ServerHost on the data dir; a port that Windows reserves makes the run end with 1, then another is tried.</summary>
    static async Task<RunningHost> StartHostAsync(string dataDir, ClientIdentity admin, Func<ServerState, Task>? afterWait = null)
    {
        for (int attempt = 0; ; attempt++)
        {
            int port = Random.Shared.Next(20_000, 45_000);
            var env = new Dictionary<string, string> { ["OVS_DATA_DIR"] = dataDir, ["OVS_PORT"] = port.ToString(CultureInfo.InvariantCulture) };
            var output = new ConcurrentQueue<string>();
#pragma warning disable CA2000 // handed to the Host, which cancels and disposes it
            var stop = new CancellationTokenSource();
#pragma warning restore CA2000
            var run = ServerHost.RunAsync(env.GetValueOrDefault, IPAddress.Loopback, TimeProvider.System, output.Enqueue, output.Enqueue, stop.Token, afterWait);
            try
            {
                var client = await ConnectWhenUpAsync(port, "chef", admin, run);
                return new(new Host(run, port, stop, output), client);
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
    public async Task Restore_ValidBackup_ReplacesStateKeepsSafetyBackupAndRestarts()
    {
        using var adminId = ClientIdentity.Create();
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
            using var annaId = ClientIdentity.Create();
            await using var anna = await ConnectWhenUpAsync(host.Port, "anna", annaId, host.Run);
            await admin2.SendAsync(new CreateChannel("Neu", ""));
            await admin2.WaitForAsync<ChannelAdded>();

            await admin2.SendAsync(new RestoreBackup(backup));
            Assert.Equal(ReasonCodes.Restoring, (await admin2.WaitForAsync<Disconnected>(timeoutMs: 30_000)).Reason);
            Assert.Equal(ReasonCodes.Restoring, (await anna.WaitForAsync<Disconnected>(timeoutMs: 30_000)).Reason);
            await admin2.DisposeAsync();

            await using var again = await ConnectWhenUpAsync(host.Port, "chef", adminId, host.Run);
            var channels = again.Welcome.Snapshot.Channels.Select(channel => channel.Name).ToList();
            Assert.Contains("Alt", channels);
            Assert.DoesNotContain("Neu", channels);
            Assert.Equal(backupCertHash, again.ServerCertHash);
            Assert.True(again.Welcome.Snapshot.Users.Single(user => user.Nickname == "chef").Permissions.Has(Permission.ServerConfig));

            await again.SendAsync(new ListBackups { RequestId = "l" });
            var list = (await again.WaitForAsync<BackupList>(backupList => backupList.RequestId == "l")).Backups;
            var safety = Assert.Single(list, backupInfo => backupInfo.FileName.StartsWith("vor-wiederherstellung_", StringComparison.Ordinal));
            Assert.Contains(list, backupInfo => backupInfo.FileName == backup);
            Assert.Contains(DataOf(Entries(Path.Combine(dir, "backups", safety.FileName))["server-data.json"]).Channels, channel => channel.Name == "Neu");
        }
        var log = string.Join("\n", Directory.GetFiles(Path.Combine(dir, "logs", "server")).Select(File.ReadAllText));
        Assert.Contains($"Backup {backup} being restored by chef", log, StringComparison.Ordinal);
        Assert.Contains("Backup applied", log, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Restore_ArrivingWhileShuttingDown_Applied()
    {
        using var adminId = ClientIdentity.Create();
        SeedAdmin(dir, adminId);
        var gap = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var sent = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        async Task AfterWaitAsync(ServerState _)
        {
            gap.SetResult();
            await sent.Task;
        }
        var (host, admin) = await StartHostAsync(dir, adminId, AfterWaitAsync);
        await using (host)
        await using (admin)
        {
            await admin.SendAsync(new CreateChannel("Alt", ""));
            await admin.WaitForAsync<ChannelAdded>();
            await admin.SendAsync(new CreateBackup());
            var backup = Assert.Single((await admin.WaitForAsync<BackupList>()).Backups).FileName;
            await admin.SendAsync(new CreateChannel("Neu", ""));
            await admin.WaitForAsync<ChannelAdded>(channelAdded => channelAdded.Channel.Name == "Neu");

            host.Stop(); // a plain shutdown: the wait ends, the sessions are still open
            await gap.Task.WaitAsync(TimeSpan.FromSeconds(30));
            await admin.SendAsync(new RestoreBackup(backup));
            Assert.Equal(ReasonCodes.Restoring, (await admin.WaitForAsync<Disconnected>(timeoutMs: 30_000)).Reason);
            sent.SetResult();
            Assert.Equal(0, await host.Run.WaitAsync(TimeSpan.FromSeconds(30)));
        }
        var channels = new DataStore(Path.Combine(dir, DataStore.FileName)).LoadOrCreate(() => throw new InvalidOperationException()).Channels;
        Assert.Contains(channels, channel => channel.Name == "Alt");
        Assert.DoesNotContain(channels, channel => channel.Name == "Neu");
        var log = string.Join("\n", Directory.GetFiles(Path.Combine(dir, "logs", "server")).Select(File.ReadAllText));
        Assert.Contains("Backup applied", log, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Restore_OlderDataVersion_Migrated()
    {
        using var adminId = ClientIdentity.Create();
        SeedAdmin(dir, adminId);
        var (host, admin) = await StartHostAsync(dir, adminId);
        await using (host)
        {
            // a backup from a version 1 server: guests could not chat yet
            var old = DataOf(await File.ReadAllBytesAsync(Path.Combine(dir, DataStore.FileName)));
            old.DataVersion = 1;
            int guest = old.Groups.FindIndex(groupRecord => groupRecord.Id == OVS.Server.Permissions.PermissionRules.GuestGroupId);
            old.Groups[guest] = old.Groups[guest] with { Permissions = Permission.Speak };
            old.Channels.Add(new ChannelRecord { Id = Guid.NewGuid(), Name = "Früher", Order = 1 });
            var backups = Directory.CreateDirectory(Path.Combine(dir, "backups")).FullName;
            WriteArchive(Path.Combine(backups, "alt.ovsbackup"), new()
            {
                ["manifest.json"] = Manifest(1),
                ["server-data.json"] = JsonSerializer.SerializeToUtf8Bytes(old, ProtocolJson.Options),
                ["cert.pfx"] = await File.ReadAllBytesAsync(Path.Combine(dir, "cert.pfx")),
            });

            await File.WriteAllBytesAsync(Path.Combine(dir, ServerIconStore.FileName), TestImages.Encode(64, 64)); // a logo the backup does not have
            await admin.SendAsync(new RestoreBackup("alt.ovsbackup"));
            Assert.Equal(ReasonCodes.Restoring, (await admin.WaitForAsync<Disconnected>(timeoutMs: 30_000)).Reason);
            await admin.DisposeAsync();

            await using var again = await ConnectWhenUpAsync(host.Port, "chef", adminId, host.Run);
            Assert.Contains(again.Welcome.Snapshot.Channels, channel => channel.Name == "Früher");
            var guestGroup = again.Welcome.Snapshot.Groups.Single(groupInfo => groupInfo.Id == OVS.Server.Permissions.PermissionRules.GuestGroupId);
            Assert.True(guestGroup.Permissions.Has(Permission.ChatChannel));
            Assert.False(File.Exists(Path.Combine(dir, ServerIconStore.FileName))); // the backup had no logo
        }
        Assert.Equal(ServerData.CurrentVersion, DataOf(await File.ReadAllBytesAsync(Path.Combine(dir, DataStore.FileName))).DataVersion);
    }

    /// <summary>Package 181: the server admin tokens the host's runs printed, in order.</summary>
    static List<string> ServerAdminTokens(Host host) =>
        host.Output.Where(line => line.Contains("Server admin token: ", StringComparison.Ordinal))
            .Select(line => line[(line.IndexOf("Server admin token: ", StringComparison.Ordinal) + "Server admin token: ".Length)..].Split(' ')[0]).ToList();

    /// <summary>Package 181 (AC6): the server admin restores without the Admin group; the next start has them again and no token.</summary>
    [Fact]
    public async Task Restore_ByServerAdmin_Allowed()
    {
        using var ownerId = ClientIdentity.Create();
        SeedAdmin(dir, ownerId, serverData => serverData.Users.Single(user => user.Fingerprint == ownerId.Fingerprint).GroupIds = [WellKnownGroups.ServerAdmin]);
        var (host, owner) = await StartHostAsync(dir, ownerId);
        await using (host)
        {
            await owner.SendAsync(new CreateBackup());
            var backup = Assert.Single((await owner.WaitForAsync<BackupList>()).Backups).FileName;
            await owner.SendAsync(new RestoreBackup(backup));
            Assert.Equal(ReasonCodes.Restoring, (await owner.WaitForAsync<Disconnected>(timeoutMs: 30_000)).Reason);
            await owner.DisposeAsync();

            await using var again = await ConnectWhenUpAsync(host.Port, "chef", ownerId, host.Run);
            Assert.True(again.Welcome.Snapshot.HasServerAdmin);
            Assert.Empty(ServerAdminTokens(host));
        }
    }

    /// <summary>Package 181 (AC8, A192): a backup from before the server admin restores, and the next start prints a new token.</summary>
    [Fact]
    public async Task Restore_WithoutServerAdmin_TokenAtNextStart()
    {
        using var adminId = ClientIdentity.Create();
        SeedAdmin(dir, adminId);
        var (host, admin) = await StartHostAsync(dir, adminId);
        await using (host)
        {
            await admin.SendAsync(new CreateBackup());
            var backup = Assert.Single((await admin.WaitForAsync<BackupList>()).Backups).FileName;
            await admin.SendAsync(new RedeemAdminToken(Assert.Single(ServerAdminTokens(host))));
            await admin.WaitForAsync<ServerAdminClaimed>();

            await admin.SendAsync(new RestoreBackup(backup));
            Assert.Equal(ReasonCodes.Restoring, (await admin.WaitForAsync<Disconnected>(timeoutMs: 30_000)).Reason);
            await admin.DisposeAsync();

            await using var again = await ConnectWhenUpAsync(host.Port, "chef", adminId, host.Run);
            Assert.False(again.Welcome.Snapshot.HasServerAdmin);
            Assert.Equal(2, ServerAdminTokens(host).Distinct().Count());
        }
    }

    // ---- Package 89 (A101): the right BackupsManage, upload and restore only for the Admin group, full validation ----

    static Action<ServerData> AddGroup(string name, Permission permissions) =>
        serverData => serverData.Groups.Add(new OVS.Server.Permissions.GroupRecord(Guid.NewGuid(), name, permissions));

    [Fact]
    public async Task BackupsManage_Required_ServerConfigAloneNotEnough()
    {
        var config = ClientIdentity.Create();
        var keeper = ClientIdentity.Create();
        await using var server = await TestServer.StartAsync(serverData =>
        {
            AddGroup("Konfig", Permission.ServerConfig)(serverData);
            AddGroup("Sicherung", Permission.BackupsManage)(serverData);
            TestServer.Grant(config, "Konfig")(serverData);
            TestServer.Grant(keeper, "Sicherung")(serverData);
        });
        await using var client = await TestClient.ConnectAsync(server, "konfig", config);
        await using var testClient = await TestClient.ConnectAsync(server, "sicherung", keeper);

        await testClient.SendAsync(new CreateBackup { RequestId = "c" });
        var name = Assert.Single((await testClient.WaitForAsync<BackupList>(backupList => backupList.RequestId == "c")).Backups).FileName;
        Assert.Contains(server.Log, line => line.Contains($"Backup {name} created by sicherung", StringComparison.Ordinal));
        int count = 0;
        foreach (Request request in new Request[] { new ListBackups(), new CreateBackup(), new DownloadBackup(name, 0), new DeleteBackup(name) })
        {
            var id = $"c{count++}";
            await client.SendAsync(request with { RequestId = id });
            Assert.Equal(ReasonCodes.PermissionDenied, (await client.ErrorAsync(id)).Code);
        }

        await testClient.SendAsync(new ListBackups { RequestId = "l" });
        Assert.Single((await testClient.WaitForAsync<BackupList>(backupList => backupList.RequestId == "l")).Backups);
        await testClient.SendAsync(new DownloadBackup(name, 0) { RequestId = "d" });
        Assert.True((await testClient.WaitForAsync<BackupChunk>(ch => ch.RequestId == "d")).IsLast);
        // uploading and restoring are not part of the right
        await testClient.SendAsync(new RestoreBackup(name) { RequestId = "r" });
        Assert.Equal(ReasonCodes.PermissionDenied, (await testClient.ErrorAsync("r")).Code);
        var bytes = await File.ReadAllBytesAsync(Path.Combine(server.DataDir, "backups", name));
        Assert.Equal(ReasonCodes.PermissionDenied, Assert.IsType<Error>(await UploadAsync(testClient, bytes)).Code);
        Assert.Empty(UploadFiles(server.DataDir));
        await testClient.SendAsync(new DeleteBackup(name) { RequestId = "x" });
        Assert.Empty((await testClient.WaitForAsync<BackupList>(backupList => backupList.RequestId == "x")).Backups);
        await testClient.AssertNoMessageAsync<Disconnected>();
    }

    [Fact]
    public async Task UploadAndRestore_OnlyAdminGroup_CheckedPerChunk()
    {
        var second = ClientIdentity.Create();
        var almighty = ClientIdentity.Create();
        var (server, admin) = await StartWithAdminAsync(seed: serverData =>
        {
            TestServer.Grant(second, "Admin")(serverData);
            AddGroup("Alles", Permission.All)(serverData);
            TestServer.Grant(almighty, "Alles")(serverData);
        });
        await using var _ = server;
        await using var client = admin;
        await using var two = await TestClient.ConnectAsync(server, "zweiter", second);
        await using var all = await TestClient.ConnectAsync(server, "allmacht", almighty);
        await admin.SendAsync(new CreateBackup());
        var name = Assert.Single((await admin.WaitForAsync<BackupList>()).Backups).FileName;
        var backups = Path.Combine(server.DataDir, "backups");
        var bytes = await File.ReadAllBytesAsync(Path.Combine(backups, name));

        // every right, but not in the Admin group
        Assert.Equal(ReasonCodes.PermissionDenied, Assert.IsType<Error>(await UploadAsync(all, bytes)).Code);
        await all.SendAsync(new RestoreBackup(name) { RequestId = "r" });
        Assert.Equal(ReasonCodes.PermissionDenied, (await all.ErrorAsync("r")).Code);
        Assert.Empty(UploadFiles(server.DataDir));
        await all.SendAsync(new ListBackups { RequestId = "l" });
        Assert.Single((await all.WaitForAsync<BackupList>(backupList => backupList.RequestId == "l")).Backups);

        // checked on every chunk: out of the Admin group in the middle of an upload, the next chunk is refused and the file goes
        var id = Guid.NewGuid().ToString("N");
        await two.SendAsync(new UploadBackupChunk(id, 0, Convert.ToBase64String(bytes, 0, 100), false) { RequestId = "c0" });
        await two.WaitForAsync<UploadBackupAck>(uploadBackupAck => uploadBackupAck.RequestId == "c0");
        Assert.Single(UploadFiles(server.DataDir));
        LeaveAdminGroup(server.State, second.Fingerprint);
        await two.WaitForAsync<UserUpdated>(userUpdated => userUpdated.User.Nickname == "zweiter" && !userUpdated.User.GroupIds.Contains(WellKnownGroups.Admin));
        await two.SendAsync(new UploadBackupChunk(id, 100, Convert.ToBase64String(bytes, 100, bytes.Length - 100), true) { RequestId = "c1" });
        Assert.Equal(ReasonCodes.PermissionDenied, (await two.ErrorAsync("c1")).Code);
        Assert.Empty(UploadFiles(server.DataDir));
        Assert.Single(Directory.GetFiles(backups, "*.ovsbackup"));

        // a member of the Admin group can
        Assert.IsType<BackupUploaded>(await UploadAsync(admin, bytes));
    }

    /// <summary>
    /// Package 84 (A102): nobody outranks a member of the Admin group, so no request can take it away from another admin.
    /// The test changes the stored groups itself, under the server's lock, and lets the server apply them as after a group change.
    /// </summary>
    static void LeaveAdminGroup(ServerState state, string fingerprint) =>
        StateHarness.ChangeStoredData(state, serverData => serverData.Users.Single(user => user.Fingerprint == fingerprint).GroupIds.Remove(WellKnownGroups.Admin));

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
    [InlineData("separator-default")]
    [InlineData("separator-linked")]
    [InlineData("separator-named")]
    [InlineData("separator-with-options")]
    public async Task Restore_InvalidContent_RejectedServerStillStarts(string variant)
    {
        var (server, admin) = await StartWithAdminAsync(); // disposed through RestartAsync at the end
        await admin.SendAsync(new CreateBackup());
        var backups = Path.Combine(server.DataDir, "backups");
        var good = Entries(Path.Combine(backups, Assert.Single((await admin.WaitForAsync<BackupList>()).Backups).FileName));
        var data = DataOf(good["server-data.json"]);
        BrokenServerData.Break(data, variant); // content that passed the old checks but broke every later start
        var archive = Path.Combine(dir, variant + ".ovsbackup");
        WriteArchive(archive, new(good) { ["server-data.json"] = JsonSerializer.SerializeToUtf8Bytes(data, ProtocolJson.Options) });

        // uploaded: refused with the same check, no file left
        Assert.Equal(ReasonCodes.InvalidBackup, Assert.IsType<Error>(await UploadAsync(admin, await File.ReadAllBytesAsync(archive))).Code);
        Assert.Empty(UploadFiles(server.DataDir));
        Assert.Contains(server.Log, line => line.Contains("Uploaded backup is invalid", StringComparison.Ordinal));

        // put on the server by hand: the restore is refused and nothing is applied
        File.Copy(archive, Path.Combine(backups, "kaputt.ovsbackup"));
        var dataBefore = await File.ReadAllBytesAsync(Path.Combine(server.DataDir, DataStore.FileName));
        await admin.SendAsync(new RestoreBackup("kaputt.ovsbackup") { RequestId = "r" });
        Assert.Equal(ReasonCodes.InvalidBackup, (await admin.ErrorAsync("r")).Code);
        await admin.AssertNoMessageAsync<Disconnected>();
        Assert.Equal(dataBefore, await File.ReadAllBytesAsync(Path.Combine(server.DataDir, DataStore.FileName)));
        Assert.DoesNotContain(Directory.GetFiles(backups), text => Path.GetFileName(text).StartsWith("vor-wiederherstellung_", StringComparison.Ordinal));
        await admin.DisposeAsync();

        // and the server still starts
        await using var restarted = await server.RestartAsync();
        await using var anna = await TestClient.ConnectAsync(restarted, "anna");
        Assert.Contains(anna.Welcome.Snapshot.Channels, channel => channel.Name == "Lobby");
    }

    [Fact]
    public async Task ManifestCap_Quota_ListingCached()
    {
        var time = new ManualTimeProvider(); // Package 86: one CreateBackup per 10 s and session
        var (server, admin) = await StartWithAdminAsync(time);
        await using var _ = server;
        await using var client = admin;
        await admin.SendAsync(new CreateBackup());
        var first = Assert.Single((await admin.WaitForAsync<BackupList>()).Backups).FileName;
        var backups = Path.Combine(server.DataDir, "backups");
        var bytes = await File.ReadAllBytesAsync(Path.Combine(backups, first));
        var good = Entries(Path.Combine(backups, first));

        // a manifest over 16 KB is never read: listed without a version, refused for a restore
        var big = JsonSerializer.SerializeToUtf8Bytes(new
        {
            formatVersion = 1, dataVersion = ServerData.CurrentVersion, serverVersion = new string('x', 17 * 1024), createdAt = DateTimeOffset.UtcNow,
        });
        WriteArchive(Path.Combine(backups, "gross.ovsbackup"), new(good) { ["manifest.json"] = big });
        await admin.SendAsync(new ListBackups { RequestId = "l" });
        Assert.Equal("?", (await admin.WaitForAsync<BackupList>(backupList => backupList.RequestId == "l")).Backups.Single(backup => backup.FileName == "gross.ovsbackup").ServerVersion);
        await admin.SendAsync(new RestoreBackup("gross.ovsbackup") { RequestId = "r" });
        Assert.Equal(ReasonCodes.InvalidBackup, (await admin.ErrorAsync("r")).Code);

        // at most 50 backups: creating and uploading beyond that are refused
        for (int index = 0; index < 48; index++) File.WriteAllBytes(Path.Combine(backups, $"alt-{index:00}.ovsbackup"), [1]);
        time.Advance(TimeSpan.FromMinutes(1));
        await admin.SendAsync(new CreateBackup { RequestId = "q" });
        Assert.Equal(ReasonCodes.BackupQuotaExceeded, (await admin.ErrorAsync("q")).Code);
        Assert.Equal(ReasonCodes.BackupQuotaExceeded, Assert.IsType<Error>(await UploadAsync(admin, bytes)).Code);
        Assert.Empty(UploadFiles(server.DataDir));
        Assert.Equal(50, Directory.GetFiles(backups, "*.ovsbackup").Length);
        Assert.Contains(server.Log, line => line.Contains("backup limit reached", StringComparison.Ordinal));
        await admin.SendAsync(new DeleteBackup("alt-00.ovsbackup") { RequestId = "d" });
        Assert.Equal(49, (await admin.WaitForAsync<BackupList>(backupList => backupList.RequestId == "d")).Total);
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

        // the listing re-reads only archives whose size or time changed, and sees every change at once, even two
        // within the folder's timestamp resolution (a CI runner once listed a file that was already gone)
        var listed = store.List();
        Assert.Equal(listed, store.List());
        var path = Path.Combine(store.Folder, listed[0].FileName);
        var changed = Path.Combine(dir, "geaendert.zip");
        WriteArchive(changed, new(Entries(path))
        {
            ["manifest.json"] = JsonSerializer.SerializeToUtf8Bytes(new { formatVersion = 1, dataVersion = 1, serverVersion = "geaendert", createdAt = listed[0].CreatedAt }),
        });
        await File.WriteAllBytesAsync(path, await File.ReadAllBytesAsync(changed));
        Assert.Equal("geaendert", store.List().Single(backup => backup.FileName == listed[0].FileName).ServerVersion);
        store.Delete(Path.Combine(store.Folder, listed[1].FileName));
        File.Delete(path); // right after the store's own change, from outside
        Assert.Empty(store.List());
    }

    [Fact]
    public async Task Create_Failing_LeavesNoTmp()
    {
        await File.WriteAllTextAsync(Path.Combine(dir, DataStore.FileName), "{}");
        var store = new BackupStore(dir, new ManualTimeProvider());
        Assert.Throws<FileNotFoundException>(() => store.Create()); // no cert.pfx: writing the archive fails
        Assert.Empty(Directory.GetFiles(store.Folder));
    }

    /// <summary>What a crash or an older version left behind in the backup folder goes at the next start.</summary>
    [Fact]
    public async Task Start_LeftoversInTheBackupFolder_Removed()
    {
        var store = new BackupStore(dir, new ManualTimeProvider());
        Directory.CreateDirectory(store.Folder);
        await File.WriteAllTextAsync(Path.Combine(store.Folder, "2026-01-01_12-00-00.ovsbackup.tmp"), "x");
        await File.WriteAllTextAsync(Path.Combine(store.Folder, UploadFolder.Prefix + new string('a', 32)), "x");
        await File.WriteAllTextAsync(Path.Combine(store.Folder, "behalten.ovsbackup"), "x");
        await using var server = await TestServer.StartAsync(dataDir: dir);
        Assert.Equal(["behalten.ovsbackup"], Directory.GetFiles(store.Folder).Select(Path.GetFileName));
    }

    sealed class Host(Task<int> run, int port, CancellationTokenSource stop, ConcurrentQueue<string> output) : IAsyncDisposable
    {
        public Task<int> Run => run;
        /// <summary>Package 181: the console and log lines of every run, the server admin tokens included.</summary>
        public IReadOnlyCollection<string> Output => output;
        public int Port => port;
        public void Stop() => stop.Cancel();

        public async ValueTask DisposeAsync()
        {
            await stop.CancelAsync();
            Assert.Equal(0, await run.WaitAsync(TimeSpan.FromSeconds(30)));
            stop.Dispose();
        }
    }

    sealed record RunningHost(Host Host, TestClient Admin);
}
