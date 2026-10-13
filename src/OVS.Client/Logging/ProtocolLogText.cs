using OVS.Client.Net;
using OVS.Shared.Protocol;
using OVS.Shared.Protocol.Messages;
using OVS.Shared.Protocol.Messages.Channels;
using OVS.Shared.Protocol.Messages.Chat;
using OVS.Shared.Protocol.Messages.Connection;
using OVS.Shared.Protocol.Messages.Groups;
using OVS.Shared.Protocol.Messages.Links;
using OVS.Shared.Protocol.Messages.Moderation;
using OVS.Shared.Protocol.Messages.ServerInfo;
using OVS.Shared.Protocol.Messages.Users;

namespace OVS.Client.Logging;

/// <summary>
/// The client log's words for a server message or an own request, with the names from the mirror; <see cref="ClientLog"/> writes them.
/// </summary>
internal static class ProtocolLogText
{
    /// <summary>A server message in words, with names from the mirror. Call before the mirror applies it.</summary>
    /// <returns>Null for messages that are logged elsewhere (errors appear as notices).</returns>
    internal static string? Describe(Message message, StateMirror mirror)
    {
        switch (message)
        {
            case ChannelAdded { Channel.Kind: ChannelKind.Separator }:
                return "Separator created";
            case ChannelAdded channelAdded:
                return $"Channel '{channelAdded.Channel.Name}' created";
            case ChannelUpdated { Channel.Kind: ChannelKind.Separator }:
                return "Separator moved";
            case ChannelUpdated channelUpdated:
                var before = mirror.Channels.GetValueOrDefault(channelUpdated.Channel.Id);
                if (before is not null && before.IsMuted != channelUpdated.Channel.IsMuted)
                    return $"Channel '{channelUpdated.Channel.Name}' {(channelUpdated.Channel.IsMuted ? "is muted now" : "is no longer muted")}";
                return before is null || before.Name == channelUpdated.Channel.Name
                    ? $"Channel '{channelUpdated.Channel.Name}' changed"
                    : $"Channel '{before.Name}' renamed to '{channelUpdated.Channel.Name}'";
            case ChannelRemoved channelRemoved when IsSeparator(channelRemoved.ChannelId, mirror):
                return "Separator deleted";
            case ChannelRemoved channelRemoved:
                return $"Channel {DescribeChannel(channelRemoved.ChannelId, mirror)} deleted";
            case UserJoined userJoined:
                return $"{userJoined.User.Nickname} connected, channel {DescribeChannel(userJoined.User.ChannelId, mirror)}";
            case UserUpdated userUpdated:
                return DescribeUserUpdate(mirror.Users.GetValueOrDefault(userUpdated.User.SessionId), userUpdated.User, mirror);
            case UserLeft userLeft:
                return $"{DescribeSession(userLeft.SessionId, mirror)} disconnected";
            case ChannelsLinked channelsLinked:
                return $"Channels {DescribeChannel(channelsLinked.A, mirror)} and {DescribeChannel(channelsLinked.B, mirror)} linked";
            case ChannelsUnlinked channelsUnlinked:
                return $"Link between {DescribeChannel(channelsUnlinked.A, mirror)} and {DescribeChannel(channelsUnlinked.B, mirror)} removed";
            case GroupsChanged groupsChanged:
                return $"Groups changed: {string.Join(", ", groupsChanged.Groups.Select(groupInfo => groupInfo.Name))}";
            case ServerSettingsChanged serverSettingsChanged:
                return $"Server settings changed: name '{serverSettingsChanged.Settings.Name}', {(serverSettingsChanged.Settings.HasPassword ? "with" : "without")} password";
            case UserList userList:
                return $"User list received ({userList.Users.Count} entries)";
            case BanList banList:
                return $"Ban list received ({banList.Bans.Count} entries)";
            case ChatMessage chatMessage:
                return chatMessage.Target switch
                {
                    ChatTarget.Server => $"Chat (General) {chatMessage.FromNickname}: {chatMessage.Text}",
                    ChatTarget.Channel => $"Chat in {DescribeChannel(chatMessage.ChannelId ?? Guid.Empty, mirror)} {chatMessage.FromNickname}: {chatMessage.Text}",
                    _ => $"Private {chatMessage.FromNickname} to {DescribeSession(chatMessage.ToSessionId ?? 0, mirror)}: {chatMessage.Text}",
                };
            case ServerIcon serverIcon:
                return serverIcon.PngBase64 is null ? "Server has no logo" : $"Server logo received ({Base64Text.Decoded(serverIcon.PngBase64.Length) / 1024} KB)";
            case Error { ErrorId: { } errorId } error:
                return $"Error {error.Code} ({error.RequestId}), error ID {errorId}"; // Package 143: the ID the admin searches for
            case Error { Detail: { Length: > 0 } detail } error:
                return $"Error {error.Code} ({error.RequestId}): {detail}"; // A45: the UI shows its own text, the detail only lands here
            case Error:
                return null;
            default:
                return message.GetType().Name;
        }
    }

