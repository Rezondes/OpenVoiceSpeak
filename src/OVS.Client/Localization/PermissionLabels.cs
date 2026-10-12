using OVS.Client.Models;
using OVS.Shared.Permissions;

namespace OVS.Client.Localization;

public static class PermissionLabels
{
    /// <summary>Built on every call, so it follows the language of the calling thread.</summary>
    public static IReadOnlyList<Choice<Permission>> All =>
    [
        new(Permission.Speak, Strings.Perm_Speak),
        new(Permission.SpeakLinked, Strings.Perm_SpeakLinked),
        new(Permission.ChannelCreate, Strings.Perm_ChannelCreate),
        new(Permission.ChannelEdit, Strings.Perm_ChannelEdit),
        new(Permission.ChannelDelete, Strings.Perm_ChannelDelete),
        new(Permission.ChannelLink, Strings.Perm_ChannelLink),
        new(Permission.UserMove, Strings.Perm_UserMove),
        new(Permission.UserMute, Strings.Perm_UserMute),
        new(Permission.UserKick, Strings.Perm_UserKick),
        new(Permission.UserBan, Strings.Perm_UserBan),
        new(Permission.UserDelete, Strings.Perm_UserDelete),
        new(Permission.UsersView, Strings.Perm_UsersView),
        new(Permission.BansView, Strings.Perm_BansView),
        new(Permission.GroupsView, Strings.Perm_GroupsView),
        new(Permission.GroupsCreate, Strings.Perm_GroupsCreate),
        new(Permission.GroupsManage, Strings.Perm_GroupsManage),
        new(Permission.GroupsDelete, Strings.Perm_GroupsDelete),
        new(Permission.GroupsAssign, Strings.Perm_GroupsAssign),
        new(Permission.ServerConfig, Strings.Perm_ServerConfig),
        new(Permission.LogsView, Strings.Perm_LogsView),
        new(Permission.LogsDownload, Strings.Perm_LogsDownload),
        new(Permission.BackupsManage, Strings.Perm_BackupsManage), // Package 89
        new(Permission.ChatServer, Strings.Perm_ChatServer),
        new(Permission.ChatChannel, Strings.Perm_ChatChannel),
        new(Permission.ChatPrivate, Strings.Perm_ChatPrivate),
        new(Permission.ChannelPasswordBypass, Strings.Perm_ChannelPasswordBypass), // Package 94
        new(Permission.ChannelJoinFull, Strings.Perm_ChannelJoinFull),
    ];
}
