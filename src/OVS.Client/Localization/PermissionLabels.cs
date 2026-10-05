using OVS.Shared.Permissions;

namespace OVS.Client.Localization;

public static class PermissionLabels
{
    /// <summary>Built on every call, so it follows the language of the calling thread.</summary>
    public static IReadOnlyList<(Permission Permission, string Label)> All =>
    [
        (Permission.Speak, Strings.Perm_Speak),
        (Permission.SpeakLinked, Strings.Perm_SpeakLinked),
        (Permission.ChannelCreate, Strings.Perm_ChannelCreate),
        (Permission.ChannelEdit, Strings.Perm_ChannelEdit),
        (Permission.ChannelDelete, Strings.Perm_ChannelDelete),
        (Permission.ChannelLink, Strings.Perm_ChannelLink),
        (Permission.UserMove, Strings.Perm_UserMove),
        (Permission.UserMute, Strings.Perm_UserMute),
        (Permission.UserKick, Strings.Perm_UserKick),
        (Permission.UserBan, Strings.Perm_UserBan),
        (Permission.UserDelete, Strings.Perm_UserDelete),
        (Permission.UsersView, Strings.Perm_UsersView),
        (Permission.BansView, Strings.Perm_BansView),
        (Permission.GroupsView, Strings.Perm_GroupsView),
        (Permission.GroupsCreate, Strings.Perm_GroupsCreate),
        (Permission.GroupsManage, Strings.Perm_GroupsManage),
        (Permission.GroupsDelete, Strings.Perm_GroupsDelete),
        (Permission.GroupsAssign, Strings.Perm_GroupsAssign),
        (Permission.ServerConfig, Strings.Perm_ServerConfig),
        (Permission.LogsView, Strings.Perm_LogsView),
        (Permission.LogsDownload, Strings.Perm_LogsDownload),
        (Permission.BackupsManage, Strings.Perm_BackupsManage), // Package 89
        (Permission.ChatServer, Strings.Perm_ChatServer),
        (Permission.ChatChannel, Strings.Perm_ChatChannel),
        (Permission.ChatPrivate, Strings.Perm_ChatPrivate),
        (Permission.ChannelPasswordBypass, Strings.Perm_ChannelPasswordBypass), // Package 94
        (Permission.ChannelJoinFull, Strings.Perm_ChannelJoinFull),
    ];
}
