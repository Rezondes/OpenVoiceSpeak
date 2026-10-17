using OVS.Client.Net;
using OVS.Shared.Permissions;
using OVS.Shared.Protocol.Messages.Channels;
using OVS.Shared.Protocol.Messages.Connection;
using OVS.Shared.Protocol.Messages.Groups;
using OVS.Shared.Protocol.Messages.Links;
using OVS.Shared.Protocol.Messages.ServerInfo;
using OVS.Shared.Protocol.Messages.Users;

namespace OVS.Tests.TestSupport;

/// <summary>What a server sends a client on connecting, built for the client tests without a network.</summary>
static class Snapshots
{
    /// <summary>
    /// A user with the fingerprint "fp" + id, self-muted only with <paramref name="selfMuted"/>, neither deafened nor
    /// server-muted, in the guest group unless <paramref name="groups"/> says otherwise.
    /// </summary>
    public static UserInfo User(uint id, string nick, Guid channel, Permission perms, IReadOnlyList<Guid>? groups = null, bool selfMuted = false) =>
        new(id, "fp" + id, nick, channel, selfMuted, false, false, perms, groups ?? [WellKnownGroups.Guest]);

    /// <summary>A server named <paramref name="name"/> without a password, icon or limits.</summary>
    public static ServerSnapshot Snapshot(string name, string welcome, Guid defaultChannel, IReadOnlyList<ChannelInfo> channels,
        IReadOnlyList<GroupInfo> groups, IReadOnlyList<UserInfo> users, IReadOnlyList<LinkInfo>? links = null, bool hasServerAdmin = false) =>
        new(new ServerSettingsInfo(name, welcome, false), defaultChannel, channels, links ?? [], groups, users, hasServerAdmin);

    /// <summary>The welcome of session 1 (the own user) without a voice key.</summary>
    public static Welcome Welcome(ServerSnapshot snapshot) => new(1, "", snapshot);

    /// <summary>The client's mirror right after <see cref="Welcome"/>.</summary>
    public static StateMirror Mirror(ServerSnapshot snapshot) => new(Welcome(snapshot));
}