    static string DescribeUserUpdate(UserInfo? before, UserInfo after, StateMirror mirror)
    {
        if (before is null) return $"{after.Nickname} updated";
        var changes = new List<string>();
        if (before.ChannelId != after.ChannelId) changes.Add($"moves from {DescribeChannel(before.ChannelId, mirror)} to {DescribeChannel(after.ChannelId, mirror)}");
        if (before.SelfMuted != after.SelfMuted) changes.Add(after.SelfMuted ? "muted" : "no longer muted");
        if (before.SelfDeafened != after.SelfDeafened) changes.Add(after.SelfDeafened ? "deafened" : "no longer deafened");
        if (before.ServerMuted != after.ServerMuted) changes.Add(after.ServerMuted ? "muted by the server" : "unmuted by the server");
        if (!before.GroupIds.SequenceEqual(after.GroupIds) || before.Permissions != after.Permissions)
            changes.Add(after.Permissions == OVS.Shared.Permissions.Permission.None ? "groups changed" : $"permissions now {after.Permissions}"); // Package 92: others come without rights
        return changes.Count == 0 ? $"{after.Nickname} updated" : $"{after.Nickname} {string.Join(", ", changes)}";
    }

    /// <summary>An own request in words. Passwords and tokens are never part of the text.</summary>
    internal static string Describe(Request request, StateMirror mirror)
    {
        static string YesNo(bool value) => value ? "yes" : "no";
        static string BanTerms(int? minutes, bool includeIp) => $"{(minutes is { } count ? $"{count} minutes" : "permanently")}{(includeIp ? ", with IP" : "")}";
        static string ServerMute(bool muted) => muted ? "Server-mute" : "Server-unmute";

        var text = request switch
        {
            JoinChannel joinChannel => $"Join channel {DescribeChannel(joinChannel.ChannelId, mirror)}",
            CreateChannel { Kind: ChannelKind.Separator } => "Create separator",
            CreateChannel createChannel => $"Create channel '{createChannel.Name}'",
            EditChannel editChannel => $"Edit channel {DescribeChannel(editChannel.ChannelId, mirror)}: name '{editChannel.Name}', order {editChannel.Order}, muted {YesNo(editChannel.IsMuted)}, max. users {editChannel.MaxUsers}, bitrate {editChannel.Bitrate} kbps",
            DeleteChannel deleteChannel => IsSeparator(deleteChannel.ChannelId, mirror) ? "Delete separator" : $"Delete channel {DescribeChannel(deleteChannel.ChannelId, mirror)}",
            SetChannelLinks setChannelLinks => $"Change links: set {setChannelLinks.Add.Count}, remove {setChannelLinks.Remove.Count}",
            ReorderChannels reorderChannels => $"Reorder channels: {string.Join(", ", reorderChannels.ChannelIds.Select(id => DescribeChannel(id, mirror)))}",
            ReorderGroups reorderGroups => $"Reorder groups: {string.Join(", ", reorderGroups.GroupIds.Select(id => DescribeGroup(id, mirror)))}",
            MoveUser moveUser => $"Move {DescribeSession(moveUser.SessionId, mirror)} to {DescribeChannel(moveUser.ChannelId, mirror)}",
            SetSelfState setSelfState => $"Own state: muted {YesNo(setSelfState.Muted)}, deafened {YesNo(setSelfState.Deafened)}",
            LinkChannels linkChannels => $"Link channels {DescribeChannel(linkChannels.A, mirror)} and {DescribeChannel(linkChannels.B, mirror)}",
            UnlinkChannels unlinkChannels => $"Remove link between {DescribeChannel(unlinkChannels.A, mirror)} and {DescribeChannel(unlinkChannels.B, mirror)}",
            CreateGroup createGroup => $"Create group '{createGroup.Name}' ({createGroup.Permissions})",
            UpdateGroup updateGroup => $"Change group {DescribeGroup(updateGroup.GroupId, mirror)}: name '{updateGroup.Name}', permissions {updateGroup.Permissions}",
            DeleteGroup deleteGroup => $"Delete group {DescribeGroup(deleteGroup.GroupId, mirror)}",
            AssignGroup assignGroup => $"Assign group {DescribeGroup(assignGroup.GroupId, mirror)} to {DescribePerson(assignGroup.Fingerprint, mirror)}",
            UnassignGroup unassignGroup => $"Remove group {DescribeGroup(unassignGroup.GroupId, mirror)} from {DescribePerson(unassignGroup.Fingerprint, mirror)}",
            ListUsers => "Request user list",
            ListBans => "Request ban list",
            RedeemAdminToken => "Redeem admin token",
            SetServerIcon setServerIcon => setServerIcon.PngBase64 is null ? "Remove server logo" : "Set server logo",
            GetServerIcon => "Request server logo",
            SendChat sendChat => sendChat.Target switch
            {
                ChatTarget.Server => "Chat to everyone",
                ChatTarget.Channel => "Chat in the channel",
                _ => $"Private message to {DescribeSession(sendChat.ToSessionId ?? 0, mirror)}",
            },
            UpdateServerSettings updateServerSettings => $"Change server settings: name '{updateServerSettings.Name}'" + updateServerSettings.Password switch
            {
                null => "",
                "" => ", remove password",
                _ => ", set password",
            },
            KickSession kickSession => $"Kick {DescribeSession(kickSession.SessionId, mirror)}: {kickSession.Reason}",
            BanSession banSession => $"Ban {DescribeSession(banSession.SessionId, mirror)} ({BanTerms(banSession.DurationMinutes, banSession.IncludeIp)}): {banSession.Reason}",
            BanKnownUser banKnownUser => $"Ban {DescribePerson(banKnownUser.Fingerprint, mirror)} ({BanTerms(banKnownUser.DurationMinutes, banKnownUser.IncludeIp)}): {banKnownUser.Reason}",
            DeleteKnownUser deleteKnownUser => $"Delete the user data of {DescribePerson(deleteKnownUser.Fingerprint, mirror)}",
            Unban unban => $"Lift ban {unban.BanId}",
            SetSessionServerMute setSessionServerMute => $"{ServerMute(setSessionServerMute.Muted)} {DescribeSession(setSessionServerMute.SessionId, mirror)}",
            SetKnownUserServerMute setKnownUserServerMute => $"{ServerMute(setKnownUserServerMute.Muted)} user {ShortenFingerprint(setKnownUserServerMute.Fingerprint)}",
            _ => request.GetType().Name,
        };
        return $"Request {request.RequestId}: {text}";
    }

