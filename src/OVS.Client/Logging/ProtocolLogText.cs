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
            case ChannelAdded m:
                return $"Channel '{m.Channel.Name}' created";
            case ChannelUpdated { Channel.Kind: ChannelKind.Separator }:
                return "Separator moved";
            case ChannelUpdated m:
                var before = mirror.Channels.GetValueOrDefault(m.Channel.Id);
                if (before is not null && before.IsMuted != m.Channel.IsMuted)
                    return $"Channel '{m.Channel.Name}' {(m.Channel.IsMuted ? "is muted now" : "is no longer muted")}";
                return before is null || before.Name == m.Channel.Name
                    ? $"Channel '{m.Channel.Name}' changed"
                    : $"Channel '{before.Name}' renamed to '{m.Channel.Name}'";
            case ChannelRemoved m when IsSeparator(m.ChannelId, mirror):
                return "Separator deleted";
            case ChannelRemoved m:
                return $"Channel {DescribeChannel(m.ChannelId, mirror)} deleted";
            case UserJoined m:
                return $"{m.User.Nickname} connected, channel {DescribeChannel(m.User.ChannelId, mirror)}";
            case UserUpdated m:
                return DescribeUserUpdate(mirror.Users.GetValueOrDefault(m.User.SessionId), m.User, mirror);
            case UserLeft m:
                return $"{DescribeSession(m.SessionId, mirror)} disconnected";
            case ChannelsLinked m:
                return $"Channels {DescribeChannel(m.A, mirror)} and {DescribeChannel(m.B, mirror)} linked";
            case ChannelsUnlinked m:
                return $"Link between {DescribeChannel(m.A, mirror)} and {DescribeChannel(m.B, mirror)} removed";
            case GroupsChanged m:
                return $"Groups changed: {string.Join(", ", m.Groups.Select(g => g.Name))}";
            case ServerSettingsChanged m:
                return $"Server settings changed: name '{m.Settings.Name}', {(m.Settings.HasPassword ? "with" : "without")} password";
            case UserList m:
                return $"User list received ({m.Users.Count} entries)";
            case BanList m:
                return $"Ban list received ({m.Bans.Count} entries)";
            case ChatMessage m:
                return m.Target switch
                {
                    ChatTarget.Server => $"Chat (General) {m.FromNickname}: {m.Text}",
                    ChatTarget.Channel => $"Chat in {DescribeChannel(m.ChannelId ?? Guid.Empty, mirror)} {m.FromNickname}: {m.Text}",
                    _ => $"Private {m.FromNickname} to {DescribeSession(m.ToSessionId ?? 0, mirror)}: {m.Text}",
                };
            case ServerIcon m:
                return m.PngBase64 is null ? "Server has no logo" : $"Server logo received ({Base64Text.Decoded(m.PngBase64.Length) / 1024} KB)";
            case Error { Detail: { Length: > 0 } detail } e:
                return $"Error {e.Code} ({e.RequestId}): {detail}"; // A45: the UI shows its own text, the detail only lands here
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
        static string YesNo(bool b) => b ? "yes" : "no";
        static string BanTerms(int? minutes, bool includeIp) => $"{(minutes is { } m ? $"{m} minutes" : "permanently")}{(includeIp ? ", with IP" : "")}";
        static string ServerMute(bool muted) => muted ? "Server-mute" : "Server-unmute";

        var text = request switch
        {
            JoinChannel r => $"Join channel {DescribeChannel(r.ChannelId, mirror)}",
            CreateChannel { Kind: ChannelKind.Separator } => "Create separator",
            CreateChannel r => $"Create channel '{r.Name}'",
            EditChannel r => $"Edit channel {DescribeChannel(r.ChannelId, mirror)}: name '{r.Name}', order {r.Order}, muted {YesNo(r.IsMuted)}, max. users {r.MaxUsers}, bitrate {r.Bitrate} kbps",
            DeleteChannel r => IsSeparator(r.ChannelId, mirror) ? "Delete separator" : $"Delete channel {DescribeChannel(r.ChannelId, mirror)}",
            SetChannelLinks r => $"Change links: set {r.Add.Count}, remove {r.Remove.Count}",
            ReorderChannels r => $"Reorder channels: {string.Join(", ", r.ChannelIds.Select(id => DescribeChannel(id, mirror)))}",
            ReorderGroups r => $"Reorder groups: {string.Join(", ", r.GroupIds.Select(id => DescribeGroup(id, mirror)))}",
            MoveUser r => $"Move {DescribeSession(r.SessionId, mirror)} to {DescribeChannel(r.ChannelId, mirror)}",
            SetSelfState r => $"Own state: muted {YesNo(r.Muted)}, deafened {YesNo(r.Deafened)}",
            LinkChannels r => $"Link channels {DescribeChannel(r.A, mirror)} and {DescribeChannel(r.B, mirror)}",
            UnlinkChannels r => $"Remove link between {DescribeChannel(r.A, mirror)} and {DescribeChannel(r.B, mirror)}",
            CreateGroup r => $"Create group '{r.Name}' ({r.Permissions})",
            UpdateGroup r => $"Change group {DescribeGroup(r.GroupId, mirror)}: name '{r.Name}', permissions {r.Permissions}",
            DeleteGroup r => $"Delete group {DescribeGroup(r.GroupId, mirror)}",
            AssignGroup r => $"Assign group {DescribeGroup(r.GroupId, mirror)} to {DescribePerson(r.Fingerprint, mirror)}",
            UnassignGroup r => $"Remove group {DescribeGroup(r.GroupId, mirror)} from {DescribePerson(r.Fingerprint, mirror)}",
            ListUsers => "Request user list",
            ListBans => "Request ban list",
            RedeemAdminToken => "Redeem admin token",
            SetServerIcon r => r.PngBase64 is null ? "Remove server logo" : "Set server logo",
            GetServerIcon => "Request server logo",
            SendChat r => r.Target switch
            {
                ChatTarget.Server => "Chat to everyone",
                ChatTarget.Channel => "Chat in the channel",
                _ => $"Private message to {DescribeSession(r.ToSessionId ?? 0, mirror)}",
            },
            UpdateServerSettings r => $"Change server settings: name '{r.Name}'" + r.Password switch
            {
                null => "",
                "" => ", remove password",
                _ => ", set password",
            },
            KickSession r => $"Kick {DescribeSession(r.SessionId, mirror)}: {r.Reason}",
            BanSession r => $"Ban {DescribeSession(r.SessionId, mirror)} ({BanTerms(r.DurationMinutes, r.IncludeIp)}): {r.Reason}",
            BanKnownUser r => $"Ban {DescribePerson(r.Fingerprint, mirror)} ({BanTerms(r.DurationMinutes, r.IncludeIp)}): {r.Reason}",
            DeleteKnownUser r => $"Delete the user data of {DescribePerson(r.Fingerprint, mirror)}",
            Unban r => $"Lift ban {r.BanId}",
            SetSessionServerMute r => $"{ServerMute(r.Muted)} {DescribeSession(r.SessionId, mirror)}",
            SetKnownUserServerMute r => $"{ServerMute(r.Muted)} user {ShortenFingerprint(r.Fingerprint)}",
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
    static string DescribeGroup(Guid id, StateMirror mirror) => $"'{mirror.Groups.FirstOrDefault(g => g.Id == id)?.Name ?? id.ToString()}'";

    /// <summary>The nickname of the online user with this fingerprint, else the shortened fingerprint.</summary>
    static string DescribePerson(string fingerprint, StateMirror mirror) =>
        mirror.Users.Values.FirstOrDefault(u => u.Fingerprint == fingerprint)?.Nickname ?? ShortenFingerprint(fingerprint);

    static string ShortenFingerprint(string fingerprint) => fingerprint[..Math.Min(12, fingerprint.Length)];
}
