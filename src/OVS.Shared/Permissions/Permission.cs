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
    ChannelJoinFull = 1 << 16, // enter a full channel, or move someone into one (Package 35)
    // Package 76 (A92): seeing and acting are separate rights. GroupsManage now only edits (name, rights, order).
    UsersView = 1 << 17,    // the user overview and ListUsers
    BansView = 1 << 18,     // the ban list
    GroupsView = 1 << 19,   // the groups tab
    GroupsCreate = 1 << 20,
    GroupsDelete = 1 << 21,
    UserDelete = 1 << 22,   // delete a user's stored data (Package 72)
    LogsView = 1 << 23,     // Package 81 (A98): list, read and search the server and channel logs
    LogsDownload = 1 << 24, // Package 82 (A99): save log files on the own PC (the tab needs LogsView)
    BackupsManage = 1 << 25, // Package 89 (A101): list, create, download and delete backups; upload and restore need the Admin group
    All = (1 << 26) - 1,
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

    /// <summary>Package 84 (A102): actions on others only work on users whose rights are a strict subset of the actor's.</summary>
    public static bool CanActOn(this Permission actor, Permission target) => target != actor && target.IsSubsetOf(actor);

    /// <summary>
    /// Package 84 (A102): the one rule for every action on another user (kick, ban, unban, mute, move, group change, delete),
    /// used by the server to decide and by the client to show the actions: never oneself, only strictly weaker users.
    /// </summary>
    public static bool CanModerate(this Permission actor, string actorFingerprint, Permission target, string targetFingerprint) =>
        actorFingerprint != targetFingerprint && actor.CanActOn(target);
}
