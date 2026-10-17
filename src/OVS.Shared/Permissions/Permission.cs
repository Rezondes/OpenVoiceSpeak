namespace OVS.Shared.Permissions;

/// <summary>The rights a group grants, as flags; a user's rights are the union of their groups'.</summary>
[Flags]
#pragma warning disable CA1711 // the domain's word (rule 17)
public enum Permission
#pragma warning restore CA1711
{
    /// <summary>No right.</summary>
    None = 0,
    /// <summary>Talk in the own channel.</summary>
    Speak = 1 << 0,
    /// <summary>Talk into the linked channels too, with link push-to-talk.</summary>
    SpeakLinked = 1 << 1,
    /// <summary>Create channels.</summary>
    ChannelCreate = 1 << 2,
    /// <summary>Edit and reorder channels.</summary>
    ChannelEdit = 1 << 3,
    /// <summary>Delete channels.</summary>
    ChannelDelete = 1 << 4,
    /// <summary>Link and unlink channels.</summary>
    ChannelLink = 1 << 5,
    /// <summary>Move other users into another channel.</summary>
    UserMove = 1 << 6,
    /// <summary>Mute other users at the server.</summary>
    UserMute = 1 << 7,
    /// <summary>Kick other users.</summary>
    UserKick = 1 << 8,
    /// <summary>Ban other users.</summary>
    UserBan = 1 << 9,
    /// <summary>Edit and reorder groups.</summary>
    GroupsManage = 1 << 10,
    /// <summary>Give and take groups.</summary>
    GroupsAssign = 1 << 11,
    /// <summary>Change the server settings and its logo.</summary>
    ServerConfig = 1 << 12,
    /// <summary>Write in "Allgemein", to everyone on the server (Package 31).</summary>
    ChatServer = 1 << 13,
    /// <summary>Write in the own channel.</summary>
    ChatChannel = 1 << 14,
    /// <summary>Write private messages.</summary>
    ChatPrivate = 1 << 15,
    /// <summary>Enter a full channel, or move someone into one (Package 35).</summary>
    ChannelJoinFull = 1 << 16,
    // Package 76 (A92): seeing and acting are separate rights. GroupsManage now only edits (name, rights, order).
    /// <summary>See the user overview (ListUsers).</summary>
    UsersView = 1 << 17,
    /// <summary>See the ban list.</summary>
    BansView = 1 << 18,
    /// <summary>See the groups tab.</summary>
    GroupsView = 1 << 19,
    /// <summary>Create groups.</summary>
    GroupsCreate = 1 << 20,
    /// <summary>Delete groups.</summary>
    GroupsDelete = 1 << 21,
    /// <summary>Delete a user's stored data (Package 72).</summary>
    UserDelete = 1 << 22,
    /// <summary>Package 81 (A98): list, read and search the server and channel logs.</summary>
    LogsView = 1 << 23,
    /// <summary>Package 82 (A99): save log files on the own PC (the tab needs LogsView)</summary>
    LogsDownload = 1 << 24,
    /// <summary>Package 89 (A101): list, create, download and delete backups; upload and restore need the Admin group.</summary>
    BackupsManage = 1 << 25,
    /// <summary>Package 94: join password-locked channels without the password (never past a group lock)</summary>
    ChannelPasswordBypass = 1 << 26,
    /// <summary>Every right.</summary>
    All = (1 << 27) - 1,
    /// <summary>
    /// Package 181 (A190): the server admin's rank, outside All; only the server's rights of a Server-Admin member add it, so
    /// the strict-subset rule (A102) puts the server admin above every admin. No group stores it.
    /// </summary>
    ServerAdminRank = 1 << 27,
}
