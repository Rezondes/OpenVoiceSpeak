namespace OVS.Shared.Permissions;

[Flags]
public enum Permission
{
    None = 0,
    Speak = 1 << 0,
    SpeakLinked = 1 << 1,
    ChannelCreate = 1 << 2,
    ChannelEdit = 1 << 3,
    ChannelDelete = 1 << 4,
    ChannelLink = 1 << 5,
    UserMove = 1 << 6,
    UserMute = 1 << 7,
    UserKick = 1 << 8,
    UserBan = 1 << 9,
    GroupsManage = 1 << 10,
    GroupsAssign = 1 << 11,
    ServerConfig = 1 << 12,
    ChatServer = 1 << 13,   // write in "Allgemein", to everyone on the server (Package 31)
    ChatChannel = 1 << 14,  // write in the own channel
    ChatPrivate = 1 << 15,  // write private messages
    All = (1 << 16) - 1,
}

/// <summary>Fixed ids of the two protected groups, known to server and client.</summary>
public static class WellKnownGroups
{
    public static readonly Guid Guest = Guid.Parse("00000000-0000-0000-0000-00000000000a");
    public static readonly Guid Admin = Guid.Parse("00000000-0000-0000-0000-00000000000b");
}

public static class PermissionExtensions
{
    public static bool Has(this Permission perms, Permission required) => (perms & required) == required;

    public static bool IsSubsetOf(this Permission subset, Permission superset) => (subset & ~superset) == 0;

    /// <summary>Kick, ban, move and mute only work on users whose rights are a subset of the actor's.</summary>
    public static bool CanActOn(this Permission actor, Permission target) => target.IsSubsetOf(actor);
}
