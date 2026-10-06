using System.Security.Cryptography;
using OVS.Server;
using OVS.Server.Data.Records;
using OVS.Server.Security;
using OVS.Shared.Permissions;
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

    /// <summary>Package 91: both stored formats of the server password count as valid.</summary>
    [Theory]
    [InlineData("pbkdf2")]
    [InlineData("legacy")]
    public void FindProblem_StoredPasswordHash_None(string format)
    {
        var data = Valid();
        data.Settings.PasswordHash = format == "pbkdf2"
            ? PasswordHash.Hash("geheim")
            : Convert.ToHexString(SHA256.HashData("geheim"u8)); // the legacy format: uppercase SHA-256 hex
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
}
