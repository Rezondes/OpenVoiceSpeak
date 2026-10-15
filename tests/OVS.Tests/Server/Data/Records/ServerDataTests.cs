using System.Security.Cryptography;
using System.Text.Json;
using OVS.Server;
using OVS.Server.Data.Records;
using OVS.Server.Security;
using OVS.Shared.Permissions;
using OVS.Shared.Protocol.Framing;
using OVS.Shared.Protocol.Messages.ServerInfo;
using OVS.Tests.TestSupport;

namespace OVS.Tests.Server.Data.Records;

public class ServerDataTests
{
    static ServerData Valid()
    {
        var data = ServerData.CreateDefault(new ServerConfig(0, "unused", 10, "Testserver", ""));
        data.Users.Add(new UserRecord { Fingerprint = "f", LastNickname = "anna", GroupIds = [WellKnownGroups.Admin] });
        data.Bans.Add(new BanRecord { Id = Guid.NewGuid(), Fingerprint = "g", Nickname = "bert", Reason = "", CreatedBy = "anna" });
        return data;
    }

    [Fact]
    public void FindProblem_ValidData_None() => Assert.Null(Valid().FindProblem());

    /// <summary>The server settings write only their stored values: the limits and the legacy check are methods, not keys.</summary>
    [Fact]
    public void Json_ServerSettings_OnlyStoredKeys()
    {
        var settings = new ServerSettings
        {
            StoredPassword = StoredPassword.FromStored(new string('A', 64)), MaxUsers = 42, LogDays = 14, LogRotateDaily = false,
            AutoRestart = true, AutoRestartTime = new TimeOnly(4, 30), AfkChannelId = Guid.NewGuid(), AfkMinutes = 5,
        };

        var keys = JsonDocument.Parse(JsonSerializer.Serialize(settings, ProtocolJson.Indented)).RootElement.EnumerateObject().Select(jsonProperty => jsonProperty.Name);

        Assert.Equal(["name", "welcomeText", "passwordHash", "maxUsers", "logDays", "logRotateDaily", "autoRestart", "autoRestartTime", "afkChannelId", "afkMinutes"], keys);
    }

    [Fact]
    public void ToServerLimits_StoredSettings_SameValues()
    {
        var settings = new ServerSettings { MaxUsers = 42, LogDays = 14, LogRotateDaily = false, AutoRestart = true, AutoRestartTime = new TimeOnly(4, 30) };
        Assert.Equal(new ServerLimits(42, 14, false, true, new TimeOnly(4, 30)), settings.ToServerLimits());
    }

    /// <summary>Package 91: both stored formats of the server password count as valid.</summary>
    [Theory]
    [InlineData("pbkdf2")]
    [InlineData("legacy")]
    public void FindProblem_StoredPasswordHash_None(string format)
    {
        var data = Valid();
        data.Settings.StoredPassword = format == "pbkdf2"
            ? StoredPassword.Create("geheim")
            : StoredPassword.FromStored(Convert.ToHexString(SHA256.HashData("geheim"u8))); // the legacy format: uppercase SHA-256 hex
        Assert.Null(data.FindProblem());
    }

    /// <summary>Package 83: stored names keep the looser rule they had, so data with a name new input would refuse stays usable.</summary>
    [Fact]
    public void FindProblem_StoredNameNewInputWouldRefuse_None()
    {
        var data = Valid();
        data.Users[0].LastNickname = "Alt​";
        data.Channels.Add(new ChannelRecord { Id = Guid.NewGuid(), Name = "Raid‮", Order = 1 });
        Assert.Null(data.FindProblem());
    }

    /// <summary>A user known only by fingerprint (no nickname stored yet) is complete.</summary>
    [Fact]
    public void FindProblem_EmptyLastNickname_None()
    {
        var data = Valid();
        data.Users[0].LastNickname = "";
        Assert.Null(data.FindProblem());
    }

