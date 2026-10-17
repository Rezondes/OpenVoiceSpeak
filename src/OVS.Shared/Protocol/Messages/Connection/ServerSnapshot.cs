using OVS.Shared.Protocol.Messages.Channels;
using OVS.Shared.Protocol.Messages.Groups;
using OVS.Shared.Protocol.Messages.Links;
using OVS.Shared.Protocol.Messages.ServerInfo;
using OVS.Shared.Protocol.Messages.Users;

namespace OVS.Shared.Protocol.Messages.Connection;

/// <summary>Everything a client shows of a server, sent once with the welcome.</summary>
/// <param name="Settings">The server's name, texts and logo.</param>
/// <param name="DefaultChannelId">The channel new users start in.</param>
/// <param name="Channels">The channels.</param>
/// <param name="Links">The links between channels.</param>
/// <param name="Groups">The groups in their order.</param>
/// <param name="Users">The users online.</param>
/// <param name="HasServerAdmin">Package 181: someone holds the Server-Admin group, so no server admin token exists.</param>
public sealed record ServerSnapshot(
    ServerSettingsInfo Settings, Guid DefaultChannelId,
    IReadOnlyList<ChannelInfo> Channels, IReadOnlyList<LinkInfo> Links,
    IReadOnlyList<GroupInfo> Groups, IReadOnlyList<UserInfo> Users, bool HasServerAdmin = false);
