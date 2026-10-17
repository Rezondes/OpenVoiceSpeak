using OVS.Server.Data.Records;
using OVS.Server.Security;
using OVS.Shared.Permissions;
using OVS.Shared.Protocol;
using OVS.Shared.Protocol.Messages.Channels;

namespace OVS.Tests.TestSupport;

/// <summary>
/// Breaks valid server data in one named way, for the checks a start relies on (<see cref="ServerData.FindProblem"/>) and
/// the restore that refuses such a backup. Expects the default groups (Guest, Moderator, Admin), the default channel
/// first, the first user in the Admin group and, for the ban variants, one ban.
/// </summary>
static class BrokenServerData
{
    public static void Break(ServerData serverData, string variant)
    {
        var lobby = serverData.Channels.Count > 0 ? serverData.Channels[0] : null!;
        switch (variant)
        {
            case "users-null": serverData.Users = null!; break;
            case "settings-null": serverData.Settings = null!; break;
            case "channels-null": serverData.Channels = null!; break;
            case "links-null": serverData.Links = null!; break;
            case "groups-null": serverData.Groups = null!; break;
            case "bans-null": serverData.Bans = null!; break;
            case "channel-entry-null": serverData.Channels.Add(null!); break;
            case "link-entry-null": serverData.Links.Add(null!); break;
            case "group-entry-null": serverData.Groups.Add(null!); break;
            case "user-entry-null": serverData.Users.Add(null!); break;
            case "ban-entry-null": serverData.Bans.Add(null!); break;
            case "server-name-null": serverData.Settings.Name = null!; break;
            case "server-name-control": serverData.Settings.Name = "a\u0001"; break;
            case "welcome-null": serverData.Settings.WelcomeText = null!; break;
            case "welcome-too-long": serverData.Settings.WelcomeText = new string('w', ProtocolLimits.MaxTextLength + 1); break;
            case "bad-password-hash": serverData.Settings.StoredPassword = StoredPassword.FromStored("kein-hash"); break;
            case "channel-name-empty": lobby.Name = " "; break;
            case "channel-name-control": lobby.Name = "a\u0007b"; break;
            case "channel-description-too-long": lobby.Description = new string('d', ProtocolLimits.MaxTextLength + 1); break;
            case "channel-bitrate-low": lobby.Bitrate = ProtocolLimits.MinBitrate - 1; break;
            case "channel-bitrate-high": lobby.Bitrate = ProtocolLimits.MaxBitrate + 1; break;
            case "unknown-kind": lobby.Kind = (ChannelKind)7; break;
            case "channel-id-twice": serverData.Channels.Add(new ChannelRecord { Id = lobby.Id, Name = "Zweite" }); break;
            case "no-default-channel": serverData.DefaultChannelId = Guid.NewGuid(); break;
            case "dangling-link": serverData.Links.Add(ChannelLinkRecord.Of(lobby.Id, Guid.NewGuid())); break;
            case "separator-default": // Package 111
                serverData.Channels.Single(channel => channel.Id == serverData.DefaultChannelId).Name = "";
                serverData.Channels.Single(channel => channel.Id == serverData.DefaultChannelId).Kind = ChannelKind.Separator;
                break;
            case "separator-linked":
                var separator = new ChannelRecord { Id = Guid.NewGuid(), Order = 9, Kind = ChannelKind.Separator };
                serverData.Channels.Add(separator);
                serverData.Links.Add(ChannelLinkRecord.Of(serverData.DefaultChannelId, separator.Id));
                break;
            case "separator-named": serverData.Channels.Add(Separator(channel => channel.Name = "Name")); break;
            case "separator-with-options": serverData.Channels.Add(Separator(channel => channel.MaxUsers = 5)); break;
            case "separator-described": serverData.Channels.Add(Separator(channel => channel.Description = "Text")); break;
            case "separator-muted": serverData.Channels.Add(Separator(channel => channel.IsMuted = true)); break;
            case "separator-locked": serverData.Channels.Add(Separator(channel => channel.AllowedGroupIds = [])); break;
            case "separator-password": serverData.Channels.Add(Separator(channel => channel.StoredPassword = StoredPassword.FromStored("kein-hash"))); break;
            case "group-name-too-long": serverData.Groups[1] = serverData.Groups[1] with { Name = new string('g', 33) }; break;
            case "group-unknown-permission": serverData.Groups[1] = serverData.Groups[1] with { Permissions = (Permission)(1 << 28) }; break;
            case "group-id-twice": serverData.Groups.Add(serverData.Groups[1] with { Name = "Kopie" }); break;
            case "no-guest-group": serverData.Groups.RemoveAll(groupRecord => groupRecord.Id == WellKnownGroups.Guest); break;
            case "user-groups-null": serverData.Users[0].GroupIds = null!; break;
            case "user-no-fingerprint": serverData.Users.Add(new UserRecord { Fingerprint = "" }); break;
            case "user-nicknames-null": serverData.Users[0].PreviousNicknames = null!; break;
            case "user-nickname-entry-null": serverData.Users[0].PreviousNicknames.Add(null!); break;
            case "nickname-too-long": serverData.Users[0].LastNickname = new string('n', 33); break;
            case "user-twice": serverData.Users.Add(new UserRecord { Fingerprint = serverData.Users[0].Fingerprint, LastNickname = "doppelt" }); break;
            case "dangling-group": serverData.Users[0].GroupIds.Add(Guid.NewGuid()); break;
            case "no-admin": serverData.Users.ForEach(user => user.GroupIds.Remove(WellKnownGroups.Admin)); break;
            case "ban-fingerprint-null": serverData.Bans[0].Fingerprint = null!; break;
            case "ban-nickname-null": serverData.Bans[0].Nickname = null!; break;
            case "ban-reason-null": serverData.Bans[0].Reason = null!; break;
            case "ban-creator-null": serverData.Bans[0].CreatedBy = null!; break;
            case "ban-id-twice": serverData.Bans.Add(new BanRecord { Id = serverData.Bans[0].Id, Fingerprint = "h", Nickname = "c", Reason = "", CreatedBy = "anna" }); break;
            default: throw new ArgumentException(variant);
        }
    }

    static ChannelRecord Separator(Action<ChannelRecord> option)
    {
        var separator = new ChannelRecord { Id = Guid.NewGuid(), Order = 9, Kind = ChannelKind.Separator };
        option(separator);
        return separator;
    }
}