    public static TheoryData<string, string> Cases => new()
    {
        { "users-null", "settings or a list is missing" },
        { "settings-null", "settings or a list is missing" },
        { "channels-null", "settings or a list is missing" },
        { "links-null", "settings or a list is missing" },
        { "groups-null", "settings or a list is missing" },
        { "bans-null", "settings or a list is missing" },
        { "channel-entry-null", "empty entry in a list" },
        { "link-entry-null", "empty entry in a list" },
        { "group-entry-null", "empty entry in a list" },
        { "user-entry-null", "empty entry in a list" },
        { "ban-entry-null", "empty entry in a list" },
        { "server-name-null", "server name or welcome text invalid" },
        { "server-name-control", "server name or welcome text invalid" },
        { "welcome-null", "server name or welcome text invalid" },
        { "welcome-too-long", "server name or welcome text invalid" },
        { "bad-password-hash", "password hash invalid" },
        { "channel-name-empty", "channel name or bitrate invalid" },
        { "channel-name-control", "channel name or bitrate invalid" },
        { "channel-description-too-long", "channel name or bitrate invalid" },
        { "channel-bitrate-low", "channel name or bitrate invalid" },
        { "channel-bitrate-high", "channel name or bitrate invalid" },
        { "unknown-kind", "channel name or bitrate invalid" },
        { "separator-named", "channel name or bitrate invalid" },
        { "separator-described", "channel name or bitrate invalid" },
        { "separator-muted", "channel name or bitrate invalid" },
        { "separator-with-options", "channel name or bitrate invalid" },
        { "separator-locked", "channel name or bitrate invalid" },
        { "separator-password", "channel name or bitrate invalid" },
        { "channel-id-twice", "channel id twice" },
        { "no-default-channel", "default channel missing" },
        { "dangling-link", "link to an unknown channel" },
        { "separator-default", "separator as default channel" },
        { "separator-linked", "link to a separator" },
        { "group-name-too-long", "group invalid" },
        { "group-unknown-permission", "group invalid" },
        { "group-id-twice", "group id twice" },
        { "no-guest-group", "guest or Admin group missing" },
        { "user-groups-null", "user incomplete" },
        { "user-no-fingerprint", "user incomplete" },
        { "user-nicknames-null", "user incomplete" },
        { "user-nickname-entry-null", "user incomplete" },
        { "nickname-too-long", "nickname invalid" },
        { "user-twice", "user twice" },
        { "dangling-group", "user in an unknown group" },
        { "no-admin", "no member of the Admin group" },
        { "ban-fingerprint-null", "ban incomplete" },
        { "ban-nickname-null", "ban incomplete" },
        { "ban-reason-null", "ban incomplete" },
        { "ban-creator-null", "ban incomplete" },
        { "ban-id-twice", "ban id twice" },
    };

    [Theory]
    [MemberData(nameof(Cases))]
    public void FindProblem_BrokenData_NamesTheProblem(string variant, string problem)
    {
        var data = Valid();
        BrokenServerData.Break(data, variant);
        Assert.Equal(problem, data.FindProblem());
    }

    static readonly DateTimeOffset Now = new(2026, 10, 6, 12, 0, 0, TimeSpan.Zero);

    static ServerData WithEndedBans(int logDays)
    {
        var data = Valid();
        data.Settings.LogDays = logDays;
        data.Bans =
        [
            new BanRecord { Nickname = "aktiv" },
            new BanRecord { Nickname = "aufgehoben-alt", LiftedAt = Now.AddDays(-15) },
            new BanRecord { Nickname = "abgelaufen-alt", ExpiresAt = Now.AddDays(-14).AddTicks(-1) },
            new BanRecord { Nickname = "abgelaufen-genau", ExpiresAt = Now.AddDays(-14) },
            new BanRecord { Nickname = "aufgehoben-neu", LiftedAt = Now.AddDays(-13), ExpiresAt = Now.AddDays(-20) },
        ];
        return data;
    }

    [Fact]
    public void PruneEndedBans_EndedLongerAgoThanLogDays_Removed()
    {
        var data = WithEndedBans(logDays: 14);

        data.PruneEndedBans(Now);

        Assert.Equal(["aktiv", "abgelaufen-genau", "aufgehoben-neu"], data.Bans.Select(ban => ban.Nickname));
    }

    [Fact]
    public void PruneEndedBans_LogDaysZero_KeepsEveryBan()
    {
        var data = WithEndedBans(logDays: 0);

        data.PruneEndedBans(Now);

        Assert.Equal(5, data.Bans.Count);
    }
}
