using System.Security.Cryptography.X509Certificates;
using System.Text.Json;
using OVS.Server.Data;
using OVS.Server.Data.Records;
using OVS.Server.Permissions;
using OVS.Server.Security;
using OVS.Shared.Permissions;
using OVS.Shared.Protocol.Framing;
using OVS.Shared.Protocol.Messages.Channels;
using OVS.Tests.TestSupport;

namespace OVS.Tests.Server.Data;

/// <summary>The server's persisted files of earlier versions load with every stored value as it was written.</summary>
public sealed class PersistedFixtureTests() : TempDirectoryTest("ovs-fixture-", quiet: false)
{
    const string BackupName = "2026-10-06_12-00-00.ovsbackup";
    const string BuildVersion = "dev.0qhb"; // the version the fixture builds reported
    static readonly DateTimeOffset Date = PersistedFixtures.Date;

    [Theory]
    [MemberData(nameof(PersistedFixtures.Versions), MemberType = typeof(PersistedFixtures))]
    public void DataStore_FixtureOfVersion_LoadsEveryStoredValue(string version)
    {
        var path = PersistedFixtures.CopyTo(version, DataStore.FileName, dir);
        var text = File.ReadAllText(path);
        Assert.Contains("\"limits\": {", text); // computed keys the file carries and the loader must ignore
        Assert.Contains("\"hasLegacyHash\": true", text);

        var data = new DataStore(path).LoadOrCreate(() => throw new InvalidOperationException("the fixture is missing"));

        Assert.Equivalent(Expected(version), data, strict: true);
        Assert.Equal(Order(Expected(version)), Order(data)); // Equivalent ignores the order of list entries
        Assert.True(data.Settings.CheckPassword("geheim"));
        Assert.True(PasswordHash.Verify(data.Channels[1].PasswordHash, "kanal"));
    }

    [Theory]
    [MemberData(nameof(PersistedFixtures.Versions), MemberType = typeof(PersistedFixtures))]
    public void BackupStore_FixtureOfVersion_ListsAndReadsEveryStoredValue(string version)
    {
        PersistedFixtures.CopyTo(version, BackupName, Path.Combine(dir, BackupStore.FolderName));
        var store = new BackupStore(dir, new ManualTimeProvider(Date));

        var info = Assert.Single(store.List());
        var content = BackupStore.Read(store.Find(info.FileName)!);

        Assert.Equal((BackupName, Date, BuildVersion), (info.FileName, info.CreatedAt, info.ServerVersion));
        Assert.Equal(new BackupManifest(BackupStore.FormatVersion, 6, BuildVersion, Date), content.Manifest);
        Assert.Equivalent(Expected(version), JsonSerializer.Deserialize<ServerData>(content.Data, ProtocolJson.Options), strict: true);
        using var certificate = X509CertificateLoader.LoadPkcs12(content.Certificate, null);
        Assert.Equal("CN=OpenVoiceSpeak", certificate.Subject);
        Assert.Null(content.Icon);
    }

    /// <summary>The order of every stored list, which <see cref="Assert.Equivalent"/> does not check.</summary>
    static string[] Order(ServerData d) =>
    [
        .. d.Channels.Select(c => $"{c.Id}"), .. d.Groups.Select(g => $"{g.Id}"), .. d.Users.Select(u => u.Fingerprint),
        .. d.Users.Select(u => string.Join(",", u.GroupIds)), .. d.Users.SelectMany(u => u.PreviousNicknames), .. d.Bans.Select(b => $"{b.Id}"),
    ];

