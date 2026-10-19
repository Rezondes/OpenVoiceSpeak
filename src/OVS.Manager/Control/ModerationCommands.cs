using OVS.Manager.Audit;
using OVS.Manager.Localization;
using OVS.Shared.Protocol.Messages;
using OVS.Shared.Protocol.Messages.Moderation;
using OVS.Shared.Protocol.Messages.Users;
using Error = OVS.Shared.Protocol.Messages.Connection.Error;

namespace OVS.Manager.Control;

/// <summary>
/// Package M13 (Packages 8, 71, 72, 80, 166): the moderation of a server through the service session, as its server
/// admin, who may act on everyone; each action also writes a line in the manager's audit log.
/// </summary>
internal sealed class ModerationCommands(ServerConnections connections, AuditLog audit)
{
    /// <summary>AC1: the users online but the service session, by nickname; empty while not connected.</summary>
    public IReadOnlyList<UserInfo> Online(string serverId) =>
        connections.LinkOf(serverId)?.Read(mirror => (IReadOnlyList<UserInfo>)[.. mirror.Users.Values.Where(user => user.SessionId != mirror.SelfId)
            .OrderBy(user => user.Nickname, StringComparer.OrdinalIgnoreCase)], []) ?? [];

    /// <summary>The channels by id, for names and the move.</summary>
    public IReadOnlyDictionary<Guid, string> Channels(string serverId) =>
        connections.LinkOf(serverId)?.Read(mirror => (IReadOnlyDictionary<Guid, string>)mirror.Channels.Values.ToDictionary(channel => channel.Id, channel => channel.Name),
            new Dictionary<Guid, string>()) ?? new Dictionary<Guid, string>();

    /// <summary>AC1: disconnects the session with <paramref name="reason"/>.</summary>
    public Task<string?> KickAsync(string serverId, UserInfo user, string reason, string actor) =>
        ActAsync(serverId, new KickSession(user.SessionId, reason), message => message is UserLeft left && left.SessionId == user.SessionId,
            actor, AuditActions.UserKicked, user.Nickname, reason);

    /// <summary>AC1: bans the session's user, for <paramref name="minutes"/> or for good, with the address when asked.</summary>
    public Task<string?> BanAsync(string serverId, UserInfo user, string reason, int? minutes, bool includeIp, string actor) =>
        ActAsync(serverId, new BanSession(user.SessionId, reason, minutes, includeIp), message => message is UserLeft left && left.SessionId == user.SessionId,
            actor, AuditActions.UserBanned, user.Nickname, $"{reason} ({Duration(minutes)})");

    /// <summary>AC1: the server mute, for <paramref name="minutes"/> or until lifted; or lifts it.</summary>
    public Task<string?> MuteAsync(string serverId, UserInfo user, bool muted, int? minutes, string actor) =>
        ActAsync(serverId, new SetSessionServerMute(user.SessionId, muted, muted ? minutes : null),
            message => message is UserUpdated updated && updated.User.SessionId == user.SessionId && updated.User.ServerMuted == muted,
            actor, muted ? AuditActions.UserMuted : AuditActions.UserUnmuted, user.Nickname, muted ? Duration(minutes) : null);

    /// <summary>AC1: moves the session into <paramref name="channelId"/>.</summary>
    public Task<string?> MoveAsync(string serverId, UserInfo user, Guid channelId, string actor) =>
        ActAsync(serverId, new MoveUser(user.SessionId, channelId),
            message => message is UserUpdated updated && updated.User.SessionId == user.SessionId && updated.User.ChannelId == channelId,
            actor, AuditActions.UserMoved, user.Nickname, Channels(serverId).GetValueOrDefault(channelId));

    /// <summary>AC2: the known users with first and last login, online and speech time and groups.</summary>
    public async Task<KnownUserPage> KnownUsersAsync(string serverId)
    {
        if (Connected(serverId) is not { } link) return new KnownUserPage([], Strings.Link_NotConnected);
        return await link.RequestAsync(new ListUsers()) switch
        {
            UserList list => new KnownUserPage(list.Users, null),
            Error error => new KnownUserPage([], ServerTexts.Refusal(error)),
            _ => new KnownUserPage([], Strings.Link_NotConnected),
        };
    }

    /// <summary>Removes a known user with its groups, statistics and bans (Package 72); returns null or why it was refused.</summary>
    public Task<string?> DeleteKnownUserAsync(string serverId, KnownUserInfo user, string actor) =>
        ActConfirmedAsync(serverId, new DeleteKnownUser(user.Fingerprint), actor, AuditActions.KnownUserDeleted, user.LastNickname, null);

    /// <summary>AC3: the bans, active and history.</summary>
    public async Task<BanPage> BansAsync(string serverId)
    {
        if (Connected(serverId) is not { } link) return new BanPage([], Strings.Link_NotConnected);
        return Bans(await link.RequestAsync(new ListBans()));
    }

    /// <summary>AC3: lifts the ban; the answer is the fresh list.</summary>
    public async Task<BanPage> UnbanAsync(string serverId, BanInfo ban, string actor)
    {
        if (Connected(serverId) is not { } link) return new BanPage([], Strings.Link_NotConnected);
        var page = Bans(await link.RequestAsync(new Unban(ban.Id)));
        if (page.Error is null) await audit.WriteAsync(actor, AuditActions.UserUnbanned, ban.Nickname, serverId, ban.Reason);
        return page;
    }

    /// <summary>Sends <paramref name="action"/>, which the server confirms to nobody, and waits until it was taken; returns null or why it was refused.</summary>
    internal async Task<string?> ActConfirmedAsync(string serverId, Request action, string actor, string auditAction, string target, string? detail)
    {
        if (Connected(serverId) is not { } link) return Strings.Link_NotConnected;
        if (await link.RequestConfirmedAsync(action) is { } error) return ServerTexts.Refusal(error);
        await audit.WriteAsync(actor, auditAction, target, serverId, detail);
        return null;
    }

    async Task<string?> ActAsync(string serverId, Request request, Func<Message, bool> done, string actor, string auditAction, string target, string? detail)
    {
        if (Connected(serverId) is not { } link) return Strings.Link_NotConnected;
        if (await link.RequestAsync(request, done) is Error error) return ServerTexts.Refusal(error);
        await audit.WriteAsync(actor, auditAction, target, serverId, detail);
        return null;
    }

    ServerLink? Connected(string serverId) => connections.LinkOf(serverId) is { Status: ServerLinkStatus.Connected } link ? link : null;

    static BanPage Bans(Message answer) => answer switch
    {
        BanList list => new BanPage(list.Bans, null),
        Error error => new BanPage([], ServerTexts.Refusal(error)),
        _ => new BanPage([], Strings.Link_NotConnected),
    };

    static string Duration(int? minutes) => minutes is { } length ? $"{length} min" : "permanent";
}
