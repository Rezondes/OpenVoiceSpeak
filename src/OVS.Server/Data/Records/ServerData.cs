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
        var banned = Bans.Select(b => b.Fingerprint).ToHashSet();
        return Users.RemoveAll(u => u.GroupIds.All(g => g == PermissionRules.GuestGroupId) && !banned.Contains(u.Fingerprint)
                                    && (u.LastLogin ?? u.FirstSeen) is var seen && seen != default && seen < cutoff);
    }

    /// <summary>
    /// Package 80 (A97): lifted and expired bans stay as history and are dropped once they ended longer ago than the log
    /// retention (LogDays, 0 = forever).
    /// </summary>
    public void PruneEndedBans(DateTimeOffset now)
    {
        if (Settings.LogDays <= 0) return;
        var cutoff = now.AddDays(-Settings.LogDays);
        Bans.RemoveAll(b => b.EndedAt(now) < cutoff);
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
        int guest = Groups.FindIndex(g => g.Id == PermissionRules.GuestGroupId);
        if (DataVersion < 2 && guest >= 0) // A28: guests may chat, granted once
            Groups[guest] = Groups[guest] with { Permissions = Groups[guest].Permissions | Permission.ChatChannel | Permission.ChatPrivate };
        if (DataVersion < 3) Settings.TakeStartValues(config);
        if (DataVersion < 4) // Package 76 (A92): nobody loses a possibility; a stored "All" already reads as every new right
            for (int i = 0; i < Groups.Count; i++) Groups[i] = Groups[i] with { Permissions = WithViewRights(Groups[i].Permissions) };
        if (DataVersion < 5) // Package 89 (A101): backups were part of ServerConfig; a stored "All" already reads as the new right
            for (int i = 0; i < Groups.Count; i++)
                if (Groups[i].Permissions.Has(Permission.ServerConfig)) Groups[i] = Groups[i] with { Permissions = Groups[i].Permissions | Permission.BackupsManage };
        if (DataVersion < 6) // Package 94: a stored "All" reads as the new right too, but only Admin gets it
            for (int i = 0; i < Groups.Count; i++)
                if (Groups[i].Id != PermissionRules.AdminGroupId) Groups[i] = Groups[i] with { Permissions = Groups[i].Permissions & ~Permission.ChannelPasswordBypass };
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
        if (!new object?[] { Settings, Channels, Links, Groups, Users, Bans }.All(o => o is not null)) return "settings or a list is missing";
        return Channels.Contains(null!) || Links.Contains(null!) || Groups.Contains(null!) || Users.Contains(null!) || Bans.Contains(null!)
            ? "empty entry in a list"
            : null;
    }

    string? FindSettingsProblem()
    {
        if (!StoredName(Settings.Name, ProtocolInfo.MaxNameLength) || Settings.WelcomeText is not { Length: <= ProtocolInfo.MaxTextLength })
            return "server name or welcome text invalid";
        return Settings.StoredPassword is null || Settings.StoredPassword.IsValid() ? null : "password hash invalid"; // Package 91: both formats
    }

    /// <summary>Package 111: a separator has an empty name and description, is never the default channel and never linked.</summary>
    string? FindChannelProblem()
    {
        if (!Channels.All(c => c.Kind switch
            {
                // Package 132: a separator's bitrate is never read (one stored before the package loads as 64)
                ChannelKind.Voice => StoredName(c.Name, ProtocolInfo.MaxNameLength) && c.Description is { Length: <= ProtocolInfo.MaxTextLength }
                                     && ChannelSettingsRule.CheckBitrate(c.Bitrate) is null,
                ChannelKind.Separator => c is { Name: "", Description: "", IsMuted: false, MaxUsers: 0, AllowedGroupIds: null, StoredPassword: null },
                _ => false,
            }))
            return "channel name or bitrate invalid";
        if (!AllUnique(Channels.Select(c => c.Id))) return "channel id twice";
        var channels = Channels.Select(c => c.Id).ToHashSet();
        if (!channels.Contains(DefaultChannelId)) return "default channel missing";
        if (!Links.All(l => channels.Contains(l.A) && channels.Contains(l.B))) return "link to an unknown channel";
        var separators = Channels.Where(c => c.Kind == ChannelKind.Separator).Select(c => c.Id).ToHashSet();
        if (separators.Contains(DefaultChannelId)) return "separator as default channel";
        return Links.Any(l => separators.Contains(l.A) || separators.Contains(l.B)) ? "link to a separator" : null;
    }

    string? FindGroupProblem()
    {
        if (!Groups.All(g => StoredName(g.Name, ProtocolInfo.MaxGroupNameLength) && g.Permissions.IsSubsetOf(Permission.All))) return "group invalid";
        if (!AllUnique(Groups.Select(g => g.Id))) return "group id twice";
        return Groups.Any(g => g.Id == WellKnownGroups.Guest) && Groups.Any(g => g.Id == WellKnownGroups.Admin) ? null : "guest or Admin group missing";
    }

    string? FindUserProblem()
    {
        if (!Users.All(u => u.Fingerprint is { Length: > 0 } && u.GroupIds is not null && u.PreviousNicknames is not null
                            && !u.PreviousNicknames.Contains(null!)))
            return "user incomplete";
        if (!Users.All(u => u.LastNickname is "" || StoredName(u.LastNickname, ProtocolInfo.MaxNicknameLength))) return "nickname invalid";
        if (!AllUnique(Users.Select(u => u.Fingerprint))) return "user twice";
        var groups = Groups.Select(g => g.Id).ToHashSet();
        if (!Users.All(u => u.GroupIds.All(groups.Contains))) return "user in an unknown group";
        return Users.Any(u => u.GroupIds.Contains(WellKnownGroups.Admin)) ? null : "no member of the Admin group";
    }

    string? FindBanProblem()
    {
        if (!Bans.All(b => b.Fingerprint is not null && b.Nickname is not null && b.Reason is not null && b.CreatedBy is not null)) return "ban incomplete";
        return AllUnique(Bans.Select(b => b.Id)) ? null : "ban id twice";
    }

    /// <summary>
    /// Package 83: deliberately looser than the text rules for new input (TextRules): stored data with a name new input would
    /// refuse keeps loading on start, so a backup of it must stay restorable; control characters are still refused.
    /// </summary>
    static bool StoredName(string? name, int max) => name?.Trim() is { Length: > 0 } n && n.Length <= max && !n.Any(char.IsControl);

    static bool AllUnique<T>(IEnumerable<T> ids)
    {
        var seen = new HashSet<T>();
        return ids.All(seen.Add);
    }

    static Permission WithViewRights(Permission p)
    {
        if (p.Has(Permission.GroupsManage)) p |= Permission.GroupsView | Permission.GroupsCreate | Permission.GroupsDelete;
        if (p.Has(Permission.GroupsAssign)) p |= Permission.UsersView;
        if (p.Has(Permission.UserBan)) p |= Permission.BansView;
        return p;
    }
}