    /// <summary>What the fixture generator stored; the release had no channel bitrate yet, so its channels load with the default.</summary>
    static ServerData Expected(string version)
    {
        var lobby = new Guid("10000000-0000-0000-0000-000000000001");
        var raid = new Guid("10000000-0000-0000-0000-000000000002");
        var moderator = new Guid("20000000-0000-0000-0000-000000000001");
        var team = new Guid("20000000-0000-0000-0000-000000000002");
        var admin = new string('a', 64);
        var banned = new string('c', 64);
        return new ServerData
        {
            DataVersion = 6,
            Settings = new ServerSettings
            {
                Name = "Fixture-Server", WelcomeText = "Willkommen", PasswordHash = "ADDB0F5E7826C857D7376D1BD9BC33C0C544790A2EAC96144A8AF22B1298C940",
                MaxUsers = 42, LogDays = 14, LogRotateDaily = false, AutoRestart = true, AutoRestartTime = new TimeOnly(4, 30),
            },
            DefaultChannelId = lobby,
            Channels =
            [
                new ChannelRecord { Id = lobby, Name = "Lobby" },
                new ChannelRecord
                {
                    Id = raid, Name = "Raid", Description = "Mit Passwort", Order = 1, IsMuted = true, MaxUsers = 5,
                    Bitrate = version == PersistedFixtures.Current ? 96 : 64,
                    PasswordHash = version == PersistedFixtures.Current
                        ? "pbkdf2$100000$a2BpHGlppwK6yp1mDZn5tA==$EejxIRWPgCIybTZ9pfE1Kbk37mJiDIlVdKpYu9oqJVg="
                        : "pbkdf2$100000$/hf+SY0bsSEoY+Pwayi35g==$RiowhwpqRi2niCqqwfd950YHK57+Ko/doXpSRnRAz8k=",
                },
                new ChannelRecord { Id = new Guid("10000000-0000-0000-0000-000000000003"), Order = 2, Kind = ChannelKind.Separator },
                new ChannelRecord { Id = new Guid("10000000-0000-0000-0000-000000000004"), Name = "Gesperrt", Order = 3, AllowedGroupIds = [] },
            ],
            Links = [new ChannelLink(lobby, raid)],
            Groups =
            [
                new Group(WellKnownGroups.Guest, "Gast", Permission.Speak | Permission.ChatChannel | Permission.ChatPrivate),
                new Group(moderator, "Moderator", PermissionRules.ModeratorPermissions),
                new Group(WellKnownGroups.Admin, "Admin", Permission.All),
                new Group(team, "Team", Permission.Speak | Permission.ChannelCreate | Permission.ChatChannel),
            ],
            Users =
            [
                new UserRecord
                {
                    Fingerprint = admin, LastNickname = "Chefin", GroupIds = [WellKnownGroups.Admin], FirstSeen = Date.AddDays(-30),
                    LastLogin = Date.AddHours(-2), LoginCount = 12, LastIp = "192.0.2.10", PreviousNicknames = ["Anna", "Anni"],
                    OnlineTime = TimeSpan.FromHours(5.5), SpeechTime = TimeSpan.FromMinutes(42), ChatMessages = 17,
                },
                new UserRecord
                {
                    Fingerprint = new string('b', 64), LastNickname = "Bert", GroupIds = [team, moderator], FirstSeen = Date.AddDays(-10),
                    LastLogin = Date.AddDays(-1), LoginCount = 3, LastIp = "2001:db8::5", ServerMuted = true,
                },
                new UserRecord { Fingerprint = banned, LastNickname = "Stoerer", GroupIds = [WellKnownGroups.Guest], FirstSeen = Date.AddDays(-5) },
            ],
            Bans =
            [
                new BanRecord
                {
                    Id = new Guid("30000000-0000-0000-0000-000000000001"), Fingerprint = banned, Nickname = "Stoerer", Ip = "198.51.100.7",
                    Reason = "Spam", CreatedBy = "Chefin", CreatedByFingerprint = admin, CreatedAt = Date.AddDays(-4), DurationMinutes = 60,
                    ExpiresAt = Date.AddDays(-4).AddMinutes(60), BlockedAttempts = 2, LastAttempt = Date.AddDays(-4).AddMinutes(10),
                    LastAttemptIp = "198.51.100.7",
                },
                new BanRecord
                {
                    Id = new Guid("30000000-0000-0000-0000-000000000002"), Fingerprint = banned, Nickname = "Stoerer", Reason = "Beleidigung",
                    CreatedBy = "Chefin", CreatedByFingerprint = admin, CreatedAt = Date.AddDays(-3), LiftedAt = Date.AddDays(-2), LiftedBy = "Chefin",
                },
            ],
        };
    }
}
