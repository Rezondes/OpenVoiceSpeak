using OVS.Server;
using OVS.Server.Data;
using OVS.Server.Logging;
using OVS.Server.Permissions;
using OVS.Shared.Permissions;
using OVS.Tests.TestSupport;

namespace OVS.Tests.Server;

public sealed class DataStoreTests : IDisposable
{
    readonly string dir = Directory.CreateTempSubdirectory("ovs-data-").FullName;
    string FilePath => Path.Combine(dir, DataStore.FileName);
    ServerConfig Config => new(7000, dir, 50, "Mein Server", "pw");

    public void Dispose() => Directory.Delete(dir, true);

    [Fact]
    public void Load_MissingFile_CreatesDefaults()
    {
        var data = new DataStore(FilePath).LoadOrCreate(() => ServerData.CreateDefault(Config));

        var lobby = Assert.Single(data.Channels);
        Assert.Equal("Lobby", lobby.Name);
        Assert.Equal(lobby.Id, data.DefaultChannelId);
        Assert.Equal(["Gast", "Moderator", "Admin"], data.Groups.Select(g => g.Name));
        Assert.Equal("Mein Server", data.Settings.Name);
        Assert.True(data.Settings.CheckPassword("pw"));
        Assert.False(data.Settings.CheckPassword("nein"));
        Assert.True(File.Exists(FilePath));
    }

    [Fact]
    public void Save_ThenLoad_RoundTrips_NoTempLeft()
    {
        var store = new DataStore(FilePath);
        var data = store.LoadOrCreate(() => ServerData.CreateDefault(Config));
        data.Channels.Add(new ChannelRecord { Id = Guid.NewGuid(), Name = "Raid", Order = 1 });
        data.Links.Add(ChannelLink.Of(data.Channels[0].Id, data.Channels[1].Id));
        data.Users.Add(new UserRecord { Fingerprint = "ab", LastNickname = "x", GroupIds = [data.Groups[0].Id], FirstSeen = DateTimeOffset.UnixEpoch });
        store.Save(data);

        var loaded = new DataStore(FilePath).LoadOrCreate(() => throw new InvalidOperationException());
        Assert.Equal(["Lobby", "Raid"], loaded.Channels.Select(c => c.Name));
        Assert.Equal(data.Links, loaded.Links);
        Assert.Equal(data.Groups, loaded.Groups);
        Assert.Equal("ab", Assert.Single(loaded.Users).Fingerprint);
        Assert.False(File.Exists(FilePath + ".tmp"));
    }

