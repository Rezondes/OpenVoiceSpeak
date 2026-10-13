using OVS.Server.Permissions;
using OVS.Server.Rules;
using OVS.Server.Security;
using OVS.Shared.Permissions;
using OVS.Shared.Protocol;
using OVS.Shared.Protocol.Messages.Channels;

namespace OVS.Server.Data.Records;

public sealed class ServerData
{
    /// <summary>
    /// 1 = before Package 31 (files without this field), 2 = chat rights, 3 = server settings from the environment (69),
    /// 4 = separate view, create and delete rights (76), 5 = the backup right (89), 6 = the channel password bypass only for
    /// Admin (94). New servers start at the current version.
    /// </summary>
    public const int CurrentVersion = 6;
    public int DataVersion { get; set; } = 1;

    public ServerSettings Settings { get; set; } = new();
    public Guid DefaultChannelId { get; set; }
    public List<ChannelRecord> Channels { get; set; } = [];
    public List<ChannelLinkRecord> Links { get; set; } = [];
    public List<GroupRecord> Groups { get; set; } = [];
    public List<UserRecord> Users { get; set; } = [];
    public List<BanRecord> Bans { get; set; } = [];

    /// <summary>
    /// Package 87: removes users that have nothing but the Guest group, no ban (active or past) and were last seen
    /// before cutoff. The group history is not stored, so "only ever Guest" is read as "Guest now". A record without
    /// any date (older than Package 70) is kept: its age is unknown. Returns the count.
    /// </summary>
    public int PruneGuests(DateTimeOffset cutoff)
    {
        var banned = Bans.Select(ban => ban.Fingerprint).ToHashSet();
        return Users.RemoveAll(user => user.GroupIds.All(id => id == PermissionRules.GuestGroupId) && !banned.Contains(user.Fingerprint)
                                    && (user.LastLogin ?? user.FirstSeen) is var seen && seen != default && seen < cutoff);
    }

    /// <summary>
    /// Package 80 (A97): lifted and expired bans stay as history and are dropped once they ended longer ago than the log
    /// retention (LogDays, 0 = forever).
    /// </summary>
    public void PruneEndedBans(DateTimeOffset now)
    {
        if (Settings.LogDays <= 0) return;
        var cutoff = now.AddDays(-Settings.LogDays);
        Bans.RemoveAll(ban => ban.EndedAt(now) < cutoff);
    }

    public static ServerData CreateDefault(ServerConfig config)
    {
        var lobby = new ChannelRecord { Id = Guid.NewGuid(), Name = "Lobby" };
        var settings = new ServerSettings { Name = config.ServerName, StoredPassword = StoredPassword.Create(config.Password) };
        settings.TakeStartValues(config);
        return new ServerData
        {
            Settings = settings,
            DefaultChannelId = lobby.Id,
            Channels = [lobby],
            Groups = PermissionRules.DefaultGroups(),
            DataVersion = CurrentVersion,
        };
    }

    /// <summary>Brings an older file up to date. Returns true when something changed and must be saved.</summary>
    /// <param name="config">Package 69: the start values a version 2 file takes over once.</param>
    public bool Migrate(ServerConfig config)
    {
        if (DataVersion >= CurrentVersion) return false;
        int guest = Groups.FindIndex(groupRecord => groupRecord.Id == PermissionRules.GuestGroupId);
        if (DataVersion < 2 && guest >= 0) // A28: guests may chat, granted once
            Groups[guest] = Groups[guest] with { Permissions = Groups[guest].Permissions | Permission.ChatChannel | Permission.ChatPrivate };
        if (DataVersion < 3) Settings.TakeStartValues(config);
        if (DataVersion < 4) // Package 76 (A92): nobody loses a possibility; a stored "All" already reads as every new right
            for (int index = 0; index < Groups.Count; index++) Groups[index] = Groups[index] with { Permissions = WithViewRights(Groups[index].Permissions) };
        if (DataVersion < 5) // Package 89 (A101): backups were part of ServerConfig; a stored "All" already reads as the new right
            for (int index = 0; index < Groups.Count; index++)
                if (Groups[index].Permissions.Has(Permission.ServerConfig)) Groups[index] = Groups[index] with { Permissions = Groups[index].Permissions | Permission.BackupsManage };
        if (DataVersion < 6) // Package 94: a stored "All" reads as the new right too, but only Admin gets it
            for (int index = 0; index < Groups.Count; index++)
                if (Groups[index].Id != PermissionRules.AdminGroupId) Groups[index] = Groups[index] with { Permissions = Groups[index].Permissions & ~Permission.ChannelPasswordBypass };
        DataVersion = CurrentVersion;
        return true;
    }

    /// <summary>
    /// Package 89 (A101): why a start of the server could not rely on this data, null when it can: every list present
    /// without empty entries, names under the stored-name rule, unique ids, references that exist and at least one member
    /// of the Admin group. The reason is English, for the log. A method, so it is never written to the file.
    /// </summary>
    public string? FindProblem() =>
        FindListProblem() ?? FindSettingsProblem() ?? FindChannelProblem() ?? FindGroupProblem() ?? FindUserProblem() ?? FindBanProblem();

