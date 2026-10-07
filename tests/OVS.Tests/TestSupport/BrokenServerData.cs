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
    public static void Break(ServerData d, string variant)
    {
        var lobby = d.Channels.Count > 0 ? d.Channels[0] : null!;
        switch (variant)
        {
            case "users-null": d.Users = null!; break;
            case "settings-null": d.Settings = null!; break;
            case "channels-null": d.Channels = null!; break;
            case "links-null": d.Links = null!; break;
            case "groups-null": d.Groups = null!; break;
            case "bans-null": d.Bans = null!; break;
            case "channel-entry-null": d.Channels.Add(null!); break;
            case "link-entry-null": d.Links.Add(null!); break;
            case "group-entry-null": d.Groups.Add(null!); break;
            case "user-entry-null": d.Users.Add(null!); break;
            case "ban-entry-null": d.Bans.Add(null!); break;
            case "server-name-null": d.Settings.Name = null!; break;
            case "server-name-control": d.Settings.Name = "a\u0001"; break;
            case "welcome-null": d.Settings.WelcomeText = null!; break;
            case "welcome-too-long": d.Settings.WelcomeText = new string('w', ProtocolInfo.MaxTextLength + 1); break;
            case "bad-password-hash": d.Settings.StoredPassword = StoredPassword.FromStored("kein-hash"); break;
            case "channel-name-empty": lobby.Name = " "; break;
            case "channel-name-control": lobby.Name = "a\u0007b"; break;
            case "channel-description-too-long": lobby.Description = new string('d', ProtocolInfo.MaxTextLength + 1); break;
            case "channel-bitrate-low": lobby.Bitrate = ProtocolInfo.MinBitrate - 1; break;
            case "channel-bitrate-high": lobby.Bitrate = 200; break;
            case "unknown-kind": lobby.Kind = (ChannelKind)7; break;
            case "channel-id-twice": d.Channels.Add(new ChannelRecord { Id = lobby.Id, Name = "Zweite" }); break;
            case "no-default-channel": d.DefaultChannelId = Guid.NewGuid(); break;
            case "dangling-link": d.Links.Add(ChannelLinkRecord.Of(lobby.Id, Guid.NewGuid())); break;
            case "separator-default": // Package 111
                d.Channels.Single(c => c.Id == d.DefaultChannelId).Name = "";
                d.Channels.Single(c => c.Id == d.DefaultChannelId).Kind = ChannelKind.Separator;
                break;
            case "separator-linked":
                var separator = new ChannelRecord { Id = Guid.NewGuid(), Order = 9, Kind = ChannelKind.Separator };
                d.Channels.Add(separator);
                d.Links.Add(ChannelLinkRecord.Of(d.DefaultChannelId, separator.Id));
                break;
            case "separator-named": d.Channels.Add(Separator(c => c.Name = "Name")); break;
            case "separator-with-options": d.Channels.Add(Separator(c => c.MaxUsers = 5)); break;
            case "separator-described": d.Channels.Add(Separator(c => c.Description = "Text")); break;
            case "separator-muted": d.Channels.Add(Separator(c => c.IsMuted = true)); break;
            case "separator-locked": d.Channels.Add(Separator(c => c.AllowedGroupIds = [])); break;
            case "separator-password": d.Channels.Add(Separator(c => c.StoredPassword = StoredPassword.FromStored("kein-hash"))); break;
            case "group-name-too-long": d.Groups[1] = d.Groups[1] with { Name = new string('g', 33) }; break;
            case "group-unknown-permission": d.Groups[1] = d.Groups[1] with { Permissions = (Permission)(1 << 28) }; break;
            case "group-id-twice": d.Groups.Add(d.Groups[1] with { Name = "Kopie" }); break;
            case "no-guest-group": d.Groups.RemoveAll(g => g.Id == WellKnownGroups.Guest); break;
            case "user-groups-null": d.Users[0].GroupIds = null!; break;
            case "user-no-fingerprint": d.Users.Add(new UserRecord { Fingerprint = "" }); break;
            case "user-nicknames-null": d.Users[0].PreviousNicknames = null!; break;
            case "user-nickname-entry-null": d.Users[0].PreviousNicknames.Add(null!); break;
            case "nickname-too-long": d.Users[0].LastNickname = new string('n', 33); break;
            case "user-twice": d.Users.Add(new UserRecord { Fingerprint = d.Users[0].Fingerprint, LastNickname = "doppelt" }); break;
            case "dangling-group": d.Users[0].GroupIds.Add(Guid.NewGuid()); break;
            case "no-admin": d.Users.ForEach(u => u.GroupIds.Remove(WellKnownGroups.Admin)); break;
            case "ban-fingerprint-null": d.Bans[0].Fingerprint = null!; break;
            case "ban-nickname-null": d.Bans[0].Nickname = null!; break;
            case "ban-reason-null": d.Bans[0].Reason = null!; break;
            case "ban-creator-null": d.Bans[0].CreatedBy = null!; break;
            case "ban-id-twice": d.Bans.Add(new BanRecord { Id = d.Bans[0].Id, Fingerprint = "h", Nickname = "c", Reason = "", CreatedBy = "anna" }); break;
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
