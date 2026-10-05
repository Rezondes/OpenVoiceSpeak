using OVS.Server.Permissions;
using OVS.Server.Security;
using OVS.Shared.Permissions;

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
    public List<ChannelLink> Links { get; set; } = [];
    public List<Group> Groups { get; set; } = [];
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

    public static ServerData CreateDefault(ServerConfig config)
    {
        var lobby = new ChannelRecord { Id = Guid.NewGuid(), Name = "Lobby" };
        var settings = new ServerSettings { Name = config.ServerName, PasswordHash = PasswordHash.Hash(config.Password) };
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

    static Permission WithViewRights(Permission p)
    {
        if (p.Has(Permission.GroupsManage)) p |= Permission.GroupsView | Permission.GroupsCreate | Permission.GroupsDelete;
        if (p.Has(Permission.GroupsAssign)) p |= Permission.UsersView;
        if (p.Has(Permission.UserBan)) p |= Permission.BansView;
        return p;
    }
}