    /// <summary>Package 111: a file from before separators loads every channel as a voice channel; a separator round-trips.</summary>
    [Fact]
    public void OldFile_ChannelsLoadAsVoice_SeparatorRoundTrips()
    {
        var store = new DataStore(FilePath);
        var data = store.LoadOrCreate(() => ServerData.CreateDefault(Config));
        data.Channels.Add(new ChannelRecord { Id = Guid.NewGuid(), Order = 1, Kind = OVS.Shared.Protocol.ChannelKind.Separator });
        store.Save(data);
        Assert.Equal([OVS.Shared.Protocol.ChannelKind.Voice, OVS.Shared.Protocol.ChannelKind.Separator],
            new DataStore(FilePath).LoadOrCreate(() => throw new InvalidOperationException()).Channels.Select(c => c.Kind));

        var json = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(FilePath))!;
        var channels = json.AsObject().First(p => p.Key.Equals("channels", StringComparison.OrdinalIgnoreCase)).Value!.AsArray();
        channels.RemoveAt(1);
        foreach (var channel in channels) channel!.AsObject().Remove(channel.AsObject().First(p => p.Key.Equals("kind", StringComparison.OrdinalIgnoreCase)).Key);
        File.WriteAllText(FilePath, json.ToJsonString());
        Assert.Equal(OVS.Shared.Protocol.ChannelKind.Voice, Assert.Single(new DataStore(FilePath).LoadOrCreate(() => throw new InvalidOperationException()).Channels).Kind);
    }

    [Fact]
    public void Load_CorruptFile_ThrowsAndLeavesFileUntouched()
    {
        File.WriteAllText(FilePath, "{ kaputt");
        var before = File.ReadAllBytes(FilePath);

        Assert.Throws<InvalidDataException>(() => new DataStore(FilePath).LoadOrCreate(() => ServerData.CreateDefault(Config)));
        Assert.Equal(before, File.ReadAllBytes(FilePath));
    }
    /// <summary>A file from before Package 31: guests get the chat rights once, a later removal sticks.</summary>
    [Fact]
    public async Task LoadVersion1_GuestGetsChatRights_Once()
    {
        var serverDir = Directory.CreateDirectory(Path.Combine(dir, "server")).FullName; // the test server deletes its folder
        var store = new DataStore(Path.Combine(serverDir, DataStore.FileName));
        var old = ServerData.CreateDefault(Config);
        old.DataVersion = 1;
        int guest = old.Groups.FindIndex(g => g.Id == PermissionRules.GuestGroupId);
        old.Groups[guest] = old.Groups[guest] with { Permissions = Permission.Speak };
        store.Save(old);

        ServerData migrated;
        await using (var server = await TestServer.StartAsync(dataDir: serverDir))
        {
            await using var client = await TestClient.ConnectAsync(server, "gast", password: "pw");
            Assert.Equal(PermissionRules.GuestPermissions, client.Welcome.Snapshot.Users.Single(u => u.SessionId == client.Id).Permissions);
            migrated = store.LoadOrCreate(() => throw new InvalidOperationException()); // saved on startup
        }
        Assert.Equal(ServerData.CurrentVersion, migrated.DataVersion);
        migrated.Groups[guest] = migrated.Groups[guest] with { Permissions = Permission.Speak }; // the admin takes it back
        Assert.False(migrated.Migrate(Config));
    }

    /// <summary>Package 69: the settings that were environment variables are taken over once, later values are ignored.</summary>
    [Fact]
    public void V2_Migrates_SettingsFromConfig()
    {
        var store = new DataStore(FilePath);
        var old = ServerData.CreateDefault(Config);
        old.DataVersion = 2;
        old.Settings = new ServerSettings { Name = "Alt" }; // a version 2 file has none of the new fields
        store.Save(old);

        var config = Config with { MaxUsers = 20, LogDays = 7, AutoRestartAt = new TimeOnly(3, 30), LogRotateDaily = false };
        var logs = new ServerLogs(dir, 0, TimeProvider.System, _ => { });
        _ = new ServerState(config, TimeProvider.System, logs);

        var migrated = store.LoadOrCreate(() => throw new InvalidOperationException());
        Assert.Equal(ServerData.CurrentVersion, migrated.DataVersion);
        Assert.Equal("Alt", migrated.Settings.Name);
        Assert.Equal(20, migrated.Settings.MaxUsers);
        Assert.Equal(7, migrated.Settings.LogDays);
        Assert.False(migrated.Settings.LogRotateDaily);
        Assert.True(migrated.Settings.AutoRestart);
        Assert.Equal(new TimeOnly(3, 30), migrated.Settings.AutoRestartTime);

        _ = new ServerState(config with { MaxUsers = 99, AutoRestartAt = null }, TimeProvider.System, logs);
        var second = store.LoadOrCreate(() => throw new InvalidOperationException());
        Assert.Equal(20, second.Settings.MaxUsers);
        Assert.True(second.Settings.AutoRestart);
    }

    /// <summary>Package 70: users saved before the statistics keep FirstSeen, the new values start empty.</summary>
    [Fact]
    public void OldUsers_KeepFirstSeen_NewFieldsEmpty()
    {
        var store = new DataStore(FilePath);
        var data = ServerData.CreateDefault(Config);
        var firstSeen = new DateTimeOffset(2025, 5, 1, 8, 0, 0, TimeSpan.Zero);
        data.Users.Add(new UserRecord { Fingerprint = "ab", LastNickname = "alt", GroupIds = [PermissionRules.GuestGroupId], FirstSeen = firstSeen });
        store.Save(data);
        // the user exactly as a file before Package 70 has it
        var json = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(FilePath))!;
        var user = json["users"]![0]!.AsObject();
        foreach (var key in user.Select(p => p.Key).Except(["fingerprint", "lastNickname", "groupIds", "firstSeen"]).ToList()) user.Remove(key);
        File.WriteAllText(FilePath, json.ToJsonString());

        var loaded = store.LoadOrCreate(() => throw new InvalidOperationException());
        Assert.False(loaded.Migrate(Config)); // no new data version needed
        var old = Assert.Single(loaded.Users);
        Assert.Equal(firstSeen, old.FirstSeen);
        Assert.Null(old.LastLogin);
        Assert.Null(old.LastIp);
        Assert.Equal(0, old.LoginCount);
        Assert.Equal(0, old.ChatMessages);
        Assert.Equal(TimeSpan.Zero, old.OnlineTime);
        Assert.Equal(TimeSpan.Zero, old.SpeechTime);
        Assert.Empty(old.PreviousNicknames);
    }

    /// <summary>Package 80: bans saved before the history keep working; the new values start empty ("unknown" in the client).</summary>
    [Fact]
    public void OldBans_LoadWithUnknownNewFields()
    {
        var store = new DataStore(FilePath);
        var data = ServerData.CreateDefault(Config);
        var until = new DateTimeOffset(2030, 1, 1, 0, 0, 0, TimeSpan.Zero);
        data.Bans.Add(new BanRecord { Id = Guid.NewGuid(), Fingerprint = "ab", Nickname = "troll", Ip = "10.0.0.1", Reason = "spam", CreatedBy = "mod", ExpiresAt = until });
        store.Save(data);
        // the ban exactly as a file before Package 80 has it
        var json = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(FilePath))!;
        var ban = json["bans"]![0]!.AsObject();
        foreach (var key in ban.Select(p => p.Key).Except(["id", "fingerprint", "nickname", "ip", "reason", "createdBy", "expiresAt"]).ToList()) ban.Remove(key);
        File.WriteAllText(FilePath, json.ToJsonString());

        var loaded = store.LoadOrCreate(() => throw new InvalidOperationException());
        Assert.False(loaded.Migrate(Config)); // no new data version needed
        var old = Assert.Single(loaded.Bans);
        Assert.Equal(("troll", "10.0.0.1", "spam", "mod", (DateTimeOffset?)until), (old.Nickname, old.Ip, old.Reason, old.CreatedBy, old.ExpiresAt));
        Assert.Equal(((DateTimeOffset?)null, (string?)null, (int?)null), (old.CreatedAt, old.CreatedByFingerprint, old.DurationMinutes));
        Assert.Equal((null, null, 0, null, null), (old.LiftedAt, old.LiftedBy, old.BlockedAttempts, old.LastAttempt, old.LastAttemptIp));
        Assert.True(old.IsActive(until.AddMinutes(-1)));
        Assert.False(old.IsActive(until));
    }

    /// <summary>Package 76 (AC6): an update to data version 4 keeps every possibility by granting the new view rights once.</summary>
    [Fact]
    public void Migration_GrantsViewRightsFromOldRights()
    {
        var store = new DataStore(FilePath);
        var data = ServerData.CreateDefault(Config);
        data.DataVersion = 3;
        data.Groups.Clear();
        data.Groups.Add(new Group(Guid.NewGuid(), "Verwalter", Permission.GroupsManage | Permission.Speak));
        data.Groups.Add(new Group(Guid.NewGuid(), "Zuweiser", Permission.GroupsAssign));
        data.Groups.Add(new Group(Guid.NewGuid(), "Banner", Permission.UserBan | Permission.UserKick));
        data.Groups.Add(new Group(Guid.NewGuid(), "Normal", Permission.Speak | Permission.ChatChannel));
        data.Groups.Add(new Group(Guid.NewGuid(), "Alles", Permission.None));
        store.Save(data);
        // as a version 3 file stores them: text flags, "All" for every right of that time
        var json = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(FilePath))!;
        Assert.Equal("Speak, GroupsManage", json["groups"]![0]!["permissions"]!.GetValue<string>());
        json["groups"]![4]!["permissions"] = "All";
        File.WriteAllText(FilePath, json.ToJsonString());

        var loaded = store.LoadOrCreate(() => throw new InvalidOperationException());
        Assert.True(loaded.Migrate(Config));
        Assert.Equal(ServerData.CurrentVersion, loaded.DataVersion);
        var perms = loaded.Groups.ToDictionary(g => g.Name, g => g.Permissions);
        Assert.Equal(Permission.GroupsManage | Permission.Speak | Permission.GroupsView | Permission.GroupsCreate | Permission.GroupsDelete, perms["Verwalter"]);
        Assert.Equal(Permission.GroupsAssign | Permission.UsersView, perms["Zuweiser"]);
        Assert.Equal(Permission.UserBan | Permission.UserKick | Permission.BansView, perms["Banner"]);
        Assert.Equal(Permission.Speak | Permission.ChatChannel, perms["Normal"]);
        Assert.Equal(Permission.All & ~Permission.ChannelPasswordBypass, perms["Alles"]); // Package 94: only Admin gets the newest right
        Assert.All(loaded.Groups.Where(g => g.Name != "Alles"), g => Assert.Equal(Permission.None, g.Permissions & (Permission.UserDelete | Permission.LogsView | Permission.LogsDownload)));
        Assert.False(loaded.Migrate(Config)); // once
    }

    /// <summary>Package 89 (A101): an update to data version 5 gives BackupsManage once to every group that has ServerConfig.</summary>
    [Fact]
    public void Migration_ServerConfigGroupsGetBackupsManage()
    {
        var store = new DataStore(FilePath);
        var data = ServerData.CreateDefault(Config);
        data.DataVersion = 4;
        data.Groups.Add(new Group(Guid.NewGuid(), "Konfig", Permission.ServerConfig | Permission.Speak));
        data.Groups.Add(new Group(Guid.NewGuid(), "Normal", Permission.Speak | Permission.LogsView));
        data.Groups.Add(new Group(Guid.NewGuid(), "Alles", Permission.None));
        store.Save(data);
        // "All" as a version 4 file stores it for every right of that time
        var json = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(FilePath))!;
        json["groups"]!.AsArray().Single(g => g!["name"]!.GetValue<string>() == "Alles")!["permissions"] = "All";
        File.WriteAllText(FilePath, json.ToJsonString());

        var loaded = store.LoadOrCreate(() => throw new InvalidOperationException());
        Assert.True(loaded.Migrate(Config));
        Assert.Equal(ServerData.CurrentVersion, loaded.DataVersion);
        var perms = loaded.Groups.ToDictionary(g => g.Name, g => g.Permissions);
        Assert.Equal(Permission.ServerConfig | Permission.Speak | Permission.BackupsManage, perms["Konfig"]);
        Assert.Equal(Permission.Speak | Permission.LogsView, perms["Normal"]);
        Assert.Equal(Permission.All & ~Permission.ChannelPasswordBypass, perms["Alles"]); // Package 94: not the newer right
        Assert.True(perms["Admin"].Has(Permission.BackupsManage));
        Assert.False(perms["Moderator"].Has(Permission.BackupsManage));
        Assert.False(perms["Gast"].Has(Permission.BackupsManage));
        Assert.False(loaded.Migrate(Config)); // once

        // new servers: only Admin
        Assert.Equal(["Admin"], ServerData.CreateDefault(Config).Groups.Where(g => g.Permissions.Has(Permission.BackupsManage)).Select(g => g.Name));
    }

    /// <summary>
    /// Package 94: ChannelPasswordBypass is for Admin only. A group stored as "All" reads as every right, the new one
    /// included, so the update to data version 6 takes it away from every other group once.
    /// </summary>
    [Fact]
    public void Migration_OnlyAdminGetsChannelPasswordBypass()
    {
        var store = new DataStore(FilePath);
        var data = ServerData.CreateDefault(Config);
        data.DataVersion = 5;
        data.Groups.Add(new Group(Guid.NewGuid(), "Alles", Permission.None));
        store.Save(data);
        var json = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(FilePath))!;
        json["groups"]!.AsArray().Single(g => g!["name"]!.GetValue<string>() == "Alles")!["permissions"] = "All";
        File.WriteAllText(FilePath, json.ToJsonString());

        var loaded = store.LoadOrCreate(() => throw new InvalidOperationException());
        Assert.True(loaded.Migrate(Config));
        Assert.Equal(6, loaded.DataVersion);
        var perms = loaded.Groups.ToDictionary(g => g.Name, g => g.Permissions);
        Assert.Equal(Permission.All & ~Permission.ChannelPasswordBypass, perms["Alles"]);
        Assert.Equal(Permission.All, perms["Admin"]);
        Assert.False(loaded.Migrate(Config)); // once
        Assert.Equal(["Admin"], ServerData.CreateDefault(Config).Groups.Where(g => g.Permissions.Has(Permission.ChannelPasswordBypass)).Select(g => g.Name));
    }

    [Fact]
    public void NewServer_StartsWithConfigValues()
    {
        var config = Config with { MaxUsers = 12, LogDays = 0, AutoRestartAt = new TimeOnly(5, 0) };
        var data = ServerData.CreateDefault(config);
        Assert.Equal(ServerData.CurrentVersion, data.DataVersion);
        Assert.Equal((12, 0, true, true, new TimeOnly(5, 0)),
            (data.Settings.MaxUsers, data.Settings.LogDays, data.Settings.LogRotateDaily, data.Settings.AutoRestart, data.Settings.AutoRestartTime));
    }

    /// <summary>Package 91: PBKDF2-SHA256 with a random salt; the same password twice gives two different values that both verify.</summary>
    [Fact]
    public void PasswordHash_Pbkdf2_SaltedUnique()
    {
        var first = ServerSettings.Hash("geheim");
        var second = ServerSettings.Hash("geheim");
        Assert.NotNull(first);
        Assert.NotEqual(first, second);
        foreach (var hash in new[] { first!, second! })
        {
            var parts = hash.Split('$');
            Assert.Equal(4, parts.Length);
            Assert.Equal("pbkdf2", parts[0]);
            Assert.True(int.Parse(parts[1]) >= 100_000);
            Assert.Equal(16, Convert.FromBase64String(parts[2]).Length);
            Assert.Equal(32, Convert.FromBase64String(parts[3]).Length);
            var settings = new ServerSettings { PasswordHash = hash };
            Assert.True(settings.CheckPassword("geheim"));
            Assert.False(settings.CheckPassword("Geheim"));
            Assert.False(settings.CheckPassword(null));
            Assert.True(ServerSettings.IsValidHash(hash)); // a backup holding it passes the restore check
        }
        Assert.Null(ServerSettings.Hash(""));

        // the old unsalted SHA-256 hex still verifies and is still valid in a backup, garbage is neither
        var legacy = new ServerSettings { PasswordHash = Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData("alt"u8)) };
        Assert.True(legacy.CheckPassword("alt"));
        Assert.False(legacy.CheckPassword("neu"));
        Assert.True(ServerSettings.IsValidHash(legacy.PasswordHash));
        foreach (var bad in new[] { "pbkdf2$1$AAAA$AAAA", "pbkdf2$100000$x$y", "md5$1$2$3", "abc", "" })
        {
            Assert.False(ServerSettings.IsValidHash(bad));
            Assert.False(new ServerSettings { PasswordHash = bad }.CheckPassword("alt"));
        }
    }
}