    string? FindListProblem()
    {
        if (!new object?[] { Settings, Channels, Links, Groups, Users, Bans }.All(part => part is not null)) return "settings or a list is missing";
        return Channels.Contains(null!) || Links.Contains(null!) || Groups.Contains(null!) || Users.Contains(null!) || Bans.Contains(null!)
            ? "empty entry in a list"
            : null;
    }

    string? FindSettingsProblem()
    {
        if (!StoredName(Settings.Name, ProtocolLimits.MaxNameLength) || Settings.WelcomeText is not { Length: <= ProtocolLimits.MaxTextLength })
            return "server name or welcome text invalid";
        return Settings.StoredPassword is null || Settings.StoredPassword.IsValid() ? null : "password hash invalid"; // Package 91: both formats
    }

    /// <summary>Package 111: a separator has an empty name and description, is never the default channel and never linked.</summary>
    string? FindChannelProblem()
    {
        if (!Channels.All(channel => channel.Kind switch
            {
                // Package 132: a separator's bitrate is never read (one stored before the package loads as 64)
                ChannelKind.Voice => StoredName(channel.Name, ProtocolLimits.MaxNameLength) && channel.Description is { Length: <= ProtocolLimits.MaxTextLength }
                                     && ChannelSettingsRule.CheckBitrate(channel.Bitrate) is null,
                ChannelKind.Separator => channel is { Name: "", Description: "", IsMuted: false, MaxUsers: 0, AllowedGroupIds: null, StoredPassword: null },
                _ => false,
            }))
            return "channel name or bitrate invalid";
        if (!AllUnique(Channels.Select(channel => channel.Id))) return "channel id twice";
        var channels = Channels.Select(channel => channel.Id).ToHashSet();
        if (!channels.Contains(DefaultChannelId)) return "default channel missing";
        if (!Links.All(channelLink => channels.Contains(channelLink.A) && channels.Contains(channelLink.B))) return "link to an unknown channel";
        var separators = Channels.Where(channel => channel.Kind == ChannelKind.Separator).Select(channel => channel.Id).ToHashSet();
        if (separators.Contains(DefaultChannelId)) return "separator as default channel";
        return Links.Any(channelLink => separators.Contains(channelLink.A) || separators.Contains(channelLink.B)) ? "link to a separator" : null;
    }

    string? FindGroupProblem()
    {
        if (!Groups.All(groupRecord => StoredName(groupRecord.Name, ProtocolLimits.MaxGroupNameLength) && groupRecord.Permissions.IsSubsetOf(Permission.All))) return "group invalid";
        if (!AllUnique(Groups.Select(groupRecord => groupRecord.Id))) return "group id twice";
        return Groups.Any(groupRecord => groupRecord.Id == WellKnownGroups.Guest) && Groups.Any(groupRecord => groupRecord.Id == WellKnownGroups.Admin) ? null : "guest or Admin group missing";
    }

    string? FindUserProblem()
    {
        if (!Users.All(user => user.Fingerprint is { Length: > 0 } && user.GroupIds is not null && user.PreviousNicknames is not null
                            && !user.PreviousNicknames.Contains(null!)))
            return "user incomplete";
        if (!Users.All(user => user.LastNickname is "" || StoredName(user.LastNickname, ProtocolLimits.MaxNicknameLength))) return "nickname invalid";
        if (!AllUnique(Users.Select(user => user.Fingerprint))) return "user twice";
        var groups = Groups.Select(groupRecord => groupRecord.Id).ToHashSet();
        if (!Users.All(user => user.GroupIds.All(groups.Contains))) return "user in an unknown group";
        return Users.Any(user => user.GroupIds.Contains(WellKnownGroups.Admin)) ? null : "no member of the Admin group";
    }

    string? FindBanProblem()
    {
        if (!Bans.All(ban => ban.Fingerprint is not null && ban.Nickname is not null && ban.Reason is not null && ban.CreatedBy is not null)) return "ban incomplete";
        return AllUnique(Bans.Select(ban => ban.Id)) ? null : "ban id twice";
    }

    /// <summary>
    /// Package 83: deliberately looser than the text rules for new input (TextRules): stored data with a name new input would
    /// refuse keeps loading on start, so a backup of it must stay restorable; control characters are still refused.
    /// </summary>
    static bool StoredName(string? name, int max) => name?.Trim() is { Length: > 0 } text && text.Length <= max && !text.Any(char.IsControl);

    static bool AllUnique<T>(IEnumerable<T> ids)
    {
        var seen = new HashSet<T>();
        return ids.All(seen.Add);
    }

    static Permission WithViewRights(Permission permission)
    {
        if (permission.Has(Permission.GroupsManage)) permission |= Permission.GroupsView | Permission.GroupsCreate | Permission.GroupsDelete;
        if (permission.Has(Permission.GroupsAssign)) permission |= Permission.UsersView;
        if (permission.Has(Permission.UserBan)) permission |= Permission.BansView;
        return permission;
    }
}