    static bool IsSeparator(Guid id, StateMirror mirror) => mirror.Channels.GetValueOrDefault(id) is { Kind: ChannelKind.Separator };

    /// <summary>The channel's quoted name (its id if unknown), or "(separator)" (Package 111).</summary>
    static string DescribeChannel(Guid id, StateMirror mirror) =>
        IsSeparator(id, mirror) ? "(separator)" : $"'{mirror.Channels.GetValueOrDefault(id)?.Name ?? id.ToString()}'";

    /// <summary>The session's nickname, or "#id" if it is not in the mirror.</summary>
    static string DescribeSession(uint id, StateMirror mirror) => mirror.Users.GetValueOrDefault(id)?.Nickname ?? $"#{id}";

    /// <summary>The group's quoted name, or its quoted id if unknown.</summary>
    static string DescribeGroup(Guid id, StateMirror mirror) => $"'{mirror.Groups.FirstOrDefault(groupInfo => groupInfo.Id == id)?.Name ?? id.ToString()}'";

    /// <summary>The nickname of the online user with this fingerprint, else the shortened fingerprint.</summary>
    static string DescribePerson(string fingerprint, StateMirror mirror) =>
        mirror.Users.Values.FirstOrDefault(user => user.Fingerprint == fingerprint)?.Nickname ?? ShortenFingerprint(fingerprint);

    static string ShortenFingerprint(string fingerprint) => fingerprint[..Math.Min(12, fingerprint.Length)];
}
